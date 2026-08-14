namespace PCMonitor.Service.SessionDetection;

public sealed class ProcessMonitoringOptions
{
    public const string SectionName = "ProcessMonitoring";

    public bool Enabled { get; set; } = true;
    public double SamplingIntervalSeconds { get; set; } = 5;
    public int TopProcessCount { get; set; } = 3;
}
