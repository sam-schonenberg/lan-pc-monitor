using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
namespace PCMonitor.Application.ViewModels;
public partial class SetupViewModel(MonitorApiClient api, IAppSettingsService settings) : ObservableObject
{
    [ObservableProperty] public partial string Address { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Enter the private LAN address shown by PCMonitor.";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] public partial bool ConnectionVerified { get; set; }
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))] public partial bool IsBusy { get; set; }
    private string? _verifiedUrl;
    public event EventHandler? Saved;

    [RelayCommand]
    private Task TestConnectionAsync() => TestAddressAsync();

    public async Task ApplyScannedAddressAsync(string value)
    {
        Address = value;
        await TestAddressAsync();
    }

    private async Task TestAddressAsync()
    {
        IsBusy = true; ConnectionVerified = false; StatusMessage = "Connecting…";
        try
        {
            var uri = MonitorApiClient.NormalizeBaseUri(Address);
            var status = await api.TestStatusAsync(uri.AbsoluteUri);
            _verifiedUrl = uri.AbsoluteUri.TrimEnd('/');
            ConnectionVerified = true;
            StatusMessage = $"Connected to {status.MachineName}";
        }
        catch (MonitorApiException exception) { StatusMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    private bool CanSave() => ConnectionVerified && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_verifiedUrl is null) return;
        await settings.SetApiBaseUrlAsync(_verifiedUrl);
        Saved?.Invoke(this, EventArgs.Empty);
    }
}
