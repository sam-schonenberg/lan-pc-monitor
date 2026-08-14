using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.ViewModels;
namespace PCMonitor.Application.Views;
public sealed class SetupPage : ContentPage
{
    public SetupPage(SetupViewModel viewModel, IServiceProvider services)
    {
        Title = "Setup"; BindingContext = viewModel;
        var address = new Entry { Placeholder = "http://192.168.1.50:5005" };
        address.SetBinding(Entry.TextProperty, nameof(viewModel.Address));
        var status = new Label(); status.SetBinding(Label.TextProperty, nameof(viewModel.StatusMessage));
        var test = new Button { Text = "Test Connection" }; test.SetBinding(Button.CommandProperty, nameof(viewModel.TestConnectionCommand));
        var save = new Button { Text = "Save and Continue" }; save.SetBinding(Button.CommandProperty, nameof(viewModel.SaveCommand));
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 28, Spacing = 18, VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = "LAN PC Monitor", FontSize = 30, FontAttributes = FontAttributes.Bold },
                new Label { Text = "PC address" }, address, test, status, save,
                new Label { Text = "QR scanning can be added here in a future pairing update.", FontSize = 12 } }
        }};
        viewModel.Saved += (_, _) => Microsoft.Maui.Controls.Application.Current!.Windows[0].Page = services.GetRequiredService<AppShell>();
    }
}
