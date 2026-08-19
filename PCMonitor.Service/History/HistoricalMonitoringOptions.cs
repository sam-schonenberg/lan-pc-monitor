namespace PCMonitor.Service.History;

public sealed class HistoricalMonitoringOptions
{
    public const string SectionName = "HistoricalMonitoring";

    public bool Enabled { get; set; } = true;
    public int BucketDurationSeconds { get; set; } = 60;
    public double RetentionHours { get; set; } = 24;
    public string? HistoryFilePath { get; set; }
    public int DefaultPageSize { get; set; } = 500;
    public int MaximumPageSize { get; set; } = 2000;
    public double MaintenanceIntervalHours { get; set; } = 24;
    public double IdleDurationMinutes { get; set; } = 5;
    public long MaximumFileSizeMegabytes { get; set; } = 128;
}
