using LibreHardwareMonitor.Hardware;
using PCMonitor.Service.Models;

namespace PCMonitor.Service.Sensors;

public sealed class LibreHardwareMonitorSensorProvider : ISensorProvider, IDisposable
{
    private readonly ILogger<LibreHardwareMonitorSensorProvider> _logger;
    private readonly Lock _sync = new();
    private Computer? _computer;

    public LibreHardwareMonitorSensorProvider(ILogger<LibreHardwareMonitorSensorProvider> logger)
    {
        _logger = logger;

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true
            };
            _computer.Open();

            foreach (var hardware in _computer.Hardware)
            {
                _logger.LogInformation("Detected hardware: {Hardware} ({Type})", hardware.Name, hardware.HardwareType);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "LibreHardwareMonitor initialization failed; sensor snapshots will be empty");
            CloseComputer();
        }
    }

    public IReadOnlyCollection<SensorReading> GetSensorReadings()
    {
        lock (_sync)
        {
            if (_computer is null)
            {
                return Array.Empty<SensorReading>();
            }

            var readings = new List<SensorReading>();
            foreach (var hardware in _computer.Hardware)
            {
                ReadHardware(hardware, readings);
            }

            return readings;
        }
    }

    private void ReadHardware(IHardware hardware, List<SensorReading> readings)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to update hardware {Hardware}", hardware.Name);
            return;
        }

        foreach (var sensor in hardware.Sensors)
        {
            try
            {
                if (sensor.Value is null)
                {
                    continue;
                }

                readings.Add(new SensorReading(
                    sensor.Identifier.ToString(),
                    hardware.Name,
                    sensor.Name,
                    sensor.SensorType.ToString(),
                    sensor.Value,
                    GetUnit(sensor.SensorType)));
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Skipping unreadable sensor on {Hardware}", hardware.Name);
            }
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            ReadHardware(subHardware, readings);
        }
    }

    private static string? GetUnit(SensorType type) => type switch
    {
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Clock => "MHz",
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Fan => "RPM",
        SensorType.Flow => "L/h",
        SensorType.Power => "W",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Factor => null,
        SensorType.Frequency => "Hz",
        SensorType.Throughput => "B/s",
        SensorType.TimeSpan => "s",
        SensorType.Energy => "mWh",
        SensorType.Noise => "dBA",
        SensorType.Conductivity => "µS/cm",
        SensorType.Humidity => "%",
        _ => null
    };

    public void Dispose()
    {
        lock (_sync)
        {
            CloseComputer();
        }
    }

    private void CloseComputer()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to close LibreHardwareMonitor cleanly");
        }
        finally
        {
            _computer = null;
        }
    }
}
