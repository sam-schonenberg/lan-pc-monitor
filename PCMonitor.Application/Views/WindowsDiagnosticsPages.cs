using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Views;

public sealed class WindowsDiagnosticsPage : ContentPage
{
    private readonly WindowsDiagnosticsViewModel _viewModel;

    public WindowsDiagnosticsPage(WindowsDiagnosticsViewModel viewModel)
    {
        Title = "Windows diagnostics"; BindingContext = _viewModel = viewModel;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        var list = new CollectionView
        {
            Margin = new Thickness(18, 0), SelectionMode = SelectionMode.Single,
            RemainingItemsThreshold = 8, Header = CreateHeader(), ItemTemplate = new DataTemplate(CreateEventCard),
            EmptyView = new Label { Text = "No Windows errors are currently retained.", Margin = new Thickness(24, 40),
                HorizontalTextAlignment = TextAlignment.Center, Opacity = .7 },
            Footer = CreateFooter()
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, nameof(viewModel.Events));
        list.SetBinding(ItemsView.RemainingItemsThresholdReachedCommandProperty, nameof(viewModel.LoadMoreCommand));
        list.SelectionChanged += async (_, args) =>
        {
            if (args.CurrentSelection.FirstOrDefault() is not WindowsDiagnosticEventItem selected) return;
            list.SelectedItem = null;
            await Navigation.PushAsync(new WindowsDiagnosticDetailPage(selected.Event));
        };
        var refresh = new RefreshView { Content = list };
        refresh.SetBinding(RefreshView.CommandProperty, nameof(viewModel.RefreshCommand));
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(viewModel.IsRefreshing));
        Content = refresh;
    }

    protected override void OnAppearing()
    { base.OnAppearing(); _viewModel.InitializeCommand.Execute(null); }

    private View CreateHeader()
    {
        var all = FilterButton("All errors", "all");
        var critical = FilterButton("Critical", "critical");
        var status = new Label { FontSize = 12, Opacity = .68, Margin = new Thickness(0, 2, 0, 4) };
        status.SetBinding(Label.TextProperty, nameof(_viewModel.Status));
        return new VerticalStackLayout { Padding = new Thickness(0, 18, 0, 12), Spacing = 12, Children =
        {
            new Label { Text = "WINDOWS DIAGNOSTICS", FontSize = 25, FontAttributes = FontAttributes.Bold },
            new Label { Text = "Critical and Error events reported by Windows. Details remain on your PC.",
                FontSize = 13, Opacity = .72 },
            new HorizontalStackLayout { Spacing = 8, Children = { all, critical } }, status
        } };
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

    private static View CreateEventCard()
    {
        var dot = new Border { WidthRequest = 10, HeightRequest = 10, StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 5 }, Margin = new Thickness(0, 5, 0, 0),
            VerticalOptions = LayoutOptions.Start };
        dot.SetBinding(BackgroundColorProperty, nameof(WindowsDiagnosticEventItem.SeverityColor));
        var severity = BoundLabel(nameof(WindowsDiagnosticEventItem.Severity), 11, FontAttributes.Bold);
        severity.SetBinding(Label.TextColorProperty, nameof(WindowsDiagnosticEventItem.SeverityColor));
        var timestamp = BoundLabel(nameof(WindowsDiagnosticEventItem.DisplayTimestamp), 11);
        timestamp.Opacity = .6; timestamp.HorizontalOptions = LayoutOptions.End;
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { severity, timestamp } };
        top.SetColumn(timestamp, 1);
        var title = BoundLabel(nameof(WindowsDiagnosticEventItem.Title), 17, FontAttributes.Bold);
        var summary = BoundLabel(nameof(WindowsDiagnosticEventItem.Summary), 12); summary.Opacity = .76;
        summary.MaxLines = 2; summary.LineBreakMode = LineBreakMode.TailTruncation;
        var metadata = BoundLabel(nameof(WindowsDiagnosticEventItem.Metadata), 11); metadata.Opacity = .56;
        var content = new VerticalStackLayout { Spacing = 4, Children = { top, title, summary, metadata } };
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 11, Children = { dot, content } };
        grid.SetColumn(content, 1);
        return Card(grid, new Thickness(0, 0, 0, 9));
    }

    private View CreateFooter()
    {
        var indicator = new ActivityIndicator { IsRunning = true, HeightRequest = 48 };
        indicator.SetBinding(IsVisibleProperty, nameof(_viewModel.IsLoadingMore));
        return indicator;
    }

    private static Label BoundLabel(string property, double size, FontAttributes attributes = FontAttributes.None)
    { var label = new Label { FontSize = size, FontAttributes = attributes }; label.SetBinding(Label.TextProperty, property); return label; }

    private static Border Card(View content, Thickness margin) {
        var card = new Border { Content = content, Padding = 14, Margin = margin,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1 };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
        return card;
    }
}

public sealed class WindowsDiagnosticDetailPage : ContentPage
{
    public WindowsDiagnosticDetailPage(WindowsDiagnosticEventDto item)
    {
        Title = item.Title;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        var severity = new Label { Text = item.Severity.ToUpperInvariant(), FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = item.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb("#DC2626") : Color.FromArgb("#F59E0B") };
        var content = new VerticalStackLayout { Padding = 18, Spacing = 14, Children =
        {
            severity,
            new Label { Text = item.Title, FontSize = 25, FontAttributes = FontAttributes.Bold },
            new Label { Text = item.Timestamp.ToLocalTime().ToString("f"), FontSize = 12, Opacity = .65 },
            Section("What happened", new Label { Text = item.Summary, FontSize = 15, LineHeight = 1.25 }),
            Section("Technical details", new VerticalStackLayout { Spacing = 9, Children =
            {
                Detail("Provider", item.Provider), Detail("Event ID", item.EventId.ToString()),
                Detail("Category", FriendlyCategory(item.Category)), Detail("Windows level", WindowsLevel(item.WindowsLevel)),
                Detail("Channel", item.Channel), Detail("Record ID", item.RecordId.ToString()),
                Detail("Event schema version", item.Version.ToString()), Detail("Occurrences", item.OccurrenceCount.ToString())
            } })
        } };
        Content = new ScrollView { Content = content };
    }

    private static View Section(string heading, View content)
    {
        var stack = new VerticalStackLayout { Spacing = 10, Children =
        { new Label { Text = heading, FontSize = 18, FontAttributes = FontAttributes.Bold }, content } };
        var card = new Border { Content = stack, Padding = 16, StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1 };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
        return card;
    }

    private static View Detail(string name, string value) => new Grid
    {
        ColumnDefinitions = { new(new GridLength(135)), new(GridLength.Star) }, Children =
        {
            new Label { Text = name, FontSize = 12, Opacity = .62 },
            new Label { Text = value, FontSize = 12, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.WordWrap }
        }
    }.Also(grid => grid.SetColumn(grid.Children[1], 1));

    private static string FriendlyCategory(string value) =>
        string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(word =>
            char.ToUpperInvariant(word[0]) + word[1..]));
    private static string WindowsLevel(byte value) => value switch
    { 1 => "Critical (1)", 2 => "Error (2)", 3 => "Warning (3)", 4 => "Information (4)", _ => value.ToString() };
}

internal static class ViewBuilderExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
