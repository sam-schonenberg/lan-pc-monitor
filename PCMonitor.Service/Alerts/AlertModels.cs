namespace PCMonitor.Service.Alerts;

public enum AlertSeverity
{
    Warning,
    Critical
}

public sealed record MonitorAlert(
    Guid Id,
    DateTimeOffset Timestamp,
    AlertSeverity Severity,
    string SensorId,
    string Hardware,
    string SensorName,
    string SensorType,
    double Value,
    double Threshold,
    string? Unit,
    string Message);

public sealed record AlertHistoryResponse(
    DateTimeOffset? From,
    DateTimeOffset? To,
    IReadOnlyList<MonitorAlert> Alerts);

public sealed record LiveEventEnvelope(string Type, object Data);
