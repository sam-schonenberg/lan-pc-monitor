namespace PCMonitor.Application.Models;

public static class SensorDisplayText
{
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
