namespace PCMonitor.Application.Models;

public sealed record SensorChartPoint(
    DateTimeOffset Timestamp,
    double Average,
    double Minimum,
    double Maximum,
    long SampleCount);

public enum SensorChartResolution { Minute, Hour, Day }
