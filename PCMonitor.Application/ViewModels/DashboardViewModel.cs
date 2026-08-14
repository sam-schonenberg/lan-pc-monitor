using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Services.Api;
namespace PCMonitor.Application.ViewModels;
public partial class DashboardViewModel(MonitorApiClient api) : ObservableObject
{
    [ObservableProperty] public partial string PcName { get; set; } = "Configured PC";
    [ObservableProperty] public partial string ConnectionState { get; set; } = "Offline data remains available.";
    [RelayCommand]
    public async Task LoadAsync()
    {
        try { var status = await api.GetStatusAsync(); PcName = status.MachineName; ConnectionState = "Connected"; }
        catch (MonitorApiException exception) { ConnectionState = exception.Message; }
    }
}
