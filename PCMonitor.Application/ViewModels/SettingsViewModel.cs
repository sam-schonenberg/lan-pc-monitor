using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.Services.Notifications;
using PCMonitor.Application.Services.Export;

namespace PCMonitor.Application.ViewModels;

public partial class SettingsViewModel(
    IAppSettingsService settings,
    MonitorApiClient api,
    HistoryRepository historyRepository,
    HistorySyncService historySync,
    NotificationRegistrationService notifications,
    HistoryExportService historyExport) : ObservableObject
{
    public ObservableCollection<SensorVisibilityOption> Sensors { get; } = [];
    public ObservableCollection<SensorVisibilityOption> FilteredSensors { get; } = [];
    [ObservableProperty] public partial string Endpoint { get; set; } = string.Empty;
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoadingSensors { get; set; }
    [ObservableProperty] public partial bool IsUpdatingSensorVisibility { get; set; }
    [ObservableProperty] public partial string SensorSearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsSynchronizing { get; set; }
    [ObservableProperty] public partial double SynchronizationProgress { get; set; }
    [ObservableProperty] public partial string SynchronizationStatus { get; set; } = "Ready to synchronize";
    [ObservableProperty] public partial string LastSynchronization { get; set; } = "Never synchronized";
    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial bool IsChangingNotifications { get; set; }
    [ObservableProperty] public partial bool IsExporting { get; set; }
    [ObservableProperty] public partial string ExportStatus { get; set; } = "Export all locally saved sensors as an LLM-friendly CSV file.";
    [ObservableProperty] public partial bool HasLatestExport { get; set; }
    [ObservableProperty] public partial string LatestExportSummary { get; set; } = string.Empty;
    [ObservableProperty] public partial string NotificationStatus { get; set; } = "Checking notification support…";
    public string NotificationButtonText => NotificationsEnabled ? "Disable notifications" : "Enable notifications";
    public bool AreAllSensorsHidden => Sensors.Count > 0 && Sensors.All(sensor => !sensor.IsVisible);
    public string SensorVisibilityButtonText => AreAllSensorsHidden ? "Unhide all" : "Turn all off";
    public event EventHandler? ChangeRequested;

    partial void OnSensorSearchTextChanged(string value) => ApplySensorFilter();

    [RelayCommand]
    public async Task ExportAsync(int hours)
    {
        if (IsExporting || hours is not (1 or 6 or 24)) return;
        IsExporting = true;
        ExportStatus = $"Preparing the last {hours} hour{(hours == 1 ? string.Empty : "s")}…";
        try
        {
            var count = await historyExport.ExportAndShareAsync(TimeSpan.FromHours(hours));
            ExportStatus = count == 0
                ? "No saved sensor readings are available for that period. Synchronize history and try again."
                : $"Prepared {count:N0} sensor readings for sharing.";
            await LoadLatestExportAsync();
        }
        catch (Exception exception) { ExportStatus = $"Could not export sensor history: {exception.Message}"; }
        finally { IsExporting = false; }
    }

    [RelayCommand]
    private async Task ShareLatestExportAsync()
    {
        if (IsExporting) return;
        try
        {
            if (!await historyExport.ShareLatestAsync())
            {
                HasLatestExport = false;
                ExportStatus = "The latest export file is no longer available. Create a new export to share it.";
            }
        }
        catch (Exception exception) { ExportStatus = $"Could not share the latest export: {exception.Message}"; }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Endpoint = await settings.GetApiBaseUrlAsync() ?? "Not configured";
        var lastSync = await settings.GetLastHistorySyncAsync();
        LastSynchronization = lastSync is null ? "Never synchronized" : $"Last synchronized {FormatTimestamp(lastSync.Value)}";
        NotificationsEnabled = await settings.GetNotificationsEnabledAsync();
        OnPropertyChanged(nameof(NotificationButtonText));
        NotificationStatus = notifications.IsAvailable
            ? NotificationsEnabled ? "Critical alerts are registered for this device." : "Notifications are off."
            : "Firebase configuration is not included in this app build.";
        await LoadLatestExportAsync();
        IsLoadingSensors = true;
        try
        {
            var hidden = await settings.GetHiddenSensorIdsAsync();
            var sensors = await historyRepository.GetSensorOptionsAsync();
            Sensors.Clear();
            foreach (var sensor in sensors)
            {
                var option = new SensorVisibilityOption(sensor.SensorId, sensor.SensorName,
                    SensorVisibilityOption.FormatDetails(sensor.SensorType, sensor.Hardware),
                    !hidden.Contains(sensor.SensorId), SetVisibilityAsync);
                option.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(SensorVisibilityOption.IsVisible))
                        NotifySensorVisibilityStateChanged();
                };
                Sensors.Add(option);
            }
            ApplySensorFilter();
            NotifySensorVisibilityStateChanged();
        }
        finally { IsLoadingSensors = false; }
    }

    [RelayCommand]
    public async Task ToggleAllSensorVisibilityAsync()
    {
        if (IsLoadingSensors || IsUpdatingSensorVisibility || Sensors.Count == 0) return;
        IsUpdatingSensorVisibility = true;
        try
        {
            var makeVisible = AreAllSensorsHidden;
            var changedSensors = Sensors.Where(sensor => sensor.IsVisible != makeVisible).ToArray();
            foreach (var sensor in changedSensors)
            {
                await settings.SetSensorHiddenAsync(sensor.Id, !makeVisible);
                sensor.SetVisibilityWithoutSaving(makeVisible);
            }
            Status = makeVisible
                ? $"{changedSensors.Length:N0} sensor{(changedSensors.Length == 1 ? string.Empty : "s")} restored to app views."
                : $"{changedSensors.Length:N0} sensor{(changedSensors.Length == 1 ? string.Empty : "s")} hidden from app views. Recording remains enabled.";
            NotifySensorVisibilityStateChanged();
        }
        finally { IsUpdatingSensorVisibility = false; }
    }

    private void NotifySensorVisibilityStateChanged()
    {
        OnPropertyChanged(nameof(AreAllSensorsHidden));
        OnPropertyChanged(nameof(SensorVisibilityButtonText));
    }

    private void ApplySensorFilter()
    {
        var query = SensorSearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? Sensors
            : Sensors.Where(sensor => sensor.Matches(query));
        FilteredSensors.Clear();
        foreach (var sensor in matches) FilteredSensors.Add(sensor);
    }

    private async Task LoadLatestExportAsync()
    {
        var latest = await historyExport.GetLatestAsync();
        HasLatestExport = latest is not null;
        LatestExportSummary = latest is null ? string.Empty
            : $"{latest.FileName}\nCreated {FormatTimestamp(latest.CreatedAtUtc)} · {FormatHours(latest.Hours)} · {latest.ReadingCount:N0} readings\nTap to share again";
    }

    private static string FormatHours(int hours) => hours == 1 ? "Last hour" : $"Last {hours} hours";

    private async Task SetVisibilityAsync(string sensorId, bool visible)
    {
        try
        {
            await settings.SetSensorHiddenAsync(sensorId, !visible);
            Status = visible ? "Sensor shown in app views. Recording remains enabled."
                : "Sensor hidden from app views. Recording remains enabled.";
        }
        finally { NotifySensorVisibilityStateChanged(); }
    }

    [RelayCommand] private async Task TestAsync()
    {
        try { Status = $"Connected to {(await api.GetStatusAsync()).MachineName}"; }
        catch (MonitorApiException exception) { Status = exception.Message; }
    }
    [RelayCommand]
    private async Task SynchronizeAsync()
    {
        if (IsSynchronizing) return;
        IsSynchronizing = true;
        SynchronizationProgress = 0.03;
        SynchronizationStatus = "Starting history synchronization…";
        try
        {
            var progress = new Progress<HistorySyncProgress>(update =>
            {
                SynchronizationProgress = update.BarProgress;
                SynchronizationStatus = update.Message;
            });
            await historySync.SyncAsync(progress);
            var lastSync = await settings.GetLastHistorySyncAsync();
            LastSynchronization = lastSync is null ? "Never synchronized" : $"Last synchronized {FormatTimestamp(lastSync.Value)}";
            await LoadAsync();
        }
        catch (Exception exception) { SynchronizationStatus = $"Synchronization failed: {exception.Message}"; }
        finally { IsSynchronizing = false; }
    }
    [RelayCommand] private async Task ChangePcAsync()
    {
        if (NotificationsEnabled)
        {
            try { await notifications.DisableAsync(); } catch { }
        }
        await settings.ClearApiBaseUrlAsync(); ChangeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ToggleNotificationsAsync()
    {
        if (IsChangingNotifications) return;
        IsChangingNotifications = true;
        try
        {
            if (NotificationsEnabled)
            {
                await notifications.DisableAsync();
                NotificationsEnabled = false;
                NotificationStatus = "Notifications disabled for this device.";
            }
            else
            {
                var result = await notifications.EnableAsync();
                NotificationsEnabled = result.Enabled;
                NotificationStatus = result.Message;
            }
            OnPropertyChanged(nameof(NotificationButtonText));
        }
        catch (Exception exception) { NotificationStatus = $"Could not update notifications: {exception.Message}"; }
        finally { IsChangingNotifications = false; }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy, HH:mm");
    }
}

public sealed class SensorVisibilityOption : ObservableObject
{
    private readonly Func<string, bool, Task> _save;
    private bool _isVisible;
    public SensorVisibilityOption(string id, string displayName, string details, bool isVisible, Func<string, bool, Task> save)
    { Id = id; DisplayName = displayName; Details = details; _isVisible = isVisible; _save = save; }
    public string Id { get; }
    public string DisplayName { get; }
    public string Details { get; }
    public static string FormatDetails(string type, string hardware) =>
        $"{SensorDisplayText.FriendlyType(type)} · {hardware}";
    public bool Matches(string query) =>
        DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Details.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    public void SetVisibilityWithoutSaving(bool value) => SetProperty(ref _isVisible, value, nameof(IsVisible));
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!SetProperty(ref _isVisible, value)) return;
            _ = SaveAsync(value);
        }
    }
    private async Task SaveAsync(bool value)
    {
        try { await _save(Id, value); }
        catch { SetProperty(ref _isVisible, !value, nameof(IsVisible)); }
    }
}
