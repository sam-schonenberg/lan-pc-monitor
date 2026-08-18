using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application.ViewModels;

public partial class AlertsViewModel : ObservableObject
{
    private readonly AlertSyncService _sync;
    private readonly AlertRepository _repository;
    private readonly MonitorApiClient _api;
    private readonly IAppSettingsService _settings;
    private readonly CurrentSensorStateService _sensors;
    private readonly List<AlertEntity> _allAlerts = [];
    private bool _subscribed;
    private bool _loading;
    public ObservableCollection<AlertEntity> Alerts { get; } = [];
    public ObservableCollection<AlertMetricViewModel> Metrics { get; } = [];
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial string NotificationStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasAlerts { get; set; }
    [ObservableProperty] public partial bool HasMetrics { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial string OverallStatus { get; set; } = "Checking sensors";
    [ObservableProperty] public partial string SummaryText { get; set; } = string.Empty;
    [ObservableProperty] public partial Color OverallStatusColor { get; set; } = Color.FromArgb("#64748B");
    [ObservableProperty] public partial string SelectedSeverity { get; set; } = "all";

    public AlertsViewModel(AlertSyncService sync, AlertRepository repository, MonitorApiClient api,
        IAppSettingsService settings, CurrentSensorStateService sensors)
    {
        _sync = sync; _repository = repository; _api = api; _settings = settings; _sensors = sensors;
    }

    public void StartLiveUpdates()
    {
        if (_subscribed) return;
        _sensors.SnapshotReceived += OnSnapshotReceived; _subscribed = true;
    }
    public void StopLiveUpdates()
    {
        if (!_subscribed) return;
        _sensors.SnapshotReceived -= OnSnapshotReceived; _subscribed = false;
    }
    private void OnSnapshotReceived(object? sender, SensorSnapshotDto snapshot) => UpdateLiveValues(snapshot);

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        IsRefreshing = true;
        string? offline = null;
        try { await _sync.SyncAsync(); }
        catch (Exception exception) { offline = exception.Message; }
        try
        {
            var live = await _api.GetAlertStatusAsync();
            Metrics.Clear();
            foreach (var metric in live.Sensors.OrderByDescending(RiskRank).ThenBy(x => x.Category).ThenBy(x => x.SensorName))
                Metrics.Add(new(metric));
            HasMetrics = Metrics.Count > 0;
            Status = $"Updated {live.Timestamp.ToLocalTime():t}";
        }
        catch (Exception exception) { offline ??= exception.Message; }
        try
        {
            var push = await _api.GetNotificationStatusAsync();
            NotificationStatus = !push.Enabled || !push.Configured ? "Phone notifications need service setup"
                : await _settings.GetNotificationsEnabledAsync() ? "Phone notifications enabled"
                : "Phone notifications are off · Enable in Settings";
        }
        catch { NotificationStatus = "Notification status unavailable"; }
        try { _allAlerts.Clear(); _allAlerts.AddRange(await _repository.GetAllAsync()); }
        catch (Exception exception) { offline ??= exception.Message; }
        ApplyFilter(); UpdateSummary();
        if (offline is not null) Status = $"Offline · showing saved data";
        IsRefreshing = false; _loading = false;
    }

    [RelayCommand]
    private void SetSeverity(string severity)
    { SelectedSeverity = severity.ToLowerInvariant(); ApplyFilter(); }

    private void ApplyFilter()
    {
        Alerts.Clear();
        foreach (var alert in _allAlerts.Where(x => SelectedSeverity == "all" ||
                     x.Severity.Equals(SelectedSeverity, StringComparison.OrdinalIgnoreCase))) Alerts.Add(alert);
        HasAlerts = Alerts.Count > 0;
    }

