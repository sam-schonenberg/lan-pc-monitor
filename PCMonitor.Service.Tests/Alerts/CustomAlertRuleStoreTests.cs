using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Alerts;
using PCMonitor.Service.Models;
using Xunit;

namespace PCMonitor.Service.Tests.Alerts;

public sealed class CustomAlertRuleStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pcmonitor-custom-rules-{Guid.NewGuid():N}.json");

    [Fact]
    public void PersistsUpdatesAndDeletesRules()
    {
        var store = Create();
        var created = store.Create(Request("SSD hot", 70, 65));
        Assert.Equal(created, Create().Get(created.Id));

        var updated = store.Update(created.Id, Request("SSD very hot", 80, 75));
        Assert.NotNull(updated);
        Assert.Equal(80, Create().Get(created.Id)!.Threshold);

        Assert.True(store.Remove(created.Id));
        Assert.Empty(Create().GetAll());
    }

    [Theory]
    [InlineData("above", 70, 70)]
    [InlineData("above", 70, 75)]
    [InlineData("below", 300, 200)]
    public void RejectsInvalidHysteresis(string direction, double threshold, double reset)
    {
        var request = Request("Rule", threshold, reset) with
        { Direction = direction == "above" ? AlertRuleDirection.Above : AlertRuleDirection.Below };
        Assert.NotNull(CustomAlertRuleStore.Validate(request));
    }

    [Fact]
    public void RejectsTriggerThatIsTooCloseToCurrentReading()
    {
        var error = Create().ValidateForSensor(Request("Fan", 520, 400) with
        { SensorId = "/board/0/fan/0" }, new SensorReading("/board/0/fan/0", "Board", "CPU Fan", "Fan", 500, "RPM"));
        Assert.Contains("at least 600 RPM", error);
    }

    [Fact]
    public void AllowsAtMostTwoRulesPerSensor()
    {
        var store = Create();
        store.Create(Request("One", 70, 65));
        store.Create(Request("Two", 80, 75));
        var error = store.ValidateForSensor(Request("Three", 90, 85),
            new SensorReading("/storage/0/temperature/0", "SSD", "Temperature", "Temperature", 40, "°C"));
        Assert.Contains("At most 2", error);
    }

    [Fact]
    public void PushRulesRequireThirtySecondDuration()
    {
        Assert.Contains("30", CustomAlertRuleStore.Validate(Request("Rule", 70, 65) with
        { MinimumDurationSeconds = 29 })!);
    }

    private CustomAlertRuleStore Create() => new(Options.Create(new AlertOptions { RuleStoreFile = _path }),
        NullLogger<CustomAlertRuleStore>.Instance);
    private static CustomAlertRuleRequest Request(string name, double threshold, double reset) => new(name,
        "/storage/0/temperature/0", AlertRuleDirection.Above, threshold, reset, 30, AlertSeverity.Warning);

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }
}
