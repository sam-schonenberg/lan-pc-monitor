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
    public async Task<IReadOnlyList<AlertEntity>> GetAllAsync() => await (await database.GetConnectionAsync()).Table<AlertEntity>().OrderByDescending(x => x.TimestampUtcTicks).ToListAsync();
    public async Task<IReadOnlyList<AlertEntity>> GetRecentAsync(string? sensorId, string? minimumSeverity, int limit)
    {
        var rows = await (await database.GetConnectionAsync()).Table<AlertEntity>()
            .OrderByDescending(x => x.TimestampUtcTicks).ToListAsync();
        var minimum = SeverityRank(minimumSeverity);
        return rows.Where(x => string.IsNullOrWhiteSpace(sensorId) || x.SensorId == sensorId)
            .Where(x => SeverityRank(x.Severity) >= minimum)
            .Take(Math.Clamp(limit, 1, 100)).ToArray();
    }
    public async Task<DateTimeOffset?> GetNewestTimestampAsync() => (await GetAllAsync()).FirstOrDefault()?.Timestamp;
    private static AlertEntity ToEntity(MonitorAlertDto alert) => new()
    {
        Id = alert.Id.ToString(), TimestampUtcTicks = alert.Timestamp.UtcTicks, Severity = alert.Severity, SensorId = alert.SensorId,
        SensorName = alert.SensorName, Hardware = alert.Hardware, Value = alert.Value, Threshold = alert.Threshold,
        Unit = alert.Unit, Message = alert.Message
    };
    private static int SeverityRank(string? severity) => severity?.ToLowerInvariant() switch
    {
        "critical" or "error" => 3,
        "warning" or "warn" => 2,
        "info" or "information" => 1,
        _ => 0
    };
}
