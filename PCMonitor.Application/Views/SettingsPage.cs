using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.ViewModels;
namespace PCMonitor.Application.Views;
public sealed class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    public SettingsPage(SettingsViewModel viewModel, IServiceProvider services)
    {
        Title = "Settings"; BindingContext = _viewModel = viewModel;
        var endpoint = new Label(); endpoint.SetBinding(Label.TextProperty, nameof(viewModel.Endpoint));
        var status = new Label(); status.SetBinding(Label.TextProperty, nameof(viewModel.Status));
        var test = new Button { Text = "Test Connection" }; test.SetBinding(Button.CommandProperty, nameof(viewModel.TestCommand));
        var change = new Button { Text = "Change PC" }; change.SetBinding(Button.CommandProperty, nameof(viewModel.ChangePcCommand));
        Content = new VerticalStackLayout { Padding = 24, Spacing = 16, Children = { new Label { Text = "Configured PC endpoint", FontSize = 22 }, endpoint, test, status, change } };
        viewModel.ChangeRequested += (_, _) => Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = services.GetRequiredService<SetupPage>();
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }
}
