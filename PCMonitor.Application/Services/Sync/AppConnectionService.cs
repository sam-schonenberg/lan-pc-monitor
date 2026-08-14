using PCMonitor.Application.Services.Api;
namespace PCMonitor.Application.Services.Sync;

public sealed class AppConnectionService(
    AlertSyncService alertSync,
    MonitorWebSocketClient webSocket)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _started;

    public async Task StartAsync(CancellationToken token = default)
    {
        await _lock.WaitAsync(token);
        try
        {
            if (_started) return;
            try { await alertSync.SyncAsync(token); } catch (MonitorApiException) { }
            try { await webSocket.ConnectAsync(token); _started = true; } catch (Exception) when (!token.IsCancellationRequested) { }
        }
        finally { _lock.Release(); }
    }
}
