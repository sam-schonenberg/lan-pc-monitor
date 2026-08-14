using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;
using PCMonitor.Service.SessionDetection;

namespace PCMonitor.Service.History;

public sealed class HistoricalSensorAggregator
{
    private readonly Lock _sync = new();
    private readonly HistoricalHistoryStore _history;
    private readonly ILogger<HistoricalSensorAggregator> _logger;
    private readonly SessionRuntimeContext _sessionContext;
    private readonly bool _enabled;
    private readonly int _bucketDurationSeconds;
    private DateTimeOffset? _bucketStart;
    private DateTimeOffset? _lastSampleTime;
    private readonly Dictionary<string, MutableHistoricalSensor> _sensors = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, long> _activeSessionSamples = [];
    private readonly Dictionary<string, MutableHistoricalProcess> _processes = new(StringComparer.OrdinalIgnoreCase);
    private long _lastProcessSequence;

    public HistoricalSensorAggregator(
        HistoricalHistoryStore history,
        SessionRuntimeContext sessionContext,
        IOptions<HistoricalMonitoringOptions> options,
        ILogger<HistoricalSensorAggregator> logger)
    {
        _history = history;
        _sessionContext = sessionContext;
        _logger = logger;
        _enabled = options.Value.Enabled;
        _bucketDurationSeconds = options.Value.BucketDurationSeconds > 0
            ? options.Value.BucketDurationSeconds
            : 60;

        if (options.Value.BucketDurationSeconds <= 0)
        {
            logger.LogWarning("Invalid historical bucket duration; using 60 seconds");
        }

        logger.LogInformation(
            "Historical monitoring initialized with {BucketDuration}-second buckets (enabled: {Enabled})",
            _bucketDurationSeconds, _enabled);
    }

    public void Process(SensorSnapshot snapshot)
    {
        if (!_enabled)
        {
            return;
        }

        HistoricalSnapshot? finalized = null;
        try
        {
            lock (_sync)
            {
                var incomingStart = Align(snapshot.Timestamp, _bucketDurationSeconds);
                if (_bucketStart is not null && incomingStart < _bucketStart)
                {
                    _logger.LogDebug("Ignoring out-of-order historical snapshot at {Timestamp}", snapshot.Timestamp);
                    return;
                }

                if (_bucketStart is not null && incomingStart > _bucketStart)
                {
                    finalized = FinalizeLocked(_bucketStart.Value.AddSeconds(_bucketDurationSeconds));
                    ResetLocked(incomingStart);
                }
                else if (_bucketStart is null)
                {
                    ResetLocked(incomingStart);
                }

                AddReadingsLocked(snapshot);
                AddSessionMetadataLocked(_sessionContext.GetSnapshot());
                _lastSampleTime = snapshot.Timestamp;
            }

            if (finalized is not null)
            {
                _history.Add(finalized);
                _logger.LogDebug("Historical bucket finalized for {StartTime}", finalized.StartTime);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to aggregate historical sensor snapshot");
        }
    }

    // Normal shutdown preserves the partial bucket and records its actual last sample time.
    public void Complete()
    {
        if (!_enabled)
        {
            return;
        }

        HistoricalSnapshot? finalized;
        lock (_sync)
        {
            finalized = _bucketStart is not null && _lastSampleTime is not null && _sensors.Count > 0
                ? FinalizeLocked(_lastSampleTime.Value)
                : null;
            _bucketStart = null;
            _lastSampleTime = null;
            _sensors.Clear();
        }

        if (finalized is not null)
        {
            _history.Add(finalized);
        }
    }

    private void AddReadingsLocked(SensorSnapshot snapshot)
    {
        foreach (var sensor in snapshot.Sensors)
        {
            if (sensor.Value is not { } value || !float.IsFinite(value))
            {
                continue;
            }

            if (_sensors.TryGetValue(sensor.Id, out var aggregate))
            {
                aggregate.Add(sensor.Hardware, sensor.Name, sensor.Type, sensor.Unit, value);
            }
            else
            {
                _sensors[sensor.Id] = new MutableHistoricalSensor(
                    sensor.Id, sensor.Hardware, sensor.Name, sensor.Type, sensor.Unit, value);
            }
        }
    }

    private HistoricalSnapshot? FinalizeLocked(DateTimeOffset endTime)
    {
        if (_bucketStart is null || _sensors.Count == 0)
        {
            return null;
        }

        return new HistoricalSnapshot(
            _bucketStart.Value,
            endTime,
            _sensors.Values.Select(sensor => sensor.ToSnapshot()).ToArray(),
            _activeSessionSamples.OrderByDescending(item => item.Value).Select(item => (Guid?)item.Key).FirstOrDefault(),
            _processes.Values.OrderByDescending(process => process.Count)
                .ThenByDescending(process => process.Max).FirstOrDefault()?.ToSnapshot());
    }

    private void ResetLocked(DateTimeOffset start)
    {
        _bucketStart = start;
        _lastSampleTime = null;
        _sensors.Clear();
        _activeSessionSamples.Clear();
        _processes.Clear();
        _lastProcessSequence = 0;
    }

    private void AddSessionMetadataLocked(SessionRuntimeSnapshot runtime)
    {
        if (runtime.State != LoadSessionState.Active || runtime.SessionId is not { } sessionId)
        {
            return;
        }

        _activeSessionSamples[sessionId] = _activeSessionSamples.GetValueOrDefault(sessionId) + 1;
        if (runtime.LatestProcessSample is not { } sample || sample.SessionId != sessionId ||
            sample.Sequence == _lastProcessSequence)
        {
            return;
        }

        _lastProcessSequence = sample.Sequence;
        if (_processes.TryGetValue(sample.Dominant.Name, out var process))
        {
            process.Add(sample.Dominant.CpuPercent);
        }
        else
        {
            _processes[sample.Dominant.Name] = new MutableHistoricalProcess(
                sample.Dominant.Name, sample.Dominant.CpuPercent);
        }
    }

    private static DateTimeOffset Align(DateTimeOffset timestamp, int durationSeconds)
    {
        var utcSeconds = timestamp.ToUniversalTime().ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(utcSeconds - Mod(utcSeconds, durationSeconds));
    }

    private static long Mod(long value, long divisor) => ((value % divisor) + divisor) % divisor;
}
