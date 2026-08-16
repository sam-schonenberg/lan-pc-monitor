using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application.ViewModels;

public partial class SettingsViewModel(
    IAppSettingsService settings,
    MonitorApiClient api,
    HistoryRepository historyRepository,
    HistorySyncService historySync) : ObservableObject
{
    public ObservableCollection<SensorVisibilityOption> Sensors { get; } = [];
    [ObservableProperty] public partial string Endpoint { get; set; } = string.Empty;
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsLoadingSensors { get; set; }
    [ObservableProperty] public partial bool IsSynchronizing { get; set; }
    [ObservableProperty] public partial double SynchronizationProgress { get; set; }
    [ObservableProperty] public partial string SynchronizationStatus { get; set; } = "Ready to synchronize";
    [ObservableProperty] public partial string LastSynchronization { get; set; } = "Never synchronized";
    public event EventHandler? ChangeRequested;

    [RelayCommand]
    public async Task LoadAsync()
    {
        Endpoint = await settings.GetApiBaseUrlAsync() ?? "Not configured";
        var lastSync = await settings.GetLastHistorySyncAsync();
        LastSynchronization = lastSync is null ? "Never synchronized" : $"Last synchronized {FormatTimestamp(lastSync.Value)}";
        IsLoadingSensors = true;
        try
        {
            var hidden = await settings.GetHiddenSensorIdsAsync();
            var sensors = await historyRepository.GetSensorOptionsAsync();
            Sensors.Clear();
            foreach (var sensor in sensors)
            {
                Sensors.Add(new SensorVisibilityOption(sensor.SensorId,
                    $"{sensor.SensorType} — {sensor.Hardware} — {sensor.SensorName}",
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
        await settings.ClearApiBaseUrlAsync(); ChangeRequested?.Invoke(this, EventArgs.Empty);
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
    public SensorVisibilityOption(string id, string displayName, bool isVisible, Func<string, bool, Task> save)
    { Id = id; DisplayName = displayName; _isVisible = isVisible; _save = save; }
    public string Id { get; }
    public string DisplayName { get; }
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
