namespace PCMonitor.Service.Models;

public sealed record ServiceStatus(
    string Status,
    string Service,
    string MachineName,
    DateTimeOffset Timestamp,
    string Version,
    string ApiVersion,
    IReadOnlyList<string> Capabilities);

public sealed record ApiError(string Error);
