namespace PCMonitor.Application.Models;

public static class SensorDisplayText
{
    public static string PickerLabel(string hardware, string name, string type, string? unit)
    {
        var label = PickerLabel(name, type);
        return CommonSensorPriority(hardware, name, type, unit) > 0 ? $"★ {label}" : label;
    }

    public static string PickerLabel(string name, string type)
    {
        var label = FriendlyType(type);
        return string.IsNullOrWhiteSpace(label) || DescribesMeasurement(name, type)
            ? name
            : $"{name} · {label}";
    }

    public static string FriendlyType(string type) => type switch
    {
        "Load" => "Usage",
        "Temperature" => "Temperature",
        "Clock" => "Clock speed",
        "Voltage" => "Voltage",
        "Current" => "Current",
        "Fan" => "Speed",
        "Flow" => "Flow rate",
        "Control" => "Control",
        "Level" => "Level",
        "Power" => "Power draw",
        "Data" or "SmallData" => "Amount",
        "Throughput" => "Transfer rate",
        "TimeSpan" => "Time",
        "Frequency" => "Frequency",
        "Energy" => "Energy used",
        "Noise" => "Noise level",
        "Conductivity" => "Conductivity",
        "Humidity" => "Humidity",
        _ => type
    };

    public static int CommonSensorPriority(string hardware, string name, string type, string? unit)
    {
        var temperature = type.Equals("Temperature", StringComparison.OrdinalIgnoreCase);
        var usage = type.Equals("Load", StringComparison.OrdinalIgnoreCase) && unit == "%";
        var fan = type.Equals("Fan", StringComparison.OrdinalIgnoreCase);
        var cpu = ContainsAny(hardware, "cpu", "intel", "amd", "ryzen") || Contains(name, "cpu");
        var gpu = ContainsAny(hardware, "gpu", "nvidia", "radeon", "graphics") || Contains(name, "gpu");

        if (temperature && cpu && Contains(name, "package")) return 1000;
        if (temperature && gpu && Contains(name, "core")) return 990;
        if (temperature && cpu && Contains(name, "average")) return 980;
        if (temperature && cpu && Contains(name, "core max")) return 970;
        if (usage && cpu && ContainsAny(name, "overall cpu", "cpu total", "total cpu")) return 900;
        if (usage && gpu && Contains(name, "core")) return 890;
        if (usage && ContainsAny(name, "memory usage", "ram usage", "memory load"))
            return Contains(hardware, "total memory") ? 880 : Contains(hardware, "virtual") ? 0 : 870;
        if (fan && cpu && Contains(name, "cpu fan")) return 800;
        if (fan && gpu && Contains(name, "gpu fan")) return 790;
        return 0;
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.OrdinalIgnoreCase);
    private static bool ContainsAny(string value, params string[] terms) => terms.Any(term => Contains(value, term));

    private static bool DescribesMeasurement(string name, string type)
    {
        var terms = type switch
        {
            "Load" => new[] { "usage", "load", "utilization", "capacity" },
            "Temperature" => new[] { "temperature", "temp", "hotspot", "hot spot" },
            "Clock" => new[] { "clock", "frequency" },
            "Voltage" => new[] { "voltage", "volt" },
            "Current" => new[] { "current" },
            // A sensor called "GPU Fan" still needs to say whether it is speed or control.
            "Fan" => new[] { "speed", "rpm" },
            "Flow" => new[] { "flow" },
            "Control" => new[] { "control" },
            "Level" => new[] { "level" },
            "Power" => new[] { "power", "watt" },
            "Data" or "SmallData" => new[] { "used", "free", "available", "amount", "capacity" },
            "Throughput" => new[] { "rate", "throughput" },
            "TimeSpan" => new[] { "time", "duration" },
            "Frequency" => new[] { "frequency" },
            "Energy" => new[] { "energy" },
            "Noise" => new[] { "noise" },
            "Conductivity" => new[] { "conductivity" },
            "Humidity" => new[] { "humidity" },
            _ => Array.Empty<string>()
        };

        return terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
