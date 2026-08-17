using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace PCMonitor.Service.Sensors;

/// <summary>Turns vendor-specific sensor labels into stable, user-facing descriptions.</summary>
public static partial class SensorDisplayName
{
    public static string Format(HardwareType hardwareType, string rawName, SensorType sensorType)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return MetricName(sensorType) ?? "Sensor";
        }

        var name = Whitespace().Replace(rawName.Trim(), " ");

        if (hardwareType == HardwareType.Cpu)
        {
            name = CpuCoreThread().Replace(name, match =>
                $"CPU Core {match.Groups[1].Value}, Thread {match.Groups[2].Value}");
            name = CpuCore().Replace(name, match => $"CPU Core {match.Groups[1].Value}");
            name = BareCore().Replace(name, match => $"CPU Core {match.Groups[1].Value}");
            name = BareThread().Replace(name, match => $"CPU Thread {match.Groups[1].Value}");
            name = Regex.Replace(name, @"^CPU Total$", "Overall CPU", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^CPU Package$", "CPU Package", RegexOptions.IgnoreCase);
        }
        else if (IsGpu(hardwareType))
        {
            name = Regex.Replace(name, @"^GPU Hot Spot$", "GPU Hotspot", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^GPU Core$", "GPU Core", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"^GPU Memory$", "GPU Memory", RegexOptions.IgnoreCase);
        }
        else if (hardwareType == HardwareType.Memory && name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
        {
            name = "System Memory";
        }

        var metric = MetricName(sensorType);
        return metric is null || AlreadyDescribesMetric(name, sensorType)
            ? name
            : $"{name} {metric}";
    }

    private static bool IsGpu(HardwareType type) => type is
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static string? MetricName(SensorType type) => type switch
    {
        SensorType.Load => "Usage",
        SensorType.Temperature => "Temperature",
        SensorType.Clock => "Clock Speed",
        SensorType.Voltage => "Voltage",
        SensorType.Current => "Current",
        SensorType.Power => "Power Draw",
        SensorType.Fan => "Fan Speed",
        SensorType.Throughput => "Transfer Rate",
        SensorType.Frequency => "Frequency",
        SensorType.Energy => "Energy Used",
        SensorType.Noise => "Noise Level",
        SensorType.Humidity => "Humidity",
        _ => null
    };

    private static bool AlreadyDescribesMetric(string name, SensorType type)
    {
        var terms = type switch
        {
            SensorType.Load => new[] { "load", "usage", "utilization", "capacity" },
            SensorType.Temperature => new[] { "temperature", "temp", "hotspot", "hot spot", "tjmax" },
            SensorType.Clock => new[] { "clock", "speed" },
            SensorType.Voltage => new[] { "voltage", "volt" },
            SensorType.Current => new[] { "current" },
            SensorType.Power => new[] { "power", "watt" },
            SensorType.Fan => new[] { "fan", "rpm" },
            SensorType.Throughput => new[] { "rate", "throughput" },
            SensorType.Frequency => new[] { "frequency" },
            SensorType.Energy => new[] { "energy" },
            SensorType.Noise => new[] { "noise" },
            SensorType.Humidity => new[] { "humidity" },
            _ => Array.Empty<string>()
        };

        return terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"^CPU Core #?(\d+) Thread #?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CpuCoreThread();

    [GeneratedRegex(@"^CPU Core #?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CpuCore();

    [GeneratedRegex(@"^Core #?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BareCore();

    [GeneratedRegex(@"^Thread #?(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BareThread();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
