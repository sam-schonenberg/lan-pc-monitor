using SQLite;
namespace PCMonitor.Application.Data.Entities;
[Table("HistoricalSensors")]
public sealed class HistoricalSensorEntity
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public DateTimeOffset BucketStartTime { get; set; }
    public DateTimeOffset BucketEndTime { get; set; }
    [Indexed] public string SensorId { get; set; } = string.Empty;
    public string Hardware { get; set; } = string.Empty;
    public string SensorName { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public float Min { get; set; }
    public float Max { get; set; }
    public double Average { get; set; }
    public long SampleCount { get; set; }
    [Indexed] public string? SessionId { get; set; }
    public string? DominantProcessName { get; set; }
}
