using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Views;

public sealed class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    public SettingsPage(SettingsViewModel viewModel, IServiceProvider services)
    {
        Title = "Settings"; BindingContext = _viewModel = viewModel;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F4F7FB"), Color.FromArgb("#141414"));

        var sensors = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            Margin = new Thickness(18, 0),
            Header = CreateHeader(),
            EmptyView = new Label { Text = "No local sensor catalog is available yet.", Margin = new Thickness(12, 28),
                HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.7 },
            ItemTemplate = new DataTemplate(CreateSensorRow)
        };
        sensors.SetBinding(ItemsView.ItemsSourceProperty, nameof(viewModel.Sensors));
        Content = sensors;
        viewModel.ChangeRequested += (_, _) => Microsoft.Maui.Controls.Application.Current!.Windows[0].Page =
            services.GetRequiredService<SetupPage>();
    }

    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }

    private View CreateHeader()
    {
        var endpoint = new Label { FontAttributes = FontAttributes.Bold }; endpoint.SetBinding(Label.TextProperty, nameof(_viewModel.Endpoint));
        var status = new Label { FontSize = 12, Opacity = 0.75 }; status.SetBinding(Label.TextProperty, nameof(_viewModel.Status));
        var test = new Button { Text = "Test connection" }; test.SetBinding(Button.CommandProperty, nameof(_viewModel.TestCommand));
        var change = new Button { Text = "Change PC" }; change.SetBinding(Button.CommandProperty, nameof(_viewModel.ChangePcCommand));
        var buttons = new HorizontalStackLayout { Spacing = 10, Children = { test, change } };
        var lastSync = new Label { FontAttributes = FontAttributes.Bold };
        lastSync.SetBinding(Label.TextProperty, nameof(_viewModel.LastSynchronization));
        var syncStatus = new Label { FontSize = 12, Opacity = 0.75, LineBreakMode = LineBreakMode.WordWrap };
        syncStatus.SetBinding(Label.TextProperty, nameof(_viewModel.SynchronizationStatus));
        var syncProgress = new ProgressBar { HeightRequest = 6, ProgressColor = Color.FromArgb("#512BD4") };
        syncProgress.SetBinding(ProgressBar.ProgressProperty, nameof(_viewModel.SynchronizationProgress));
        var synchronize = new Button { Text = "Synchronize now" };
        synchronize.SetBinding(Button.CommandProperty, nameof(_viewModel.SynchronizeCommand));
        synchronize.SetBinding(IsEnabledProperty, nameof(_viewModel.IsSynchronizing), converter: new InvertedBoolConverter());
        var loading = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Start };
        loading.SetBinding(IsVisibleProperty, nameof(_viewModel.IsLoadingSensors));
        var notificationStatus = new Label { FontSize = 12, Opacity = 0.75, LineBreakMode = LineBreakMode.WordWrap };
        notificationStatus.SetBinding(Label.TextProperty, nameof(_viewModel.NotificationStatus));
        var notificationButton = new Button();
        notificationButton.SetBinding(Button.TextProperty, nameof(_viewModel.NotificationButtonText));
        notificationButton.SetBinding(Button.CommandProperty, nameof(_viewModel.ToggleNotificationsCommand));
        notificationButton.SetBinding(IsEnabledProperty, nameof(_viewModel.IsChangingNotifications),
            converter: new InvertedBoolConverter());
        return new VerticalStackLayout
        {
            Padding = new Thickness(0, 18, 0, 12), Spacing = 12,
            Children =
            {
                new Label { Text = "SETTINGS", FontSize = 25, FontAttributes = FontAttributes.Bold },
                Card(new VerticalStackLayout { Spacing = 8, Children =
                {
                    new Label { Text = "Configured PC endpoint", FontSize = 13, Opacity = 0.7 }, endpoint, buttons, status
                }}),
                Card(new VerticalStackLayout { Spacing = 9, Children =
                {
                    new Label { Text = "History synchronization", FontSize = 18, FontAttributes = FontAttributes.Bold },
                    lastSync, syncStatus, syncProgress, synchronize,
                    new Label { Text = "The latest history is also synchronized automatically whenever the app becomes active.",
                        FontSize = 12, Opacity = 0.72, LineBreakMode = LineBreakMode.WordWrap }
                }}),
                Card(new VerticalStackLayout { Spacing = 9, Children =
                {
                    new Label { Text = "Critical notifications", FontSize = 18, FontAttributes = FontAttributes.Bold },
                    notificationStatus, notificationButton,
                    new Label { Text = "The app registers this phone with your PC. Sensor monitoring continues even when the app is closed.",
                        FontSize = 12, Opacity = 0.72, LineBreakMode = LineBreakMode.WordWrap }
                }}),
                new Label { Text = "Visible sensors", FontSize = 20, FontAttributes = FontAttributes.Bold,
                    Margin = new Thickness(0, 8, 0, 0) },
                new Label { Text = "Hidden sensors are removed from app pickers only. All sensors continue to be recorded and synchronized.",
                    FontSize = 12, Opacity = 0.72, LineBreakMode = LineBreakMode.WordWrap }, loading
            }
        };
    }

    private static View CreateSensorRow()
    {
        var name = new Label { FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1 };
        name.SetBinding(Label.TextProperty, nameof(SensorVisibilityOption.DisplayName));
        var details = new Label { FontSize = 12, Opacity = 0.7, LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1 };
        details.SetBinding(Label.TextProperty, nameof(SensorVisibilityOption.Details));
        var text = new VerticalStackLayout { Spacing = 2, Children = { name, details } };
        var toggle = new Switch { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        toggle.SetBinding(Switch.IsToggledProperty, nameof(SensorVisibilityOption.IsVisible), mode: BindingMode.TwoWay);
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12,
            Children = { text, toggle } };
        grid.SetColumn(toggle, 1);
        return Card(grid, new Thickness(0, 0, 0, 8));
    }

    private static Border Card(View content, Thickness? margin = null)
    {
        var card = new Border { Content = content, Padding = 14, Margin = margin ?? Thickness.Zero,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1 };
        card.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#212121"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8DEE9"), Color.FromArgb("#404040"));
        return card;
    }
}

internal sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
