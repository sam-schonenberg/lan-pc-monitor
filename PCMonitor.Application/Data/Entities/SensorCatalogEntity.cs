using SQLite;
namespace PCMonitor.Application.Data.Entities;
[Table("SensorCatalog")]
public sealed class SensorCatalogEntity
{
    [PrimaryKey] public int TransportId { get; set; }
    [Indexed] public string SensorKey { get; set; } = string.Empty;
    public string Hardware { get; set; } = string.Empty;
    public string SensorName { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string CatalogVersion { get; set; } = string.Empty;
}
