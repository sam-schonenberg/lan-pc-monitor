using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PCMonitor.Service.Diagnostics;

public sealed class WindowsDiagnosticStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock _sync = new();
    private readonly List<WindowsDiagnosticEvent> _events = [];
    private readonly Dictionary<string, WindowsEventCheckpoint> _checkpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly long _maximumBytes;
    private readonly int _defaultPageSize;
    private readonly int _maximumPageSize;
    private readonly string _eventsPath;
    private readonly string _statePath;
    private long _nextSequence = 1;

    public WindowsDiagnosticStore(IOptions<WindowsDiagnosticsOptions> options, TimeProvider timeProvider,
        ILogger<WindowsDiagnosticStore> logger)
    {
        _timeProvider = timeProvider;
        var value = options.Value;
        _retention = TimeSpan.FromDays(Math.Max(1, value.RetentionDays));
        _maximumBytes = Math.Max(1, value.MaximumStorageMegabytes) * 1024L * 1024L;
        _defaultPageSize = Math.Max(1, value.DefaultPageSize);
        _maximumPageSize = Math.Max(_defaultPageSize, value.MaximumPageSize);
        _eventsPath = string.IsNullOrWhiteSpace(value.StoreFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LanPcMonitor", "diagnostics", "windows-events.jsonl")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.StoreFilePath));
        _statePath = _eventsPath + ".state.json";
        try { Restore(); }
        catch (Exception exception) { logger.LogError(exception, "Unable to restore Windows diagnostic history"); }
    }

    public WindowsEventCheckpoint? GetCheckpoint(string channel)
    { lock (_sync) return _checkpoints.GetValueOrDefault(channel); }

    public void AddBatch(WindowsEventBatch batch)
    {
        lock (_sync)
        {
            var known = _events.Select(x => (x.Channel, x.RecordId)).ToHashSet();
            foreach (var item in batch.Events.OrderBy(x => x.Timestamp))
                if (known.Add((item.Channel, item.RecordId)))
                    _events.Add(WindowsEventClassifier.Enrich(item) with { Sequence = _nextSequence++ });
            if (batch.Checkpoint is not null) _checkpoints[batch.Channel] = batch.Checkpoint;
            PruneLocked();
            PersistLocked();
        }
    }

    public WindowsDiagnosticEventsResponse Query(long? beforeSequence, int? limit,
        WindowsDiagnosticSeverity? minimumSeverity, string? channel, string? provider, int? eventId)
    {
        lock (_sync)
        {
            var source = _events.Where(x => beforeSequence is null || x.Sequence < beforeSequence)
                .Where(x => minimumSeverity is null || x.Severity >= minimumSeverity)
                .Where(x => channel is null || x.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase))
                .Where(x => provider is null || x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))
                .Where(x => eventId is null || x.EventId == eventId)
                .OrderByDescending(x => x.Sequence).ToArray();
            var page = source.Take(Math.Clamp(limit ?? _defaultPageSize, 1, _maximumPageSize)).ToArray();
            return new(page.LastOrDefault()?.Sequence, page.FirstOrDefault()?.Sequence, source.Length > page.Length,
                source.Length > page.Length ? page.Last().Sequence : null, page);
        }
    }

    public (int Count, long? Oldest, long? Newest) GetInventory()
    { lock (_sync) return (_events.Count, _events.FirstOrDefault()?.Sequence, _events.LastOrDefault()?.Sequence); }

    private void Restore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_eventsPath)!);
        if (File.Exists(_eventsPath)) foreach (var line in File.ReadLines(_eventsPath))
        {
            try
            {
                if (JsonSerializer.Deserialize<WindowsDiagnosticEvent>(line, JsonOptions) is { } item)
                    _events.Add(WindowsEventClassifier.Enrich(item));
            }
            catch (JsonException) { }
        }
        if (File.Exists(_statePath) && JsonSerializer.Deserialize<WindowsEventCheckpoint[]>(
                File.ReadAllText(_statePath), JsonOptions) is { } state)
            foreach (var checkpoint in state) _checkpoints[checkpoint.Channel] = checkpoint;
        _events.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        _nextSequence = _events.Select(x => x.Sequence).DefaultIfEmpty().Max() + 1;
        PruneLocked();
    }

    private void PruneLocked()
    {
        var cutoff = _timeProvider.GetUtcNow() - _retention;
        _events.RemoveAll(x => x.Timestamp < cutoff);
        while (_events.Count > 1 && EstimateBytesLocked() > _maximumBytes) _events.RemoveAt(0);
    }

    private long EstimateBytesLocked() => _events.Sum(x => JsonSerializer.SerializeToUtf8Bytes(x, JsonOptions).Length + 2L);

    private void PersistLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_eventsPath)!);
        var temp = _eventsPath + ".tmp";
        using (var writer = new StreamWriter(temp, false)) foreach (var item in _events)
            writer.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
        File.Move(temp, _eventsPath, true);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(_checkpoints.Values, JsonOptions));
    }
}
