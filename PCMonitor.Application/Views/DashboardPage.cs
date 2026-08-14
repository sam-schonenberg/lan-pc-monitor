using PCMonitor.Application.ViewModels;
namespace PCMonitor.Application.Views;
public sealed class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    public DashboardPage(DashboardViewModel viewModel)
    {
        Title = "Dashboard"; BindingContext = _viewModel = viewModel;
        var name = new Label { FontSize = 24, FontAttributes = FontAttributes.Bold }; name.SetBinding(Label.TextProperty, nameof(viewModel.PcName));
        var state = new Label(); state.SetBinding(Label.TextProperty, nameof(viewModel.ConnectionState));
        Content = new VerticalStackLayout { Padding = 24, Spacing = 16, Children = { name, state, new Label { Text = "Dashboard widgets will be configurable here." } } };
    }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }
}
