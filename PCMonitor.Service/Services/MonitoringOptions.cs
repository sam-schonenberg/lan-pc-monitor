namespace PCMonitor.Service.Services;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int PollingIntervalMilliseconds { get; set; } = 1000;
}
