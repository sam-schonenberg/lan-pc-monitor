using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Diagnostics;
using Xunit;

namespace PCMonitor.Service.Tests.Diagnostics;

public sealed class WindowsDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pcmonitor-diagnostics-{Guid.NewGuid():N}");

    [Theory]
    [InlineData((byte)1, WindowsDiagnosticSeverity.Critical)]
    [InlineData((byte)2, WindowsDiagnosticSeverity.Error)]
    public void ClassifierRetainsOnlyErrorAndCritical(byte level, WindowsDiagnosticSeverity expected) =>
        Assert.Equal(expected, WindowsEventClassifier.Severity(level));

    [Theory]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    public void ClassifierRejectsWarningsAndInformation(byte level) =>
        Assert.Null(WindowsEventClassifier.Severity(level));

    [Fact]
    public void StorePersistsDeduplicatesAndPagesNewestFirst()
    {
        var path = Path.Combine(_directory, "events.jsonl");
        var options = Options.Create(new WindowsDiagnosticsOptions { StoreFilePath = path });
        var store = new WindowsDiagnosticStore(options, TimeProvider.System,
            NullLogger<WindowsDiagnosticStore>.Instance);
        var first = Event(100, 41, WindowsDiagnosticSeverity.Critical);
        var second = Event(101, 7, WindowsDiagnosticSeverity.Error);
        store.AddBatch(new("System", [first, second, first], new("System", 101), false));

        var page = store.Query(null, 1, null, null, null, null);
        Assert.Single(page.Events);
        Assert.Equal(101, page.Events[0].RecordId);
        Assert.Equal("Power management error", page.Events[0].Title);
        Assert.NotEmpty(page.Events[0].Summary);
        Assert.True(page.HasMore);
        Assert.NotNull(page.PreviousSequence);

        var restored = new WindowsDiagnosticStore(options, TimeProvider.System,
            NullLogger<WindowsDiagnosticStore>.Instance);
        Assert.Equal(2, restored.GetInventory().Count);
        Assert.Equal(101, restored.GetCheckpoint("System")?.RecordId);
    }

    [Fact]
    public void StoreAppliesApiFilters()
    {
        var options = Options.Create(new WindowsDiagnosticsOptions
            { StoreFilePath = Path.Combine(_directory, "filtered.jsonl") });
        var store = new WindowsDiagnosticStore(options, TimeProvider.System,
            NullLogger<WindowsDiagnosticStore>.Instance);
        store.AddBatch(new("System",
        [
            Event(1, 41, WindowsDiagnosticSeverity.Critical),
            Event(2, 7, WindowsDiagnosticSeverity.Error) with { Provider = "Disk", Category = "storage" }
        ], new("System", 2), false));

        var result = store.Query(null, null, WindowsDiagnosticSeverity.Critical, "system",
            "Microsoft-Windows-Kernel-Power", 41);
        Assert.Single(result.Events);
        Assert.Equal("unexpected-shutdown", result.Events[0].Category);
        Assert.Equal("Unexpected shutdown", result.Events[0].Title);
        Assert.Contains("normal shutdown", result.Events[0].Summary);
    }

    [Theory]
    [InlineData("nvlddmkm", 14, "NVIDIA display driver error")]
    [InlineData("stornvme", 11, "NVMe controller error")]
    [InlineData("Microsoft-Windows-WHEA-Logger", 18, "Hardware error")]
    public void ClassifierProvidesReadableDescriptions(string provider, int eventId, string expectedTitle)
    {
        var description = WindowsEventClassifier.Describe(provider, eventId);
        Assert.Equal(expectedTitle, description.Title);
        Assert.NotEmpty(description.Summary);
    }

    private static WindowsDiagnosticEvent Event(long recordId, int eventId, WindowsDiagnosticSeverity severity) =>
        new(0, DateTimeOffset.UtcNow, "System", "Microsoft-Windows-Kernel-Power", eventId, 0,
            severity == WindowsDiagnosticSeverity.Critical ? (byte)1 : (byte)2, recordId, severity,
            eventId == 41 ? "unexpected-shutdown" : "power");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
