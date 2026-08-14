using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.ViewModels;
public partial class SettingsViewModel(IAppSettingsService settings, MonitorApiClient api) : ObservableObject
{
    [ObservableProperty] public partial string Endpoint { get; set; } = string.Empty;
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    public event EventHandler? ChangeRequested;
    [RelayCommand] public async Task LoadAsync() => Endpoint = await settings.GetApiBaseUrlAsync() ?? "Not configured";
    [RelayCommand] private async Task TestAsync()
    {
        try { Status = $"Connected to {(await api.GetStatusAsync()).MachineName}"; }
        catch (MonitorApiException exception) { Status = exception.Message; }
    }
    [RelayCommand] private async Task ChangePcAsync()
    {
        await settings.ClearApiBaseUrlAsync();
        ChangeRequested?.Invoke(this, EventArgs.Empty);
    }
}
