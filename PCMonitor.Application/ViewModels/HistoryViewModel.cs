using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
namespace PCMonitor.Application.ViewModels;
public partial class HistoryViewModel(HistorySyncService sync, HistoryRepository repository) : ObservableObject
{
    [ObservableProperty] public partial string Summary { get; set; } = "No local history yet.";
    [RelayCommand]
    public async Task SyncAsync()
    {
        try { await sync.SyncAsync(); Summary = $"{await repository.CountAsync()} local sensor records"; }
        catch (Exception exception) { Summary = $"Offline: {exception.Message}"; }
    }
}
