using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using PCMonitor.Application.Models;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Controls;

public sealed class DashboardWidgetHost : ContentView
{
    public DashboardWidgetHost(DashboardWidgetViewModelBase widget,
        Func<DashboardWidgetViewModelBase, Task> edit,
        Func<DashboardWidgetViewModelBase, int, Task> move,
        Func<DashboardWidgetViewModelBase, Task> toggle,
        Func<DashboardWidgetViewModelBase, Task> delete,
        Func<GraphWidgetViewModel, Task> addComparison,
        Func<GraphWidgetViewModel, string, Task> removeComparison,
        Func<GraphWidgetViewModel, Task> exportGraph)
    {
        BindingContext = widget;
        var title = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1, VerticalTextAlignment = TextAlignment.Center };
        title.SetBinding(Label.TextProperty, nameof(widget.Title));
        var header = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        header.Add(title);
        if (widget is GraphWidgetViewModel graph)
        {
            var range = new Label { Text = graph.RangeLabel, FontAttributes = FontAttributes.Bold, Opacity = 0.75,
                VerticalTextAlignment = TextAlignment.Center };
            header.Add(range, 1);
        }

        View content = widget switch
        {
            CurrentValueWidgetViewModel value => new CurrentValueWidgetView(value),
            GraphWidgetViewModel graphWidget => new GraphWidgetView(graphWidget, addComparison, removeComparison, exportGraph),
            AlertsWidgetViewModel alerts => new AlertsWidgetView(alerts),
            _ => new Label { Text = "Unsupported widget" }
        };
        var body = new VerticalStackLayout { Spacing = 10, Children = { header, content } };
        if (widget.IsEditMode) body.Add(EditBar(widget, edit, move, toggle, delete));

        var card = new Border { Content = body, Padding = 14, StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = widget.IsEditMode ? 2 : 1, Opacity = widget.IsEnabled ? 1 : 0.55 };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        card.SetAppThemeColor(Border.StrokeProperty,
            widget.IsEditMode ? Color.FromArgb("#007F94") : Color.FromArgb("#C4D2DF"),
            widget.IsEditMode ? Color.FromArgb("#00D8F0") : Color.FromArgb("#1D3248"));
        Content = card;
    }

    private static View EditBar(DashboardWidgetViewModelBase widget,
        Func<DashboardWidgetViewModelBase, Task> edit, Func<DashboardWidgetViewModelBase, int, Task> move,
        Func<DashboardWidgetViewModelBase, Task> toggle, Func<DashboardWidgetViewModelBase, Task> delete)
    {
        var row = new HorizontalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End };
        row.Add(Action("Edit", () => edit(widget)));
        row.Add(Action("↑", () => move(widget, -1), "Move widget up"));
        row.Add(Action("↓", () => move(widget, 1), "Move widget down"));
        row.Add(Action(widget.IsEnabled ? "Disable" : "Enable", () => toggle(widget)));
        row.Add(Action("Delete", () => delete(widget)));
        return row;
    }

    private static Button Action(string text, Func<Task> action, string? description = null)
    {
        var button = new Button { Text = text, FontSize = 11, Padding = new Thickness(8, 5), MinimumHeightRequest = 40 };
        SemanticProperties.SetDescription(button, description ?? text);
        button.Clicked += async (_, _) => await action();
        return button;
    }
}

public sealed class CurrentValueWidgetView : ContentView
{
    public CurrentValueWidgetView(CurrentValueWidgetViewModel viewModel)
    {
        BindingContext = viewModel;
        var value = new Label { FontSize = 30, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6) };
        value.SetBinding(Label.TextProperty, nameof(viewModel.Value));
        var freshness = new Label { FontSize = 11, Opacity = 0.7, HorizontalTextAlignment = TextAlignment.Center };
        freshness.SetBinding(Label.TextProperty, nameof(viewModel.Freshness));
        var min = new Label { FontSize = 11 }; min.SetBinding(Label.TextProperty, nameof(viewModel.Minimum), stringFormat: "Min {0}");
        var max = new Label { FontSize = 11, HorizontalTextAlignment = TextAlignment.End };
        max.SetBinding(Label.TextProperty, nameof(viewModel.Maximum), stringFormat: "Max {0}");
        var extrema = new Grid { IsVisible = viewModel.ShowMinimumAndMaximum,
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, Children = { min, max } };
        extrema.SetColumn(max, 1);
        Content = new VerticalStackLayout { Spacing = 5, Children = { value, freshness, extrema } };
    }
}

