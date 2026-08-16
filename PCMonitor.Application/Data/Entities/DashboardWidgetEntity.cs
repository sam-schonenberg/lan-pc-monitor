using SQLite;

namespace PCMonitor.Application.Data.Entities;

[Table("DashboardWidgets")]
public sealed class DashboardWidgetEntity
{
    [PrimaryKey] public string Id { get; set; } = string.Empty;
    [Indexed] public int Position { get; set; }
    public int Type { get; set; }
    public int Width { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ConfigurationJson { get; set; } = "{}";
    public bool IsEnabled { get; set; } = true;
    public long UpdatedUtcTicks { get; set; }
}
