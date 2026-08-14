using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace PCMonitor.Service.History;

public sealed class HistoricalHistoryStore : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Lock _sync = new();
    private readonly List<HistoricalSnapshot> _snapshots = [];
    private readonly Channel<HistoricalSnapshot> _writes = Channel.CreateBounded<HistoricalSnapshot>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly ILogger<HistoricalHistoryStore> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly bool _enabled;
    private readonly int _resolutionSeconds;
    private readonly TimeSpan _retention;
    private readonly string _historyPath;
    private bool _persistenceAvailable;
    private DateTimeOffset _lastCompaction;

    public HistoricalHistoryStore(
        IOptions<HistoricalMonitoringOptions> options,
        TimeProvider timeProvider,
        ILogger<HistoricalHistoryStore> logger)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _enabled = options.Value.Enabled;
        _resolutionSeconds = options.Value.BucketDurationSeconds > 0 ? options.Value.BucketDurationSeconds : 60;
        _retention = TimeSpan.FromHours(double.IsFinite(options.Value.RetentionHours) && options.Value.RetentionHours > 0
            ? options.Value.RetentionHours
            : 24);
        _historyPath = string.IsNullOrWhiteSpace(options.Value.HistoryFilePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LanPcMonitor", "history", "sensor-history.jsonl")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.HistoryFilePath));

        if (options.Value.RetentionHours <= 0 || !double.IsFinite(options.Value.RetentionHours))
        {
            logger.LogWarning("Invalid historical retention; using 24 hours");
        }

        if (_enabled)
        {
            RestoreAndCompact();
        }
    }

    public void Add(HistoricalSnapshot snapshot)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_sync)
        {
            AddOrMergeLocked(snapshot);
            _snapshots.Sort((left, right) => left.StartTime.CompareTo(right.StartTime));
            RemoveExpiredLocked(_timeProvider.GetUtcNow());
        }

        if (_persistenceAvailable && !_writes.Writer.TryWrite(snapshot))
        {
            _logger.LogWarning("Historical persistence queue is unavailable; the bucket remains available in memory");
        }
    }

    // 'from' is exclusive for incremental synchronization; 'to' is inclusive.
    public HistoricalHistoryResponse Query(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? sensorId,
        Guid? sessionId = null)
    {
        HistoricalSnapshot[] result;
        lock (_sync)
        {
            result = _snapshots
                .Where(snapshot => from is null || snapshot.StartTime > from.Value)
                .Where(snapshot => to is null || snapshot.StartTime <= to.Value)
                .Where(snapshot => sessionId is null || snapshot.SessionId == sessionId)
                .Select(snapshot => FilterSensor(snapshot, sensorId))
                .Where(snapshot => sensorId is null || snapshot.Sensors.Count > 0)
                .ToArray();
        }

        return new HistoricalHistoryResponse(
            result.FirstOrDefault()?.StartTime,
            result.LastOrDefault()?.EndTime,
            _resolutionSeconds,
            result);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var snapshot in _writes.Reader.ReadAllAsync())
        {
            await AppendAsync(snapshot);
            if (_timeProvider.GetUtcNow() - _lastCompaction >= TimeSpan.FromHours(6))
            {
                await CompactAsync();
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _writes.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private void RestoreAndCompact()
    {
        try
        {
            var directory = Path.GetDirectoryName(_historyPath)!;
            Directory.CreateDirectory(directory);
            _persistenceAvailable = true;
            var cutoff = _timeProvider.GetUtcNow() - _retention;

            if (File.Exists(_historyPath))
            {
                foreach (var line in File.ReadLines(_historyPath))
                {
                    try
                    {
                        var snapshot = JsonSerializer.Deserialize<HistoricalSnapshot>(line, JsonOptions);
                        if (snapshot is not null && snapshot.EndTime >= cutoff)
                        {
                            AddOrMergeLocked(snapshot);
                        }
                    }
                    catch (JsonException exception)
                    {
                        _logger.LogWarning(exception, "Malformed historical record skipped during recovery");
                    }
                }
            }

            _snapshots.Sort((left, right) => left.StartTime.CompareTo(right.StartTime));
            RemoveExpiredLocked(_timeProvider.GetUtcNow());
            CompactSynchronously();
            _logger.LogInformation("Historical history restored: {RecordCount} records", _snapshots.Count);
        }
        catch (Exception exception)
        {
            _persistenceAvailable = false;
            _logger.LogError(exception, "Unable to initialize historical persistence at {HistoryPath}; using memory only", _historyPath);
        }
    }

    private async Task AppendAsync(HistoricalSnapshot snapshot)
    {
        try
        {
            var line = JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(_historyPath, line);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to persist historical snapshot");
        }
    }

    private async Task CompactAsync()
    {
        try
        {
            HistoricalSnapshot[] retained;
            lock (_sync)
            {
                RemoveExpiredLocked(_timeProvider.GetUtcNow());
                retained = _snapshots.ToArray();
            }

            var temporaryPath = _historyPath + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream))
            {
                foreach (var snapshot in retained)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, JsonOptions));
                }
            }

            File.Move(temporaryPath, _historyPath, true);
            _lastCompaction = _timeProvider.GetUtcNow();
            _logger.LogInformation("Historical history compacted: {RecordCount} records retained", retained.Length);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to compact historical history");
        }
    }

    private void CompactSynchronously()
    {
        var temporaryPath = _historyPath + ".tmp";
        using (var writer = new StreamWriter(temporaryPath, false))
        {
            foreach (var snapshot in _snapshots)
            {
                writer.WriteLine(JsonSerializer.Serialize(snapshot, JsonOptions));
            }
        }

        File.Move(temporaryPath, _historyPath, true);
        _lastCompaction = _timeProvider.GetUtcNow();
    }

    private void RemoveExpiredLocked(DateTimeOffset now)
    {
        var cutoff = now - _retention;
        _snapshots.RemoveAll(snapshot => snapshot.EndTime < cutoff);
    }

    private void AddOrMergeLocked(HistoricalSnapshot incoming)
    {
        var existingIndex = _snapshots.FindIndex(snapshot => snapshot.StartTime == incoming.StartTime);
        if (existingIndex < 0)
        {
            _snapshots.Add(incoming);
            return;
        }

        var existing = _snapshots[existingIndex];
        var sensors = existing.Sensors.ToDictionary(sensor => sensor.Id, StringComparer.Ordinal);
        foreach (var next in incoming.Sensors)
        {
            if (!sensors.TryGetValue(next.Id, out var current))
            {
                sensors[next.Id] = next;
                continue;
            }

            var count = current.SampleCount + next.SampleCount;
            sensors[next.Id] = next with
            {
                Min = Math.Min(current.Min, next.Min),
                Max = Math.Max(current.Max, next.Max),
                Average = count == 0
                    ? 0
                    : ((current.Average * current.SampleCount) + (next.Average * next.SampleCount)) / count,
                SampleCount = count
            };
        }

        _snapshots[existingIndex] = existing with
        {
            EndTime = existing.EndTime > incoming.EndTime ? existing.EndTime : incoming.EndTime,
            Sensors = sensors.Values.ToArray(),
            SessionId = incoming.SessionId ?? existing.SessionId,
            DominantProcess = MergeProcess(existing.DominantProcess, incoming.DominantProcess)
        };
    }

    private static HistoricalProcessSummary? MergeProcess(
        HistoricalProcessSummary? existing,
        HistoricalProcessSummary? incoming)
    {
        if (existing is null) return incoming;
        if (incoming is null) return existing;
        if (!existing.Name.Equals(incoming.Name, StringComparison.OrdinalIgnoreCase))
        {
            return incoming.SampleCount > existing.SampleCount ? incoming : existing;
        }

        var count = existing.SampleCount + incoming.SampleCount;
        return new HistoricalProcessSummary(
            incoming.Name,
            count == 0 ? 0 :
                ((existing.AverageCpuPercent * existing.SampleCount) +
                 (incoming.AverageCpuPercent * incoming.SampleCount)) / count,
            Math.Max(existing.MaxCpuPercent, incoming.MaxCpuPercent),
            count);
    }

    private static HistoricalSnapshot FilterSensor(HistoricalSnapshot snapshot, string? sensorId)
    {
        if (string.IsNullOrWhiteSpace(sensorId))
        {
            return snapshot;
        }

        return snapshot with
        {
            Sensors = snapshot.Sensors.Where(sensor =>
                sensor.Id.Equals(sensorId, StringComparison.OrdinalIgnoreCase)).ToArray()
        };
    }
}
