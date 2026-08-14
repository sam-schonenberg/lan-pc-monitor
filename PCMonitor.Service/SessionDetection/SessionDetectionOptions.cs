namespace PCMonitor.Service.SessionDetection;

public sealed class SessionDetectionOptions
{
    public const string SectionName = "SessionDetection";

    public bool Enabled { get; set; } = true;
    public double StartCpuLoadPercent { get; set; } = 40;
    public double StartGpuLoadPercent { get; set; } = 40;
    public double StartWindowSeconds { get; set; } = 10;
    public double StartDurationSeconds { get; set; } = 10;
    public double EndCpuLoadPercent { get; set; } = 20;
    public double EndGpuLoadPercent { get; set; } = 20;
    public double EndWindowSeconds { get; set; } = 30;
    public double EndDurationSeconds { get; set; } = 90;
}
