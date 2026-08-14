using PCMonitor.Application.ViewModels;
namespace PCMonitor.Application.Views;
public sealed class HistoryPage : ContentPage
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        Title = "History"; BindingContext = viewModel;
        var summary = new Label(); summary.SetBinding(Label.TextProperty, nameof(viewModel.Summary));
        var button = new Button { Text = "Sync History" }; button.SetBinding(Button.CommandProperty, nameof(viewModel.SyncCommand));
        Content = new VerticalStackLayout { Padding = 24, Spacing = 16, Children = { new Label { Text = "Historical sensor data", FontSize = 24 }, summary, button } };
    }
}
