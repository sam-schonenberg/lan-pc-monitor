namespace PCMonitor.Service.Alerts;

public sealed class AlertOptions
{
    public const string SectionName = "Alerts";
    public bool Enabled { get; set; } = true;
    public double EvaluationIntervalSeconds { get; set; } = 1;
    public TemperatureAlertOptions Temperature { get; set; } = new();
    public double RetentionHours { get; set; } = 24;
}

public sealed class TemperatureAlertOptions
{
    public double WarningThresholdCelsius { get; set; } = 85;
    public double CriticalThresholdCelsius { get; set; } = 95;
    public double ResetBelowCelsius { get; set; } = 80;
    public double MinimumDurationSeconds { get; set; } = 5;
}
