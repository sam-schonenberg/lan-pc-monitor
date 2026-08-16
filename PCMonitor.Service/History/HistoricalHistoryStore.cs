using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;

namespace PCMonitor.Service.History;

public sealed class HistoricalHistoryStore : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock _sync = new();
    private readonly List<HistoricalSnapshot> _snapshots = [];
    private readonly Dictionary<string, SensorCatalogEntry> _catalog = new(StringComparer.Ordinal);
    private readonly Channel<HistoricalSnapshot> _writes = Channel.CreateBounded<HistoricalSnapshot>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ILogger<HistoricalHistoryStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly bool _enabled;
    private readonly int _resolutionSeconds;
    private readonly int _defaultPageSize;
    private readonly int _maximumPageSize;
    private readonly TimeSpan _retention;
    private readonly string _historyPath;
    private readonly string _streamIdPath;
    private readonly Guid _streamId;
    private bool _persistenceAvailable;
    private DateTimeOffset _lastCompaction;
    private long _nextSequence = 1;
    private int _nextSensorId = 1;

    public HistoricalHistoryStore(IOptions<HistoricalMonitoringOptions> options, TimeProvider timeProvider,
        ILogger<HistoricalHistoryStore> logger)
    {
        _logger = logger; _timeProvider = timeProvider; _enabled = options.Value.Enabled;
        _resolutionSeconds = options.Value.BucketDurationSeconds > 0 ? options.Value.BucketDurationSeconds : 60;
        _defaultPageSize = options.Value.DefaultPageSize > 0 ? options.Value.DefaultPageSize : 500;
        _maximumPageSize = options.Value.MaximumPageSize > 0 ? options.Value.MaximumPageSize : 2000;
        if (_defaultPageSize > _maximumPageSize) _defaultPageSize = _maximumPageSize;
        _retention = TimeSpan.FromHours(double.IsFinite(options.Value.RetentionHours) && options.Value.RetentionHours > 0
            ? options.Value.RetentionHours : 24);
        _historyPath = string.IsNullOrWhiteSpace(options.Value.HistoryFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LanPcMonitor", "history", "sensor-history.jsonl")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.HistoryFilePath));
        _streamIdPath = _historyPath + ".stream-id";
        _streamId = InitializeStreamId(File.Exists(_historyPath));
        if (_enabled) RestoreAndCompact();
    }

    public void RegisterSensors(IEnumerable<HistoricalSensorReading> sensors)
    {
        lock (_sync) RegisterSensorsLocked(sensors);
    }

    public void RegisterSensors(IEnumerable<SensorReading> sensors)
    {
        lock (_sync) RegisterSensorMetadataLocked(sensors);
    }

    public SensorCatalogResponse GetCatalog()
    {
        lock (_sync)
        {
            var sensors = _catalog.Values.OrderBy(x => x.Id).ToArray();
            return new SensorCatalogResponse(CatalogVersion(sensors), sensors);
        }
    }

    public HistoryManifestResponse GetManifest()
    {
        lock (_sync)
        {
            var ordered = _snapshots.OrderBy(x => x.Sequence).ToArray();
            var catalogVersion = CatalogVersion(_catalog.Values.OrderBy(x => x.Id));
            return new HistoryManifestResponse(
                _streamId,
                catalogVersion,
                ordered.FirstOrDefault()?.Sequence,
                ordered.LastOrDefault()?.Sequence,
                ordered.Length,
                ordered.OrderBy(x => x.StartTime).FirstOrDefault()?.StartTime,
                ordered.OrderByDescending(x => x.EndTime).FirstOrDefault()?.EndTime,
                _resolutionSeconds,
                _retention.TotalHours,
                BuildSequenceRanges(ordered),
                _timeProvider.GetUtcNow());
        }
    }

    public void Add(HistoricalSnapshot snapshot)
    {
        if (!_enabled) return;
        HistoricalSnapshot persisted;
        lock (_sync)
        {
            RegisterSensorsLocked(snapshot.Sensors);
            persisted = snapshot.Sequence > 0 ? snapshot : snapshot with { Sequence = _nextSequence++ };
            _nextSequence = Math.Max(_nextSequence, persisted.Sequence + 1);
            persisted = AddOrMergeLocked(persisted);
            _snapshots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            RemoveExpiredLocked(_timeProvider.GetUtcNow());
        }
        if (_persistenceAvailable && !_writes.Writer.TryWrite(persisted))
            _logger.LogWarning("Historical persistence queue is unavailable; the bucket remains available in memory");
    }

    // Compatibility query for existing in-process callers and tests.
    public HistoricalHistoryResponse Query(DateTimeOffset? from, DateTimeOffset? to, string? sensorId,
        Guid? sessionId = null)
    {
        HistoricalSnapshot[] result;
        lock (_sync) result = SelectLocked(from, to, null, sessionId)
            .Select(x => FilterSensors(x, string.IsNullOrWhiteSpace(sensorId) ? null : [sensorId]))
            .Where(x => sensorId is null || x.Sensors.Count > 0).ToArray();
        return new(result.FirstOrDefault()?.StartTime, result.LastOrDefault()?.EndTime, _resolutionSeconds, result);
    }

    public CompactHistoryResponse QueryCompact(DateTimeOffset? from, DateTimeOffset? to, long? afterSequence,
        int? limit, HistoryResolution resolution, IReadOnlyCollection<int>? sensorIds, Guid? sessionId = null,
        long? beforeSequence = null)
    {
        HistoricalSnapshot[] source;
        Dictionary<string, int> ids;
        string catalogVersion;
        lock (_sync)
        {
            source = SelectLocked(from, to, afterSequence, sessionId, beforeSequence).ToArray();
            ids = _catalog.ToDictionary(x => x.Key, x => x.Value.Id, StringComparer.Ordinal);
            catalogVersion = CatalogVersion(_catalog.Values.OrderBy(x => x.Id));
        }

        // Aggregation and serialization projection deliberately occur after releasing the store lock.
        var resolved = resolution == HistoryResolution.Minute ? source : Aggregate(source, resolution);
        if (beforeSequence is not null) resolved = resolved.OrderByDescending(x => x.Sequence).ToArray();
        if (sensorIds is { Count: > 0 })
        {
            var wanted = sensorIds.ToHashSet();
            resolved = resolved.Select(x => x with
            {
                Sensors = x.Sensors.Where(s => ids.TryGetValue(s.Id, out var id) && wanted.Contains(id)).ToArray()
            }).Where(x => x.Sensors.Count > 0).ToArray();
        }
        var pageSize = Math.Clamp(limit ?? _defaultPageSize, 1, _maximumPageSize);
        var page = resolved.Take(pageSize).ToArray();
        var hasMore = resolved.Length > page.Length;
        var compact = page.Select(x => new CompactHistoricalSnapshot(x.Sequence, x.StartTime, x.EndTime,
            x.Sensors.Select(s => new CompactHistoricalSensorReading(ids[s.Id], Round(s.Min), Round(s.Max),
                Round(s.Average), s.SampleCount)).ToArray(), x.SessionId, x.DominantProcess)).ToArray();
        return new(catalogVersion, resolution.ToString().ToLowerInvariant(), page.FirstOrDefault()?.Sequence,
            page.LastOrDefault()?.Sequence, hasMore, hasMore ? page.Last().Sequence : null, compact,
            beforeSequence is null ? resolved.LastOrDefault()?.Sequence : resolved.FirstOrDefault()?.Sequence,
            Math.Max(0, resolved.Length - page.Length),
            beforeSequence is not null && hasMore ? page.Last().Sequence : null);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var snapshot in _writes.Reader.ReadAllAsync(stoppingToken))
        {
            await AppendAsync(snapshot);
            if (_timeProvider.GetUtcNow() - _lastCompaction >= TimeSpan.FromHours(6)) await CompactAsync();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    { _writes.Writer.TryComplete(); await base.StopAsync(cancellationToken); }

    private IEnumerable<HistoricalSnapshot> SelectLocked(DateTimeOffset? from, DateTimeOffset? to,
        long? afterSequence, Guid? sessionId, long? beforeSequence = null) => _snapshots
        .Where(x => from is null || x.StartTime > from.Value)
        .Where(x => to is null || x.StartTime <= to.Value)
        .Where(x => afterSequence is null || x.Sequence > afterSequence)
        .Where(x => beforeSequence is null || x.Sequence < beforeSequence)
        .Where(x => sessionId is null || x.SessionId == sessionId)
        .OrderBy(x => beforeSequence is null ? x.Sequence : -x.Sequence);

    private void RestoreAndCompact()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!); _persistenceAvailable = true;
            var cutoff = _timeProvider.GetUtcNow() - _retention;
            var restored = new List<HistoricalSnapshot>();
            if (File.Exists(_historyPath)) foreach (var line in File.ReadLines(_historyPath))
            {
                try { var item = JsonSerializer.Deserialize<HistoricalSnapshot>(line, JsonOptions);
                    if (item is not null && item.EndTime >= cutoff) restored.Add(item); }
                catch (JsonException ex) { _logger.LogWarning(ex, "Malformed historical record skipped during recovery"); }
            }
            // Legacy records have sequence zero. Assign in chronological order after all persisted sequences.
            var next = restored.Where(x => x.Sequence > 0).Select(x => x.Sequence).DefaultIfEmpty().Max() + 1;
            foreach (var item in restored.OrderBy(x => x.StartTime))
            {
                var sequenced = item.Sequence > 0 ? item : item with { Sequence = next++ };
                RegisterSensorsLocked(sequenced.Sensors); AddOrMergeLocked(sequenced);
            }
            _snapshots.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            _nextSequence = _snapshots.Select(x => x.Sequence).DefaultIfEmpty().Max() + 1;
            RemoveExpiredLocked(_timeProvider.GetUtcNow()); CompactSynchronously();
            _logger.LogInformation("Historical history restored: {RecordCount} records", _snapshots.Count);
        }
        catch (Exception ex) { _persistenceAvailable = false; _logger.LogError(ex,
            "Unable to initialize historical persistence at {HistoryPath}; using memory only", _historyPath); }
    }

    private Guid InitializeStreamId(bool historyFileExists)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
            if (historyFileExists && File.Exists(_streamIdPath) &&
                Guid.TryParse(File.ReadAllText(_streamIdPath).Trim(), out var restored)) return restored;
            var created = Guid.NewGuid();
            File.WriteAllText(_streamIdPath, created.ToString("D"));
            return created;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to persist history stream identity; using a runtime identity");
            return Guid.NewGuid();
        }
    }

    private static IReadOnlyList<HistorySequenceRange> BuildSequenceRanges(IReadOnlyList<HistoricalSnapshot> ordered)
    {
        if (ordered.Count == 0) return [];
        var ranges = new List<HistorySequenceRange>();
        var from = ordered[0].Sequence; var previous = from; var count = 1;
        for (var index = 1; index < ordered.Count; index++)
        {
            var sequence = ordered[index].Sequence;
            if (sequence == previous + 1) { previous = sequence; count++; continue; }
            ranges.Add(new(from, previous, count));
            from = previous = sequence; count = 1;
        }
        ranges.Add(new(from, previous, count));
        return ranges;
    }

    private void RegisterSensorsLocked(IEnumerable<HistoricalSensorReading> sensors)
    {
        foreach (var s in sensors)
        {
            if (_catalog.TryGetValue(s.Id, out var existing))
                _catalog[s.Id] = existing with { Hardware = s.Hardware, Name = s.Name, Type = s.Type, Unit = s.Unit };
            else _catalog[s.Id] = new(_nextSensorId++, s.Id, s.Hardware, s.Name, s.Type, s.Unit);
        }
    }

    private void RegisterSensorMetadataLocked(IEnumerable<SensorReading> sensors)
    {
        foreach (var s in sensors)
        {
            if (_catalog.TryGetValue(s.Id, out var existing))
                _catalog[s.Id] = existing with { Hardware = s.Hardware, Name = s.Name, Type = s.Type, Unit = s.Unit };
            else _catalog[s.Id] = new(_nextSensorId++, s.Id, s.Hardware, s.Name, s.Type, s.Unit);
        }
    }

    private HistoricalSnapshot AddOrMergeLocked(HistoricalSnapshot incoming)
    {
        var index = _snapshots.FindIndex(x => x.StartTime == incoming.StartTime);
        if (index < 0) { _snapshots.Add(incoming); return incoming; }
        var existing = _snapshots[index];
        var sensors = existing.Sensors.ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var next in incoming.Sensors)
        {
            if (!sensors.TryGetValue(next.Id, out var current)) { sensors[next.Id] = next; continue; }
            var count = current.SampleCount + next.SampleCount;
            sensors[next.Id] = next with { Min = Math.Min(current.Min, next.Min), Max = Math.Max(current.Max, next.Max),
                Average = count == 0 ? 0 : (current.Average * current.SampleCount + next.Average * next.SampleCount) / count,
                SampleCount = count };
        }
        var merged = existing with { EndTime = Max(existing.EndTime, incoming.EndTime), Sensors = sensors.Values.ToArray(),
            SessionId = incoming.SessionId ?? existing.SessionId,
            DominantProcess = MergeProcess(existing.DominantProcess, incoming.DominantProcess) };
        _snapshots[index] = merged; return merged;
    }

    private static HistoricalSnapshot[] Aggregate(HistoricalSnapshot[] source, HistoryResolution resolution) => source
        .GroupBy(x => Align(x.StartTime, resolution))
        .OrderBy(g => g.Key)
        .Select(g => new HistoricalSnapshot(g.Key, End(g.Key, resolution),
            g.SelectMany(x => x.Sensors).GroupBy(x => x.Id, StringComparer.Ordinal).Select(AggregateSensor).ToArray(),
            g.Select(x => x.SessionId).FirstOrDefault(x => x is not null),
            g.Select(x => x.DominantProcess).Where(x => x is not null).OrderByDescending(x => x!.SampleCount).FirstOrDefault(),
            g.Max(x => x.Sequence))).ToArray();

    private static HistoricalSensorReading AggregateSensor(IGrouping<string, HistoricalSensorReading> group)
    {
        var count = group.Sum(x => x.SampleCount); var last = group.Last();
        return last with { Min = group.Min(x => x.Min), Max = group.Max(x => x.Max),
            Average = count == 0 ? 0 : group.Sum(x => x.Average * x.SampleCount) / count, SampleCount = count };
    }

    private static DateTimeOffset Align(DateTimeOffset value, HistoryResolution resolution)
    {
        var utc = value.UtcDateTime;
        var aligned = resolution == HistoryResolution.Hour
            ? new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc)
            : new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc);
        return new DateTimeOffset(aligned);
    }
    private static DateTimeOffset End(DateTimeOffset start, HistoryResolution resolution) =>
        resolution == HistoryResolution.Hour ? start.AddHours(1) : start.AddDays(1);
    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
    private static HistoricalSnapshot FilterSensors(HistoricalSnapshot x, IReadOnlyCollection<string>? ids) =>
        ids is null ? x : x with { Sensors = x.Sensors.Where(s => ids.Contains(s.Id, StringComparer.OrdinalIgnoreCase)).ToArray() };
    private static string CatalogVersion(IEnumerable<SensorCatalogEntry> sensors)
    {
        var canonical = string.Join('\n', sensors.Select(x => $"{x.Id}|{x.Key}|{x.Hardware}|{x.Name}|{x.Type}|{x.Unit}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()[..16];
    }
    private static HistoricalProcessSummary? MergeProcess(HistoricalProcessSummary? a, HistoricalProcessSummary? b)
    {
        if (a is null) return b; if (b is null) return a;
        if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase)) return b.SampleCount > a.SampleCount ? b : a;
        var count = a.SampleCount + b.SampleCount;
        return new(b.Name, count == 0 ? 0 : (a.AverageCpuPercent * a.SampleCount + b.AverageCpuPercent * b.SampleCount) / count,
            Math.Max(a.MaxCpuPercent, b.MaxCpuPercent), count);
    }
    private void RemoveExpiredLocked(DateTimeOffset now) => _snapshots.RemoveAll(x => x.EndTime < now - _retention);
    private async Task AppendAsync(HistoricalSnapshot snapshot)
    { try { await File.AppendAllTextAsync(_historyPath, JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine); }
      catch (Exception ex) { _logger.LogError(ex, "Unable to persist historical snapshot"); } }
    private async Task CompactAsync()
    {
        try
        {
            HistoricalSnapshot[] retained; lock (_sync) { RemoveExpiredLocked(_timeProvider.GetUtcNow()); retained = _snapshots.ToArray(); }
            var temp = _historyPath + ".tmp";
            await using (var writer = new StreamWriter(temp, false)) foreach (var item in retained)
                await writer.WriteLineAsync(JsonSerializer.Serialize(item, JsonOptions));
            File.Move(temp, _historyPath, true); _lastCompaction = _timeProvider.GetUtcNow();
        }
        catch (Exception ex) { _logger.LogError(ex, "Unable to compact historical history"); }
    }
    private void CompactSynchronously()
    {
        var temp = _historyPath + ".tmp"; using (var writer = new StreamWriter(temp, false))
            foreach (var item in _snapshots) writer.WriteLine(JsonSerializer.Serialize(item, JsonOptions));
        File.Move(temp, _historyPath, true); _lastCompaction = _timeProvider.GetUtcNow();
    }
}
