using System.Diagnostics.Eventing.Reader;
using System.Security;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;

namespace PCMonitor.Service.Diagnostics;

#pragma warning disable CA1416 // Every EventLog API call is protected by the OperatingSystem.IsWindows guard.
public sealed class WindowsEventLogSource : IWindowsEventSource
{
    private readonly WindowsDiagnosticsOptions _options;

    public WindowsEventLogSource(IOptions<WindowsDiagnosticsOptions> options) => _options = options.Value;

    public Task<WindowsEventBatch> ReadAfterAsync(string channel, WindowsEventCheckpoint? checkpoint,
        int maximumCount, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(new WindowsEventBatch(channel, [], checkpoint, false));
        return Task.Run(() => Read(channel, checkpoint, maximumCount, cancellationToken), cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private WindowsEventBatch Read(string channel, WindowsEventCheckpoint? checkpoint, int maximumCount,
        CancellationToken cancellationToken)
    {
        var providers = _options.Providers.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => $"Provider[@Name='{EscapeXPathLiteral(x)}']").ToArray();
        if (providers.Length == 0) return new(channel, [], checkpoint, false);

        var lowerBound = checkpoint is null
            ? $"TimeCreated[timediff(@SystemTime) &lt;= {Math.Max(1, _options.InitialLookbackHours) * 3_600_000L}]"
            : $"EventRecordID &gt; {checkpoint.RecordId}";
        var xpath = $"*[System[(Level=1 or Level=2) and ({string.Join(" or ", providers)}) and {lowerBound}]]";
        using var reader = new EventLogReader(new EventLogQuery(channel, PathType.LogName, xpath)
        { ReverseDirection = false, TolerateQueryErrors = true });
        var events = new List<WindowsDiagnosticEvent>();
        long? lastRecordId = checkpoint?.RecordId;
        var hasMore = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var item = reader.ReadEvent();
            if (item is null) break;
            if (events.Count >= maximumCount) { hasMore = true; break; }
            if (item.RecordId is not { } recordId || item.TimeCreated is not { } timestamp ||
                item.ProviderName is not { } provider || item.Level is not { } level ||
                WindowsEventClassifier.Severity(level) is not { } severity) continue;
            lastRecordId = Math.Max(lastRecordId ?? 0, recordId);
            events.Add(new(0, timestamp, channel, provider, item.Id, item.Version ?? 0, level, recordId,
                severity, WindowsEventClassifier.Category(provider, item.Id)));
        }
        return new(channel, events, lastRecordId is null ? checkpoint : new(channel, lastRecordId.Value), hasMore);
    }

    private static string EscapeXPathLiteral(string value)
    {
        if (value.Contains('\'')) throw new SecurityException("Windows Event Log provider names cannot contain apostrophes.");
        return value;
    }
}
#pragma warning restore CA1416
