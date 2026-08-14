using PCMonitor.Application.Data;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models.Api;
namespace PCMonitor.Application.Services.Storage;
public sealed class HistoryRepository(AppDatabase database)
{
    public async Task SaveAsync(IEnumerable<HistoricalSnapshotDto> snapshots)
    {
        var connection = await database.GetConnectionAsync();
        foreach (var bucket in snapshots)
        foreach (var sensor in bucket.Sensors)
            await connection.InsertOrReplaceAsync(new HistoricalSensorEntity
            {
                Id = $"{bucket.StartTime.UtcTicks}:{sensor.Id}", BucketStartTime = bucket.StartTime,
                BucketEndTime = bucket.EndTime, SensorId = sensor.Id, Hardware = sensor.Hardware,
                SensorName = sensor.Name, SensorType = sensor.Type, Unit = sensor.Unit, Min = sensor.Min,
                Max = sensor.Max, Average = sensor.Average, SampleCount = sensor.SampleCount,
                SessionId = bucket.SessionId?.ToString(), DominantProcessName = bucket.DominantProcess?.Name
            });
    }
    public async Task<long> CountAsync() => await (await database.GetConnectionAsync()).Table<HistoricalSensorEntity>().CountAsync();
    public async Task<DateTimeOffset?> GetNewestTimestampAsync() => (await (await database.GetConnectionAsync()).Table<HistoricalSensorEntity>().OrderByDescending(x => x.BucketStartTime).FirstOrDefaultAsync())?.BucketStartTime;
}
