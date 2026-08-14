using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
namespace PCMonitor.Application.ViewModels;
public partial class AlertsViewModel(AlertSyncService sync, AlertRepository repository) : ObservableObject
{
    public ObservableCollection<AlertEntity> Alerts { get; } = [];
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [RelayCommand]
    public async Task LoadAsync()
    {
        try { await sync.SyncAsync(); } catch (Exception exception) { Status = $"Offline: {exception.Message}"; }
        Alerts.Clear();
        foreach (var alert in await repository.GetAllAsync()) Alerts.Add(alert);
    }
}
