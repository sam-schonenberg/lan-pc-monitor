using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;

namespace PCMonitor.Service.SessionDetection;

public sealed class LoadSessionDetector
{
    private readonly Lock _sync = new();
    private readonly LoadSensorSelector _selector;
    private readonly ILogger<LoadSessionDetector> _logger;
    private readonly SessionRuntimeContext _sessionContext;
    private readonly EffectiveOptions _options;
    private readonly Queue<LoadSample> _loadSamples = new();
    private readonly Queue<SensorSnapshot> _candidateSnapshots = new();
    private readonly Dictionary<string, MutableSensorStatistics> _statistics = new(StringComparer.Ordinal);
    private LoadSessionState _state;
    private DateTimeOffset? _candidateSince;
    private DateTimeOffset? _lowSince;
    private Guid _sessionId;
    private DateTimeOffset _startedAt;
    private float? _currentCpu;
    private float? _currentGpu;
    private CompletedLoadSession? _lastSession;
    private bool _missingLoadsLogged;

    public LoadSessionDetector(
        LoadSensorSelector selector,
        SessionRuntimeContext sessionContext,
        IOptions<SessionDetectionOptions> options,
        ILogger<LoadSessionDetector> logger)
    {
        _selector = selector;
        _sessionContext = sessionContext;
        _logger = logger;
        _options = EffectiveOptions.Create(options.Value, logger);
    }

