using PCMonitor.Service.Models;

namespace PCMonitor.Service.Sensors;

public interface ISensorProvider
{
    IReadOnlyCollection<SensorReading> GetSensorReadings();
}