public sealed class GraphWidgetView : ContentView
{
    public GraphWidgetView(GraphWidgetViewModel viewModel, Func<GraphWidgetViewModel, Task> addComparison,
        Func<GraphWidgetViewModel, string, Task> removeComparison, Func<GraphWidgetViewModel, Task> exportGraph)
    {
        BindingContext = viewModel;
        var chart = new SensorChart { IsCompact = true, ShowAverage = viewModel.ShowAverage,
            ShowMinimum = viewModel.ShowMinimum, ShowMaximum = viewModel.ShowMaximum, RangeDuration = viewModel.Range };
        chart.SetBinding(SensorChart.PointsProperty, nameof(viewModel.Points));
        chart.SetBinding(SensorChart.ComparisonSeriesProperty, nameof(viewModel.ComparisonSeries));
        chart.SetBinding(SensorChart.UnitProperty, nameof(viewModel.Unit));
        chart.SetBinding(SensorChart.RangeEndProperty, nameof(viewModel.RangeEnd));
        chart.SetBinding(SensorChart.IsLoadingProperty, nameof(viewModel.IsLoading));
        var current = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold };
        current.SetBinding(Label.TextProperty, nameof(viewModel.CurrentValue), stringFormat: "Current {0}");
        var stale = new Label { FontSize = 11, Opacity = 0.65, HorizontalOptions = LayoutOptions.End };
        stale.SetBinding(Label.TextProperty, nameof(viewModel.Freshness));
        var footer = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { current, stale } };
        footer.SetColumn(stale, 1);
        var add = new Button { Text = "+", FontSize = 17, Padding = 0, WidthRequest = 34, HeightRequest = 32,
            MinimumHeightRequest = 32, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(add, "Add compatible sensor comparison");
        add.Clicked += async (_, _) => await addComparison(viewModel);
        var export = new Button { Text = "⇩", FontSize = 17, Padding = 0, WidthRequest = 34, HeightRequest = 32,
            MinimumHeightRequest = 32, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(export, $"Save {viewModel.Title} graph as an image");
        export.Clicked += async (_, _) =>
        {
            export.IsEnabled = false;
            try { await exportGraph(viewModel); }
            finally { export.IsEnabled = true; }
        };
        var comparisons = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            JustifyContent = FlexJustify.Start
        };
        comparisons.SetBinding(BindableLayout.ItemsSourceProperty, nameof(viewModel.ComparisonSeries));
        BindableLayout.SetItemTemplate(comparisons, new DataTemplate(() =>
        {
            var label = new Label { FontSize = 11, VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
            label.SetBinding(Label.TextProperty, new Binding(nameof(SensorGraphSeries.Name),
                converter: DashboardComparisonLabelConverter.Instance));
            var removeGlyph = new Label
            {
                Text = "×", FontSize = 11, Opacity = 0.65,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            var remove = new Border
            {
                Content = removeGlyph, WidthRequest = 22, HeightRequest = 22, Padding = 0,
                StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 11 },
                HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center
            };
            remove.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#DCE8F2"), Color.FromArgb("#10243A"));
            removeGlyph.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#007F94"), Color.FromArgb("#00D8F0"));
            SemanticProperties.SetDescription(remove, "Remove sensor comparison");
            var removeTap = new TapGestureRecognizer();
            removeTap.Tapped += async (sender, _) =>
            {
                if ((sender as BindableObject)?.BindingContext is SensorGraphSeries item && item.SensorId != viewModel.SensorId)
                    await removeComparison(viewModel, item.SensorId);
            };
            remove.GestureRecognizers.Add(removeTap);
            var contents = new HorizontalStackLayout
            {
                Spacing = 4, HorizontalOptions = LayoutOptions.Start, Children = { label, remove }
            };
            var chip = new Border
            {
                Content = contents,
                Padding = new Thickness(9, 4, 4, 4),
                Margin = new Thickness(0, 0, 6, 6),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                MaximumWidthRequest = 190
            };
            chip.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#DCE8F2"), Color.FromArgb("#10243A"));
            chip.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
            chip.BindingContextChanged += (_, _) =>
            {
                if (chip.BindingContext is SensorGraphSeries item)
                    remove.IsVisible = item.SensorId != viewModel.SensorId;
            };
            return chip;
        }));
        var selector = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 8,
            Children = { add, comparisons, export }
        };
        selector.SetColumn(comparisons, 1);
        selector.SetColumn(export, 2);
        Content = new VerticalStackLayout { Spacing = 7, Children = { selector, chart, footer } };
    }
}

internal sealed class DashboardComparisonLabelConverter : IValueConverter
{
    public static DashboardComparisonLabelConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var label = value?.ToString()?.Trim() ?? string.Empty;
        return label.EndsWith(" Temperature", StringComparison.OrdinalIgnoreCase)
            ? label[..^" Temperature".Length]
            : label;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class AlertsWidgetView : ContentView
{
    public AlertsWidgetView(AlertsWidgetViewModel viewModel)
    {
        BindingContext = viewModel;
        var list = new VerticalStackLayout { Spacing = 9 };
        BindableLayout.SetItemsSource(list, viewModel.Items);
        BindableLayout.SetItemTemplate(list, new DataTemplate(() =>
        {
            var sensor = new Label { FontAttributes = FontAttributes.Bold, FontSize = 13 };
            sensor.SetBinding(Label.TextProperty, nameof(DashboardAlertItem.Sensor));
            var value = new Label { FontSize = 12 }; value.SetBinding(Label.TextProperty, nameof(DashboardAlertItem.Value));
            var severity = new Label { FontSize = 11, Opacity = 0.75 }; severity.SetBinding(Label.TextProperty, nameof(DashboardAlertItem.Severity));
            var time = new Label { FontSize = 11, Opacity = 0.65, HorizontalTextAlignment = TextAlignment.End };
            time.SetBinding(Label.TextProperty, nameof(DashboardAlertItem.Timestamp));
            var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
                Children = { new VerticalStackLayout { Children = { sensor, new HorizontalStackLayout { Spacing = 6, Children = { value, severity } } } }, time } };
            grid.SetColumn(time, 1); return grid;
        }));
        var empty = new Label { Text = viewModel.EmptyMessage, Opacity = 0.7, HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 12), IsVisible = viewModel.Items.Count == 0 };
        viewModel.Items.CollectionChanged += (_, _) => empty.IsVisible = viewModel.Items.Count == 0;
        Content = new VerticalStackLayout { Children = { list, empty } };
    }
}
