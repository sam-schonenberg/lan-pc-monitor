namespace PCMonitor.Service.Models;

public sealed record SensorReading(
    string Id,
    string Hardware,
    string Name,
    string Type,
    float? Value,
    string? Unit);
