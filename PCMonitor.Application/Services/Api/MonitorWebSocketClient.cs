using System.Net.WebSockets;
using System.Text.Json;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.Services.Api;
public sealed class MonitorWebSocketClient(MonitorApiClient api, AlertRepository alerts) : IAsyncDisposable
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    public event EventHandler<SensorSnapshotDto>? SensorsReceived;
    public event EventHandler<MonitorAlertDto>? AlertReceived;
    public async Task ConnectAsync(CancellationToken token = default)
    {
        await DisconnectAsync();
        var baseUri = await api.GetBaseUriAsync();
        var uri = new UriBuilder(baseUri) { Scheme = "ws", Path = "/ws/sensors" }.Uri;
        _socket = new ClientWebSocket();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        await _socket.ConnectAsync(uri, token);
        _ = ReceiveAsync(_lifetime.Token);
    }
    public async Task DisconnectAsync()
    {
        _lifetime?.Cancel();
        if (_socket?.State == WebSocketState.Open)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnect", CancellationToken.None);
        _socket?.Dispose(); _socket = null; _lifetime?.Dispose(); _lifetime = null;
    }
    private async Task ReceiveAsync(CancellationToken token)
    {
        var buffer = new byte[128 * 1024];
        try
        {
            while (_socket?.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);
                var envelope = JsonSerializer.Deserialize<LiveEventEnvelopeDto>(stream.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (envelope is null) continue;
                if (envelope.Type == "sensors")
                {
                    var snapshot = envelope.Data.Deserialize<SensorSnapshotDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (snapshot is not null) SensorsReceived?.Invoke(this, snapshot);
                }
                else if (envelope.Type == "alert")
                {
                    var alert = envelope.Data.Deserialize<MonitorAlertDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (alert is not null) { await alerts.SaveAsync([alert]); AlertReceived?.Invoke(this, alert); }
                }
            }
        }
        catch (Exception) when (token.IsCancellationRequested || _socket?.State != WebSocketState.Open) { }
        catch (JsonException) { }
    }
    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
