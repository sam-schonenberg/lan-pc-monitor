using PCMonitor.Application.ViewModels;
namespace PCMonitor.Application.Views;
public sealed class AlertsPage : ContentPage
{
    private readonly AlertsViewModel _viewModel;
    public AlertsPage(AlertsViewModel viewModel)
    {
        Title = "Alerts"; BindingContext = _viewModel = viewModel;
        var list = new CollectionView { ItemsSource = viewModel.Alerts, ItemTemplate = new DataTemplate(() =>
        {
            var severity = new Label { FontAttributes = FontAttributes.Bold }; severity.SetBinding(Label.TextProperty, "Severity");
            var message = new Label(); message.SetBinding(Label.TextProperty, "Message");
            var timestamp = new Label { FontSize = 12 }; timestamp.SetBinding(Label.TextProperty, new Binding("Timestamp", stringFormat: "{0:g}"));
            return new Border { Padding = 12, Margin = 4, Content = new VerticalStackLayout { Children = { severity, message, timestamp } } };
        })};
        Content = list;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }
}
