using SQLite;
namespace PCMonitor.Application.Data.Entities;
[Table("Alerts")]
public sealed class AlertEntity
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public DateTimeOffset Timestamp { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string SensorId { get; set; } = string.Empty;
    public string SensorName { get; set; } = string.Empty;
    public string Hardware { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Threshold { get; set; }
    public string? Unit { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
}
