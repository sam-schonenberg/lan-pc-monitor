using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.Services.Sync;
public sealed class HistorySyncService(MonitorApiClient api, HistoryRepository repository)
{
    public async Task SyncAsync(CancellationToken token = default)
    {
        var response = await api.GetHistoryAsync(await repository.GetNewestTimestampAsync(), token);
        await repository.SaveAsync(response.Snapshots);
    }
}
public sealed class AlertSyncService(MonitorApiClient api, AlertRepository repository)
{
    public async Task SyncAsync(CancellationToken token = default)
    {
        var response = await api.GetAlertsAsync(await repository.GetNewestTimestampAsync(), token);
        await repository.SaveAsync(response.Alerts);
    }
}
