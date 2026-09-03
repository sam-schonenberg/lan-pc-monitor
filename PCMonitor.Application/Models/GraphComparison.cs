namespace PCMonitor.Application.Models;

public sealed record SensorGraphSeries(string SensorId, string Name, string? Unit,
    IReadOnlyList<SensorChartPoint> Points);

public sealed record SensorGraphGroup(string CompatibilityKey, string? Unit,
    IReadOnlyList<SensorGraphSeries> Series);

public static class GraphCompatibility
{
    public static string Key(string? measurementType, string? unit)
    {
        var type = Normalize(measurementType);
        var normalizedUnit = NormalizeUnit(unit);
        return $"{type}|{normalizedUnit}";
    }

    public static bool AreCompatible(string? firstType, string? firstUnit, string? secondType, string? secondUnit) =>
        string.Equals(Key(firstType, firstUnit), Key(secondType, secondUnit), StringComparison.Ordinal);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value)
        ? "unknown" : value.Trim().ToLowerInvariant();

    private static string NormalizeUnit(string? unit) => Normalize(unit) switch
    {
        "c" or "°c" or "celsius" => "°c",
        "f" or "°f" or "fahrenheit" => "°f",
        "percent" or "percentage" or "%" => "%",
        "rpm" => "rpm",
        "w" or "watt" or "watts" => "w",
        "v" or "volt" or "volts" => "v",
        "mhz" => "mhz",
        "ghz" => "ghz",
        var value => value
    };
}
