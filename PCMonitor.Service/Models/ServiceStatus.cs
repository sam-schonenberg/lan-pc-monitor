namespace PCMonitor.Service.Models;

public sealed record ServiceStatus(
    string Status,
    string Service,
    string MachineName,
    DateTimeOffset Timestamp);
