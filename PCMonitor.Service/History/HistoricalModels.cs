namespace PCMonitor.Service.History;

public sealed record HistoricalSensorReading(string Id, string Hardware, string Name, string Type, string? Unit, float Min, float Max, double Average, long SampleCount);

public sealed record HistoricalSnapshot(DateTimeOffset StartTime, DateTimeOffset EndTime,
    IReadOnlyList<HistoricalSensorReading> Sensors, Guid? SessionId = null,
    HistoricalProcessSummary? DominantProcess = null, long Sequence = 0);

public sealed record HistoricalProcessSummary(string Name, double AverageCpuPercent, double MaxCpuPercent, long SampleCount);

// Retained for callers that consume the internal history representation.
public sealed record HistoricalHistoryResponse(DateTimeOffset? From, DateTimeOffset? To,
    int ResolutionSeconds, IReadOnlyList<HistoricalSnapshot> Snapshots);

public enum HistoryResolution { Minute, Hour, Day }

public sealed record SensorCatalogEntry(int Id, string Key, string Hardware, string Name, string Type, string? Unit);
public sealed record SensorCatalogResponse(string Version, IReadOnlyList<SensorCatalogEntry> Sensors);
public sealed record CompactHistoricalSensorReading(int SensorId, double Min, double Max, double Avg, long Count);
public sealed record CompactHistoricalSnapshot(long Sequence, DateTimeOffset StartTime, DateTimeOffset EndTime,
    IReadOnlyList<CompactHistoricalSensorReading> Sensors, Guid? SessionId = null,
    HistoricalProcessSummary? DominantProcess = null);
public sealed record CompactHistoryResponse(string CatalogVersion, string Resolution, long? FromSequence,
    long? ToSequence, bool HasMore, long? NextSequence, IReadOnlyList<CompactHistoricalSnapshot> Snapshots,
    long? AvailableToSequence = null, int RemainingBuckets = 0, long? PreviousSequence = null);

public sealed record HistorySequenceRange(long FromSequence, long ToSequence, int BucketCount);
public sealed record HistoryManifestResponse(
    Guid StreamId,
    string CatalogVersion,
    long? OldestSequence,
    long? NewestSequence,
    int BucketCount,
    DateTimeOffset? OldestTimestamp,
    DateTimeOffset? NewestTimestamp,
    int ResolutionSeconds,
    double RetentionHours,
    IReadOnlyList<HistorySequenceRange> SequenceRanges,
    DateTimeOffset GeneratedAt);

internal sealed class MutableHistoricalSensor(string id, string hardware, string name, string type, string? unit, float initialValue)
{
    private double _sum = initialValue;
    public string Id { get; } = id;
    public string Hardware { get; private set; } = hardware;
    public string Name { get; private set; } = name;
    public string Type { get; private set; } = type;
    public string? Unit { get; private set; } = unit;
    public float Min { get; private set; } = initialValue;
    public float Max { get; private set; } = initialValue;
    public long Count { get; private set; } = 1;
    public void Add(string hardware, string name, string type, string? unit, float value)
    {
        Hardware = hardware; Name = name; Type = type; Unit = unit;
        Min = Math.Min(Min, value); Max = Math.Max(Max, value); _sum += value; Count++;
    }
    public HistoricalSensorReading ToSnapshot() => new(Id, Hardware, Name, Type, Unit, Min, Max, _sum / Count, Count);
}

internal sealed class MutableHistoricalProcess(string name, double initialCpuPercent)
{
    private double _sum = initialCpuPercent;
    public string Name { get; } = name;
    public double Max { get; private set; } = initialCpuPercent;
    public long Count { get; private set; } = 1;
    public void Add(double cpuPercent) { _sum += cpuPercent; Max = Math.Max(Max, cpuPercent); Count++; }
    public HistoricalProcessSummary ToSnapshot() => new(Name, _sum / Count, Max, Count);
}