    public void Process(SensorSnapshot snapshot)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                ProcessLocked(snapshot);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Load session detection failed for a sensor snapshot");
        }
    }

    public LoadSessionStatus GetCurrent()
    {
        lock (_sync)
        {
            return new LoadSessionStatus(_state, _state is LoadSessionState.Candidate or LoadSessionState.Active
                ? CreateSessionSnapshot(DateTimeOffset.UtcNow, null)
                : null);
        }
    }

    public CompletedLoadSessionStatus GetLast()
    {
        lock (_sync)
        {
            return new CompletedLoadSessionStatus(_state, _lastSession);
        }
    }

    private void ProcessLocked(SensorSnapshot snapshot)
    {
        var loads = _selector.Select(snapshot);
        _currentCpu = loads.Cpu;
        _currentGpu = loads.Gpu;

        if (loads.Cpu is null && loads.Gpu is null)
        {
            if (!_missingLoadsLogged)
            {
                _logger.LogWarning("Neither an overall CPU nor GPU load sensor is available; session detection is waiting for one");
                _missingLoadsLogged = true;
            }
            return;
        }

        _missingLoadsLogged = false;
        _loadSamples.Enqueue(new LoadSample(snapshot.Timestamp, loads.Cpu, loads.Gpu));
        TrimLoadSamples(snapshot.Timestamp);

        if (_state == LoadSessionState.Active)
        {
            AddStatistics(snapshot);
            EvaluateEnd(snapshot.Timestamp);
            return;
        }

        var startAverage = AverageSince(snapshot.Timestamp - _options.StartWindow);
        var high = IsHigh(startAverage);
        if (!high)
        {
            ResetCandidate();
            return;
        }

        if (_state == LoadSessionState.Idle)
        {
            _state = LoadSessionState.Candidate;
            _candidateSince = snapshot.Timestamp;
            _sessionId = Guid.NewGuid();
            _candidateSnapshots.Clear();
            _sessionContext.CreateCandidate(_sessionId, snapshot.Timestamp);
            _logger.LogInformation(
                "Candidate session created: {SessionId} (CPU average: {CpuAverage}, GPU average: {GpuAverage})",
                _sessionId, startAverage.Cpu, startAverage.Gpu);
        }

        _candidateSnapshots.Enqueue(snapshot);
        TrimCandidateSnapshots(snapshot.Timestamp);

        if (snapshot.Timestamp - _candidateSince >= _options.StartDuration)
        {
            StartSession(snapshot.Timestamp, startAverage);
        }
    }

    private void StartSession(DateTimeOffset now, LoadValues averages)
    {
        _state = LoadSessionState.Active;
        _startedAt = _candidateSince ?? now;
        _sessionContext.Promote(_sessionId);
        _lowSince = null;
        _statistics.Clear();

        foreach (var candidateSnapshot in _candidateSnapshots.Where(item => item.Timestamp >= _startedAt))
        {
            AddStatistics(candidateSnapshot);
        }

        _candidateSnapshots.Clear();
        _logger.LogInformation(
            "Session promoted to active: {SessionId} at {StartedAt} (CPU average: {CpuAverage}, GPU average: {GpuAverage})",
            _sessionId, _startedAt, averages.Cpu, averages.Gpu);
    }

    private void EvaluateEnd(DateTimeOffset now)
    {
        var endAverage = AverageSince(now - _options.EndWindow);
        if (!IsLow(endAverage))
        {
            _lowSince = null;
            return;
        }

        _lowSince ??= now;
        if (now - _lowSince < _options.EndDuration)
        {
            return;
        }

        var completed = CreateSessionSnapshot(now, now);
        _lastSession = new CompletedLoadSession(
            completed.Id,
            completed.StartedAt,
            completed.EndedAt!.Value,
            completed.DurationSeconds,
            completed.PrimaryProcess);
        _logger.LogInformation(
            "Session ended: {SessionId}, primary process {ProcessName}, after {DurationSeconds:F0} seconds (CPU average: {CpuAverage}, GPU average: {GpuAverage})",
            _sessionId, completed.PrimaryProcess?.Name, completed.DurationSeconds, endAverage.Cpu, endAverage.Gpu);

        _sessionContext.Clear(_sessionId);
        _state = LoadSessionState.Idle;
        _lowSince = null;
        _candidateSince = null;
        _statistics.Clear();
    }

    private void AddStatistics(SensorSnapshot snapshot)
    {
        foreach (var sensor in snapshot.Sensors)
        {
            if (sensor.Value is not { } value || !float.IsFinite(value))
            {
                continue;
            }

            if (_statistics.TryGetValue(sensor.Id, out var statistics))
            {
                statistics.Add(sensor.Hardware, sensor.Name, sensor.Type, sensor.Unit, value);
            }
            else
            {
                _statistics[sensor.Id] = new MutableSensorStatistics(
                    sensor.Id, sensor.Hardware, sensor.Name, sensor.Type, sensor.Unit, value);
            }
        }
    }

    private LoadSession CreateSessionSnapshot(DateTimeOffset now, DateTimeOffset? endedAt)
    {
        var runtime = _sessionContext.GetSnapshot();
        var startedAt = _candidateSince ?? _startedAt;
        return new LoadSession(
            _sessionId,
            startedAt,
            endedAt,
            Math.Max(0, ((endedAt ?? now) - startedAt).TotalSeconds),
            _currentCpu,
            _currentGpu,
            runtime.LatestProcessSample?.Dominant,
            runtime.PrimaryProcess,
            _state == LoadSessionState.Active
                ? _statistics.Values.Select(value => value.ToSnapshot()).ToArray()
                : Array.Empty<SensorStatistics>());
    }

    private LoadValues AverageSince(DateTimeOffset cutoff)
    {
        var samples = _loadSamples.Where(sample => sample.Timestamp >= cutoff).ToArray();
        return new LoadValues(Average(samples.Select(sample => sample.Cpu)), Average(samples.Select(sample => sample.Gpu)));
    }

    private bool IsHigh(LoadValues values) =>
        values.Cpu >= _options.StartCpuLoadPercent || values.Gpu >= _options.StartGpuLoadPercent;

    private bool IsLow(LoadValues values)
    {
        var hasValue = values.Cpu is not null || values.Gpu is not null;
        return hasValue &&
               (values.Cpu is null || values.Cpu < _options.EndCpuLoadPercent) &&
               (values.Gpu is null || values.Gpu < _options.EndGpuLoadPercent);
    }

    private static float? Average(IEnumerable<float?> values)
    {
        var available = values.Where(value => value is not null).Select(value => (double)value!.Value).ToArray();
        return available.Length == 0 ? null : (float)available.Average();
    }

    private void TrimLoadSamples(DateTimeOffset now)
    {
        var retention = _options.StartWindow > _options.EndWindow ? _options.StartWindow : _options.EndWindow;
        while (_loadSamples.TryPeek(out var sample) && sample.Timestamp < now - retention)
        {
            _loadSamples.Dequeue();
        }
    }

    private void TrimCandidateSnapshots(DateTimeOffset now)
    {
        var retention = _options.StartDuration + _options.StartWindow;
        while (_candidateSnapshots.TryPeek(out var snapshot) && snapshot.Timestamp < now - retention)
        {
            _candidateSnapshots.Dequeue();
        }
    }

    private void ResetCandidate()
    {
        if (_state == LoadSessionState.Candidate)
        {
            _logger.LogInformation("Candidate session cancelled: {SessionId}", _sessionId);
            _sessionContext.Clear(_sessionId);
        }

        _state = LoadSessionState.Idle;
        _candidateSince = null;
        _candidateSnapshots.Clear();
    }

    private readonly record struct LoadSample(DateTimeOffset Timestamp, float? Cpu, float? Gpu);

    private sealed record EffectiveOptions(
        bool Enabled,
        double StartCpuLoadPercent,
        double StartGpuLoadPercent,
        TimeSpan StartWindow,
        TimeSpan StartDuration,
        double EndCpuLoadPercent,
        double EndGpuLoadPercent,
        TimeSpan EndWindow,
        TimeSpan EndDuration)
    {
        public static EffectiveOptions Create(SessionDetectionOptions source, ILogger logger)
        {
            static double Percent(double value, double fallback) => value is >= 0 and <= 100 ? value : fallback;
            static TimeSpan Duration(double seconds, double fallback) =>
                TimeSpan.FromSeconds(double.IsFinite(seconds) && seconds > 0 ? seconds : fallback);

            var result = new EffectiveOptions(
                source.Enabled,
                Percent(source.StartCpuLoadPercent, 40),
                Percent(source.StartGpuLoadPercent, 40),
                Duration(source.StartWindowSeconds, 10),
                Duration(source.StartDurationSeconds, 30),
                Percent(source.EndCpuLoadPercent, 20),
                Percent(source.EndGpuLoadPercent, 20),
                Duration(source.EndWindowSeconds, 30),
                Duration(source.EndDurationSeconds, 90));

            if (result.StartCpuLoadPercent != source.StartCpuLoadPercent ||
                result.StartGpuLoadPercent != source.StartGpuLoadPercent ||
                result.EndCpuLoadPercent != source.EndCpuLoadPercent ||
                result.EndGpuLoadPercent != source.EndGpuLoadPercent ||
                result.StartWindow.TotalSeconds != source.StartWindowSeconds ||
                result.StartDuration.TotalSeconds != source.StartDurationSeconds ||
                result.EndWindow.TotalSeconds != source.EndWindowSeconds ||
                result.EndDuration.TotalSeconds != source.EndDurationSeconds)
            {
                logger.LogWarning("Invalid session detection configuration was replaced with safe default values");
            }

            return result;
        }
    }
}
