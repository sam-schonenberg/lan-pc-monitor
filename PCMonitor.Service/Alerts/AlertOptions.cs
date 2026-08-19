namespace PCMonitor.Service.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";
    public bool Enabled { get; set; } = true;
    public string RuleStoreFile { get; set; } = string.Empty;
    public double EvaluationIntervalSeconds { get; set; } = 1;
    public TemperatureAlertOptions Temperature { get; set; } = new();
    public HighValueAlertOptions MemoryPressure { get; set; } = new()
    { WarningThreshold = 90, CriticalThreshold = 97, ResetBelow = 85, MinimumDurationSeconds = 30 };
    public HighValueAlertOptions Utilization { get; set; } = new()
    { Enabled = false, WarningThreshold = 95, CriticalThreshold = 99, ResetBelow = 85, MinimumDurationSeconds = 120 };
    public FanAlertOptions Fan { get; set; } = new();
    public double RetentionHours { get; set; } = 24;
}

public sealed class HighValueAlertOptions
{
    public bool Enabled { get; set; } = true;
    public double WarningThreshold { get; set; }
    public double CriticalThreshold { get; set; }
    public double ResetBelow { get; set; }
    public double MinimumDurationSeconds { get; set; }
}

public sealed class FanAlertOptions
{
    public bool Enabled { get; set; } = true;
    public bool MonitorCpuFans { get; set; } = true;
    public bool MonitorGpuFans { get; set; } = true;
    public double WarningBelowRpm { get; set; } = 300;
    public double CriticalBelowRpm { get; set; } = 100;
    public double ResetAboveRpm { get; set; } = 500;
    public double HardwareTemperatureGateCelsius { get; set; } = 70;
    public double MinimumDurationSeconds { get; set; } = 15;
}

public sealed class TemperatureAlertOptions
{
    public double WarningThresholdCelsius { get; set; } = 85;
    public double CriticalThresholdCelsius { get; set; } = 95;
    public double ResetBelowCelsius { get; set; } = 80;
    public double MinimumDurationSeconds { get; set; } = 5;
}
