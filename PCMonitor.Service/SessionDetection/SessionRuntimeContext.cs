namespace PCMonitor.Service.SessionDetection;

public sealed class SessionRuntimeContext
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, MutableProcessSessionStatistics> _processes =
        new(StringComparer.OrdinalIgnoreCase);
    private LoadSessionState _state;
    private Guid? _sessionId;
    private DateTimeOffset? _startedAt;
    private ProcessSampleSnapshot? _latestProcessSample;
    private long _sequence;

    public void CreateCandidate(Guid sessionId, DateTimeOffset startedAt)
    {
        lock (_sync)
        {
            _state = LoadSessionState.Candidate;
            _sessionId = sessionId;
            _startedAt = startedAt;
            _latestProcessSample = null;
            _processes.Clear();
        }
    }

    public void Promote(Guid sessionId)
    {
        lock (_sync)
        {
            if (_sessionId == sessionId)
            {
                _state = LoadSessionState.Active;
            }
        }
    }

    public void Clear(Guid sessionId)
    {
        lock (_sync)
        {
            if (_sessionId != sessionId)
            {
                return;
            }

            _state = LoadSessionState.Idle;
            _sessionId = null;
            _startedAt = null;
            _latestProcessSample = null;
            _processes.Clear();
        }
    }

    public bool RecordProcessSample(Guid sessionId, DateTimeOffset timestamp, IReadOnlyList<ProcessCpuReading> readings)
    {
        lock (_sync)
        {
            if (_sessionId != sessionId || _state is not (LoadSessionState.Candidate or LoadSessionState.Active) ||
                readings.Count == 0)
            {
                return false;
            }

            var dominant = readings[0];
            foreach (var reading in readings)
            {
                if (!_processes.TryGetValue(reading.Name, out var statistics))
                {
                    statistics = new MutableProcessSessionStatistics(reading.Name);
                    _processes[reading.Name] = statistics;
                }

                statistics.Add(reading.CpuPercent, reading.Name.Equals(dominant.Name, StringComparison.OrdinalIgnoreCase));
            }

            _latestProcessSample = new ProcessSampleSnapshot(++_sequence, sessionId, timestamp, dominant);
            return true;
        }
    }

    public SessionRuntimeSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new SessionRuntimeSnapshot(
                _state,
                _sessionId,
                _startedAt,
                _latestProcessSample,
                GetPrimaryProcessLocked());
        }
    }

    private ProcessSessionStatistics? GetPrimaryProcessLocked() => _processes.Values
        .OrderByDescending(process => process.DominantSampleCount)
        .ThenByDescending(process => process.AverageCpuPercent)
        .Select(process => process.ToSnapshot())
        .FirstOrDefault();

    private sealed class MutableProcessSessionStatistics(string name)
    {
        private double _sum;
        public string Name { get; } = name;
        public long DominantSampleCount { get; private set; }
        public long CpuSampleCount { get; private set; }
        public double MaxCpuPercent { get; private set; }
        public double AverageCpuPercent => CpuSampleCount == 0 ? 0 : _sum / CpuSampleCount;

        public void Add(double cpuPercent, bool dominant)
        {
            _sum += cpuPercent;
            CpuSampleCount++;
            MaxCpuPercent = Math.Max(MaxCpuPercent, cpuPercent);
            if (dominant) DominantSampleCount++;
        }

        public ProcessSessionStatistics ToSnapshot() => new(
            Name, DominantSampleCount, CpuSampleCount, AverageCpuPercent, MaxCpuPercent);
    }
}

public sealed record ProcessCpuReading(string Name, double CpuPercent);

public sealed record ProcessSampleSnapshot(
    long Sequence,
    Guid SessionId,
    DateTimeOffset Timestamp,
    ProcessCpuReading Dominant);

public sealed record ProcessSessionStatistics(
    string Name,
    long DominantSampleCount,
    long CpuSampleCount,
    double AverageCpuPercent,
    double MaxCpuPercent);

public sealed record SessionRuntimeSnapshot(
    LoadSessionState State,
    Guid? SessionId,
    DateTimeOffset? StartedAt,
    ProcessSampleSnapshot? LatestProcessSample,
    ProcessSessionStatistics? PrimaryProcess);
