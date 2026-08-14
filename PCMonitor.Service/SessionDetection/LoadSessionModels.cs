namespace PCMonitor.Service.SessionDetection;

public sealed record SensorStatistics(
    string Id,
    string Hardware,
    string Name,
    string Type,
    float Current,
    float Min,
    float Max,
    double Average,
    long SampleCount,
    string? Unit);

public sealed record LoadSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DurationSeconds,
    float? CurrentCpuLoad,
    float? CurrentGpuLoad,
    ProcessCpuReading? CurrentDominantProcess,
    ProcessSessionStatistics? PrimaryProcess,
    IReadOnlyCollection<SensorStatistics> Sensors);

public sealed record LoadSessionStatus(LoadSessionState State, LoadSession? Session);

public sealed record CompletedLoadSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationSeconds,
    ProcessSessionStatistics? PrimaryProcess);

public sealed record CompletedLoadSessionStatus(LoadSessionState State, CompletedLoadSession? Session);

internal sealed class MutableSensorStatistics(
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
    public float Current { get; private set; } = initialValue;
    public float Min { get; private set; } = initialValue;
    public float Max { get; private set; } = initialValue;
    public long Count { get; private set; } = 1;

    public void Add(string hardware, string name, string type, string? unit, float value)
    {
        Hardware = hardware;
        Name = name;
        Type = type;
        Unit = unit;
        Current = value;
        Min = Math.Min(Min, value);
        Max = Math.Max(Max, value);
        _sum += value;
        Count++;
    }

    public SensorStatistics ToSnapshot() => new(
        Id, Hardware, Name, Type, Current, Min, Max, _sum / Count, Count, Unit);
}
