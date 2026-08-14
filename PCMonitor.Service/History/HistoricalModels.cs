namespace PCMonitor.Service.History;

public sealed record HistoricalSensorReading(
    string Id,
    string Hardware,
    string Name,
    string Type,
    string? Unit,
    float Min,
    float Max,
    double Average,
    long SampleCount);

public sealed record HistoricalSnapshot(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<HistoricalSensorReading> Sensors,
    Guid? SessionId = null,
    HistoricalProcessSummary? DominantProcess = null);

public sealed record HistoricalProcessSummary(
    string Name,
    double AverageCpuPercent,
    double MaxCpuPercent,
    long SampleCount);

public sealed record HistoricalHistoryResponse(
    DateTimeOffset? From,
    DateTimeOffset? To,
    int ResolutionSeconds,
    IReadOnlyList<HistoricalSnapshot> Snapshots);

internal sealed class MutableHistoricalSensor(
    string id,
    string hardware,
    string name,
    string type,
    string? unit,
    float initialValue)
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
        Hardware = hardware;
        Name = name;
        Type = type;
        Unit = unit;
        Min = Math.Min(Min, value);
        Max = Math.Max(Max, value);
        _sum += value;
        Count++;
    }

    public HistoricalSensorReading ToSnapshot() =>
        new(Id, Hardware, Name, Type, Unit, Min, Max, _sum / Count, Count);
}

internal sealed class MutableHistoricalProcess(string name, double initialCpuPercent)
{
    private double _sum = initialCpuPercent;

    public string Name { get; } = name;
    public double Max { get; private set; } = initialCpuPercent;
    public long Count { get; private set; } = 1;

    public void Add(double cpuPercent)
    {
        _sum += cpuPercent;
        Max = Math.Max(Max, cpuPercent);
        Count++;
    }

    public HistoricalProcessSummary ToSnapshot() => new(Name, _sum / Count, Max, Count);
}
