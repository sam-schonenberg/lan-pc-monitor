using PCMonitor.Service.Models;

namespace PCMonitor.Service.SessionDetection;

public sealed class LoadSensorSelector
{
    public LoadValues Select(SensorSnapshot snapshot)
    {
        var loadSensors = snapshot.Sensors.Where(sensor =>
            sensor.Value is not null &&
            sensor.Type.Equals("Load", StringComparison.OrdinalIgnoreCase));

        var cpu = SelectBest(loadSensors, IsCpuSensor, ScoreCpu);
        var gpu = SelectBest(loadSensors, IsGpuSensor, ScoreGpu);
        return new LoadValues(cpu?.Value, gpu?.Value);
    }

    private static SensorReading? SelectBest(
        IEnumerable<SensorReading> sensors,
        Func<SensorReading, bool> predicate,
        Func<SensorReading, int> score) =>
        sensors.Where(predicate).OrderByDescending(score).FirstOrDefault();

    private static bool IsCpuSensor(SensorReading sensor) =>
        sensor.Id.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
        sensor.Hardware.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
        sensor.Hardware.Contains("processor", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuSensor(SensorReading sensor) =>
        sensor.Id.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
        sensor.Hardware.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
        sensor.Hardware.Contains("radeon", StringComparison.OrdinalIgnoreCase) ||
        sensor.Hardware.Contains("graphics", StringComparison.OrdinalIgnoreCase);

    private static int ScoreCpu(SensorReading sensor)
    {
        var name = sensor.Name.ToLowerInvariant();
        if (name is "cpu total" or "cpu total load" or "total cpu") return 100;
        if (name.Contains("total")) return 80;
        if (name.Contains("package")) return 60;
        return 10;
    }

    private static int ScoreGpu(SensorReading sensor)
    {
        var name = sensor.Name.ToLowerInvariant();
        if (name is "gpu core" or "gpu core load") return 100;
        if (name.Contains("core")) return 80;
        if (name.Contains("total")) return 70;
        return 10;
    }
}

public readonly record struct LoadValues(float? Cpu, float? Gpu);
