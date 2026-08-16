using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services;
namespace PCMonitor.Application.Services.Sync;

public sealed class AppConnectionService(
    AlertSyncService alertSync,
    MonitorWebSocketClient webSocket,
    CurrentSensorStateService currentSensorState)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CurrentSensorStateService _currentSensorState = currentSensorState;
    private bool _started;

    public async Task StartAsync(CancellationToken token = default)
    {
        _ = _currentSensorState; // Ensure the shared state subscribes before the WebSocket connects.
        await _lock.WaitAsync(token);
        try
        {
            if (_started && webSocket.IsConnected) return;
            try { await alertSync.SyncAsync(token); } catch (MonitorApiException) { }
            try { await webSocket.ConnectAsync(token); _started = true; }
            catch (Exception) when (!token.IsCancellationRequested) { _started = false; }
        }
        finally { _lock.Release(); }
    }
}
