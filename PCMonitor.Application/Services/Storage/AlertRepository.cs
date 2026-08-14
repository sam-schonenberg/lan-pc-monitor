using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models.Api;
namespace PCMonitor.Application.Services.Storage;
public sealed class AlertRepository(AppDatabase database)
{
    public async Task SaveAsync(IEnumerable<MonitorAlertDto> alerts)
    {
        var connection = await database.GetConnectionAsync();
        foreach (var alert in alerts) await connection.InsertOrReplaceAsync(ToEntity(alert));
    }
    public async Task<IReadOnlyList<AlertEntity>> GetAllAsync() => await (await database.GetConnectionAsync()).Table<AlertEntity>().OrderByDescending(x => x.Timestamp).ToListAsync();
    public async Task<DateTimeOffset?> GetNewestTimestampAsync() => (await GetAllAsync()).FirstOrDefault()?.Timestamp;
    private static AlertEntity ToEntity(MonitorAlertDto alert) => new()
    {
        Id = alert.Id.ToString(), Timestamp = alert.Timestamp, Severity = alert.Severity, SensorId = alert.SensorId,
        SensorName = alert.SensorName, Hardware = alert.Hardware, Value = alert.Value, Threshold = alert.Threshold,
        Unit = alert.Unit, Message = alert.Message
    };
}
