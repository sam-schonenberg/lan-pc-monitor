using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.Services.Notifications;

namespace PCMonitor.Application.ViewModels;

public partial class SettingsViewModel(
    IAppSettingsService settings,
    MonitorApiClient api,
    HistoryRepository historyRepository,
    HistorySyncService historySync,
    NotificationRegistrationService notifications) : ObservableObject
{
    public ObservableCollection<SensorVisibilityOption> Sensors { get; } = [];
    [ObservableProperty] public partial string Endpoint { get; set; } = string.Empty;
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoadingSensors { get; set; }
    [ObservableProperty] public partial bool IsSynchronizing { get; set; }
    [ObservableProperty] public partial double SynchronizationProgress { get; set; }
    [ObservableProperty] public partial string SynchronizationStatus { get; set; } = "Ready to synchronize";
    [ObservableProperty] public partial string LastSynchronization { get; set; } = "Never synchronized";
    [ObservableProperty] public partial bool NotificationsEnabled { get; set; }
    [ObservableProperty] public partial bool IsChangingNotifications { get; set; }
    [ObservableProperty] public partial string NotificationStatus { get; set; } = "Checking notification support…";
    public string NotificationButtonText => NotificationsEnabled ? "Disable notifications" : "Enable notifications";
    public event EventHandler? ChangeRequested;

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
        IsLoadingSensors = true;
        try
        {
            var hidden = await settings.GetHiddenSensorIdsAsync();
            var sensors = await historyRepository.GetSensorOptionsAsync();
            Sensors.Clear();
            foreach (var sensor in sensors)
            {
                Sensors.Add(new SensorVisibilityOption(sensor.SensorId, sensor.SensorName,
                    SensorVisibilityOption.FormatDetails(sensor.SensorType, sensor.Hardware),
                    !hidden.Contains(sensor.SensorId), SetVisibilityAsync));
            }
        }
        finally { IsLoadingSensors = false; }
    }

    private async Task SetVisibilityAsync(string sensorId, bool visible)
    {
        await settings.SetSensorHiddenAsync(sensorId, !visible);
        Status = visible ? "Sensor shown in app views. Recording remains enabled."
            : "Sensor hidden from app views. Recording remains enabled.";
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
