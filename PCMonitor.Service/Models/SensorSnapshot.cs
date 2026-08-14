namespace PCMonitor.Service.Models;

public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyCollection<SensorReading> Sensors);
