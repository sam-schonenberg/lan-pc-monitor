using SQLite;
namespace PCMonitor.Application.Data.Entities;
[Table("AppSettings")]
public sealed class AppSettingEntity
{
    [PrimaryKey] public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
