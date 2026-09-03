using Microsoft.Maui.Controls.Shapes;
using Microsoft.Extensions.DependencyInjection;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Views;

public sealed class AlertsPage : ContentPage
{
    private readonly AlertsViewModel _viewModel;
    private readonly IServiceProvider _services;
    public AlertsPage(AlertsViewModel viewModel, IServiceProvider services)
    {
        _services = services;
        Title = "Alerts"; BindingContext = _viewModel = viewModel;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        var list = new CollectionView
        {
            Margin = new Thickness(18, 0),
            SelectionMode = SelectionMode.None,
            Header = CreateHeader(),
            ItemTemplate = new DataTemplate(CreateAlertRow),
            EmptyView = new Label { Text = "No alerts recorded. Your monitored sensors have remained within their configured limits.",
                Margin = new Thickness(8, 24), HorizontalTextAlignment = TextAlignment.Center, Opacity = .72 }
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, nameof(viewModel.Alerts));
        var refresh = new RefreshView { Content = list };
        refresh.SetBinding(RefreshView.CommandProperty, nameof(viewModel.LoadCommand));
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(viewModel.IsRefreshing));
        Content = refresh;
    }

    protected override async void OnAppearing()
    { base.OnAppearing(); _viewModel.StartLiveUpdates(); await _viewModel.LoadAsync(); }
    protected override void OnDisappearing()
    { _viewModel.StopLiveUpdates(); base.OnDisappearing(); }

    private View CreateHeader()
    {
        var notification = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center };
        notification.SetBinding(Label.TextProperty, nameof(_viewModel.NotificationStatus));
        var update = new Label { FontSize = 12, Opacity = .7 };
        update.SetBinding(Label.TextProperty, nameof(_viewModel.Status));
        var metrics = new VerticalStackLayout { Spacing = 10 };
        BindableLayout.SetItemsSource(metrics, _viewModel.Metrics);
        BindableLayout.SetItemTemplate(metrics, new DataTemplate(CreateMetricCard));
        var noMetrics = new Label { Text = "Connect to the PC to load live alert thresholds.",
            HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(8, 18), Opacity = .7 };
        noMetrics.SetBinding(IsVisibleProperty, nameof(_viewModel.HasMetrics), converter: new InvertedBoolConverter());
        var health = new Label { FontSize = 18, FontAttributes = FontAttributes.Bold };
        health.SetBinding(Label.TextProperty, nameof(_viewModel.OverallStatus));
        health.SetBinding(Label.TextColorProperty, nameof(_viewModel.OverallStatusColor));
        var summary = new Label { FontSize = 12, Opacity = .72 };
        summary.SetBinding(Label.TextProperty, nameof(_viewModel.SummaryText));
        var healthDot = new Border { WidthRequest = 12, HeightRequest = 12, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 }, VerticalOptions = LayoutOptions.Center };
        healthDot.SetBinding(BackgroundColorProperty, nameof(_viewModel.OverallStatusColor));
        var healthText = new VerticalStackLayout { Spacing = 3, Children = { health, summary } };
        var healthGrid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 12, Children = { healthDot, healthText } };
        healthGrid.SetColumn(healthText, 1);
        var bell = new Label { Text = "●", FontSize = 15,
            VerticalTextAlignment = TextAlignment.Center };
        bell.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#007F94"), Color.FromArgb("#00D8F0"));
        var notificationGrid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 9, Children = { bell, notification } };
        notificationGrid.SetColumn(notification, 1);
        var filters = new HorizontalStackLayout { Spacing = 8,
            Children = { FilterButton("All", "all"), FilterButton("Critical", "critical"), FilterButton("Warning", "warning") } };
        var manageRules = new Button { Text = "Manage custom alert rules", HorizontalOptions = LayoutOptions.Fill };
        manageRules.Clicked += async (_, _) => await Navigation.PushAsync(
            _services.GetRequiredService<AlertRulesPage>());
        var diagnostics = CreateDiagnosticsCard();
        return new VerticalStackLayout
        {
            Spacing = 12, Padding = new Thickness(0, 18, 0, 12),
            Children =
            {
                new Label { Text = "ALERTS", FontSize = 25, FontAttributes = FontAttributes.Bold },
                Card(healthGrid),
                diagnostics,
                Card(new VerticalStackLayout { Spacing = 5, Children = { notificationGrid, update } }),
                manageRules,
                new Label { Text = "Live alert status", FontSize = 20, FontAttributes = FontAttributes.Bold,
                    Margin = new Thickness(0, 6, 0, 0) }, metrics, noMetrics,
                new Label { Text = "Recent alerts", FontSize = 20, FontAttributes = FontAttributes.Bold,
                    Margin = new Thickness(0, 10, 0, 0) }, filters
            }
        };
    }

    private View CreateDiagnosticsCard()
    {
        var dot = new Border { WidthRequest = 12, HeightRequest = 12, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 }, VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 5, 0, 0) };
        dot.SetBinding(BackgroundColorProperty, nameof(_viewModel.LatestDiagnosticColor));
        var heading = new Label { Text = "WINDOWS DIAGNOSTICS", FontSize = 11, FontAttributes = FontAttributes.Bold,
            CharacterSpacing = .6, Opacity = .7 };
        var title = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold };
        title.SetBinding(Label.TextProperty, nameof(_viewModel.LatestDiagnosticTitle));
        var summary = new Label { FontSize = 12, Opacity = .78, MaxLines = 2,
            LineBreakMode = LineBreakMode.TailTruncation };
        summary.SetBinding(Label.TextProperty, nameof(_viewModel.LatestDiagnosticSummary));
        var metadata = new Label { FontSize = 11, Opacity = .58, MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation };
        metadata.SetBinding(Label.TextProperty, nameof(_viewModel.LatestDiagnosticMetadata));
        var chevron = new Label { Text = "View history  ›", FontSize = 12, FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.End };
        chevron.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#007F94"), Color.FromArgb("#00D8F0"));
        var text = new VerticalStackLayout { Spacing = 4, Children = { heading, title, summary, metadata, chevron } };
        var content = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 12, Children = { dot, text } };
        content.SetColumn(text, 1);
        var card = Card(content);
        card.SetBinding(IsVisibleProperty, nameof(_viewModel.HasDiagnosticsCapability));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(
            _services.GetRequiredService<WindowsDiagnosticsPage>());
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private Button FilterButton(string text, string value)
    {
        var button = new Button { Text = text, FontSize = 12, Padding = new Thickness(14, 7),
            MinimumHeightRequest = 36, CornerRadius = 18, CommandParameter = value };
        button.SetBinding(Button.CommandProperty, nameof(_viewModel.SetSeverityCommand));
        button.SetBinding(Button.BackgroundColorProperty, new Binding(nameof(_viewModel.SelectedSeverity),
            converter: new SelectedFilterColorConverter(), converterParameter: value));
        button.SetBinding(Button.TextColorProperty, new Binding(nameof(_viewModel.SelectedSeverity),
            converter: new SelectedFilterTextColorConverter(), converterParameter: value));
        return button;
    }

    private static View CreateMetricCard()
    {
        var category = new Label { FontSize = 11, Opacity = .68, CharacterSpacing = .6 };
        category.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.CategoryText));
        var hardware = new Label { FontSize = 11, Opacity = .58, HorizontalOptions = LayoutOptions.End,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        hardware.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.Hardware));
        var metadata = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            Children = { category, hardware } };
        metadata.SetColumn(hardware, 1);
        var name = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation };
        name.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.SensorName));
        var state = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End };
        state.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.StateText));
        state.SetBinding(Label.TextColorProperty, nameof(AlertMetricViewModel.StateColor));
        var heading = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { name, state } };
        heading.SetColumn(state, 1);
        var value = new Label { FontSize = 25, FontAttributes = FontAttributes.Bold };
        value.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.ValueText));
        var threshold = new Label { FontSize = 12, Opacity = .72, HorizontalOptions = LayoutOptions.End };
        threshold.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.ThresholdText));
        var values = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { value, threshold } };
        values.SetColumn(threshold, 1);
        var bar = new ProgressBar { HeightRequest = 8 };
        bar.SetBinding(ProgressBar.ProgressProperty, nameof(AlertMetricViewModel.Progress));
        bar.SetBinding(ProgressBar.ProgressColorProperty, nameof(AlertMetricViewModel.StateColor));
        var headroom = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold };
        headroom.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.HeadroomText));
        var condition = new Label { FontSize = 11, Opacity = .65 };
        condition.SetBinding(Label.TextProperty, nameof(AlertMetricViewModel.Condition));
        return Card(new VerticalStackLayout { Spacing = 7, Children = { metadata, heading, values, bar, headroom, condition } });
    }

    private static View CreateAlertRow()
    {
        var severity = new Label { FontAttributes = FontAttributes.Bold, FontSize = 12 };
        severity.SetBinding(Label.TextProperty, nameof(AlertEntity.Severity));
        severity.SetBinding(Label.TextColorProperty, new Binding(nameof(AlertEntity.Severity), converter: new SeverityColorConverter()));
        var message = new Label { FontAttributes = FontAttributes.Bold };
        message.SetBinding(Label.TextProperty, nameof(AlertEntity.Message));
        var timestamp = new Label { FontSize = 12, Opacity = .68 };
        timestamp.SetBinding(Label.TextProperty, new Binding(nameof(AlertEntity.Timestamp), stringFormat: "{0:g}"));
        return Card(new VerticalStackLayout { Spacing = 4, Children = { severity, message, timestamp } }, new Thickness(0, 0, 0, 8));
    }

    private static Border Card(View content, Thickness? margin = null)
    {
        var card = new Border { Content = content, Padding = 14, Margin = margin ?? Thickness.Zero,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1 };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
        return card;
    }
}

internal sealed class SeverityColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value?.ToString()?.Equals("critical", StringComparison.OrdinalIgnoreCase) == true
            ? Color.FromArgb("#DC2626") : Color.FromArgb("#F59E0B");
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class SelectedFilterColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? FilterTheme.Color("#DCE8F2", "#10243A") : FilterTheme.Color("#EAF1F7", "#0B1A2C");
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class SelectedFilterTextColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? FilterTheme.Color("#007F94", "#00D8F0") : FilterTheme.Color("#52657A", "#AAB7C8");
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

internal static class FilterTheme
{
    public static Color Color(string light, string dark) =>
        Microsoft.Maui.Graphics.Color.FromArgb(
            Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark ? dark : light);
}
