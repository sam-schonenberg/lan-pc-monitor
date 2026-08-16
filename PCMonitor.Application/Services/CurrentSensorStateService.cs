using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Api;

namespace PCMonitor.Application.Services;

public sealed class CurrentSensorStateService
{
    private readonly Dictionary<string, SensorReadingDto> _readings = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public CurrentSensorStateService(MonitorWebSocketClient webSocket)
    {
        webSocket.SensorsReceived += (_, snapshot) =>
        {
            lock (_gate)
            {
                foreach (var sensor in snapshot.Sensors) _readings[sensor.Id] = sensor;
                LastSnapshotTimestamp = snapshot.Timestamp;
            }
            SnapshotReceived?.Invoke(this, snapshot);
        };
    }

    public event EventHandler<SensorSnapshotDto>? SnapshotReceived;
    public DateTimeOffset? LastSnapshotTimestamp { get; private set; }

    public bool TryGet(string sensorId, out SensorReadingDto? reading)
    {
        lock (_gate) return _readings.TryGetValue(sensorId, out reading);
    }
}