    private void UpdateSummary()
    {
        var critical = Metrics.Count(x => x.State == "critical");
        var warning = Metrics.Count(x => x.State is "warning" or "pending");
        if (critical > 0) { OverallStatus = "Critical attention needed"; OverallStatusColor = Color.FromArgb("#DC2626"); }
        else if (warning > 0) { OverallStatus = "Approaching a limit"; OverallStatusColor = Color.FromArgb("#F59E0B"); }
        else if (Metrics.Count > 0) { OverallStatus = "All monitored values are healthy"; OverallStatusColor = Color.FromArgb("#16A34A"); }
        else { OverallStatus = "Live status unavailable"; OverallStatusColor = Color.FromArgb("#64748B"); }
        SummaryText = Metrics.Count == 0 ? "Connect to your PC to load its alert rules."
            : $"{Metrics.Count} monitored {Pluralize(Metrics.Count, "value", "values")} · {critical} critical · {warning} warning";
    }

    private void UpdateLiveValues(SensorSnapshotDto snapshot)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var readings = snapshot.Sensors.ToDictionary(x => x.Id, StringComparer.Ordinal);
            foreach (var metric in Metrics)
                if (readings.TryGetValue(metric.SensorId, out var reading) && reading.Value is { } value)
                    metric.Update(value);
            Status = $"Live · {snapshot.Timestamp.ToLocalTime():t}";
            UpdateSummary();
        });
    }

    private static int RiskRank(AlertMetricStatusDto x) => x.State switch
    { "critical" => 3, "warning" or "pending" => 2, _ => 1 };
    private static string Pluralize(int count, string singular, string plural) => count == 1 ? singular : plural;
}

public partial class AlertMetricViewModel : ObservableObject
{
    private readonly string _direction;
    public AlertMetricViewModel(AlertMetricStatusDto source)
    {
        Category = source.Category; _direction = source.Direction; SensorId = source.SensorId;
        Hardware = source.Hardware; SensorName = source.SensorName; Unit = source.Unit ?? string.Empty;
        WarningThreshold = source.WarningThreshold; CriticalThreshold = source.CriticalThreshold;
        Condition = source.Condition; PendingSecondsRemaining = source.PendingSecondsRemaining;
        Update(source.Value); State = source.State;
    }
    public string Category { get; }
    public string SensorId { get; }
    public string Hardware { get; }
    public string SensorName { get; }
    public string Unit { get; }
    public double WarningThreshold { get; }
    public double CriticalThreshold { get; }
    public string? Condition { get; }
    public string State { get; private set; }
    public double? PendingSecondsRemaining { get; }
    [ObservableProperty] public partial double Value { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    public string CategoryText => Category switch { "temperature" => "Temperature", "memory" => "Memory pressure",
        "utilization" => "Sustained utilization", "fan" => "Fan health", _ => Category };
    public string StateText => State == "pending" && PendingSecondsRemaining is { } seconds
        ? $"PENDING · {seconds:0}s" : State.ToUpperInvariant();
    public Color StateColor => State switch { "critical" => Color.FromArgb("#DC2626"),
        "warning" or "pending" => Color.FromArgb("#F59E0B"), _ => Color.FromArgb("#16A34A") };
    public string ValueText => $"{Value:0.#}{Unit}";
    public string ThresholdText => _direction == "high" ? $"Critical at {CriticalThreshold:0.#}{Unit}"
        : $"Critical below {CriticalThreshold:0.#}{Unit}";
    public string HeadroomText
    {
        get
        {
            var distance = _direction == "high" ? CriticalThreshold - Value : Value - CriticalThreshold;
            return distance > 0 ? $"{distance:0.#}{Unit} before critical"
                : $"Critical threshold exceeded by {Math.Abs(distance):0.#}{Unit}";
        }
    }

    public void Update(double value)
    {
        Value = value;
        Progress = _direction == "high" ? Math.Clamp(value / CriticalThreshold, 0, 1)
            : Math.Clamp((WarningThreshold - value) / Math.Max(1, WarningThreshold - CriticalThreshold), 0, 1);
        if (_direction == "high") State = value >= CriticalThreshold ? "critical" : value >= WarningThreshold ? "warning" : "safe";
        else State = value <= CriticalThreshold ? "critical" : value <= WarningThreshold ? "warning" : "safe";
        OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(HeadroomText));
        OnPropertyChanged(nameof(StateText)); OnPropertyChanged(nameof(StateColor));
    }
}
