using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Controls;

public sealed class DashboardWidgetHost : ContentView
{
    public DashboardWidgetHost(DashboardWidgetViewModelBase widget,
        Func<DashboardWidgetViewModelBase, Task> edit,
        Func<DashboardWidgetViewModelBase, int, Task> move,
        Func<DashboardWidgetViewModelBase, Task> toggle,
        Func<DashboardWidgetViewModelBase, Task> delete)
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
            GraphWidgetViewModel graphWidget => new GraphWidgetView(graphWidget),
            AlertsWidgetViewModel alerts => new AlertsWidgetView(alerts),
            _ => new Label { Text = "Unsupported widget" }
        };
        var body = new VerticalStackLayout { Spacing = 10, Children = { header, content } };
        if (widget.IsEditMode) body.Add(EditBar(widget, edit, move, toggle, delete));

        var card = new Border { Content = body, Padding = 14, StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = widget.IsEditMode ? 2 : 1, Opacity = widget.IsEnabled ? 1 : 0.55 };
        card.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#212121"));
        card.SetAppThemeColor(Border.StrokeProperty, widget.IsEditMode ? Color.FromArgb("#8067D8") : Color.FromArgb("#D8DEE9"),
            widget.IsEditMode ? Color.FromArgb("#8067D8") : Color.FromArgb("#404040"));
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
    public GraphWidgetView(GraphWidgetViewModel viewModel)
    {
        BindingContext = viewModel;
        var chart = new SensorChart { IsCompact = true, ShowAverage = viewModel.ShowAverage,
            ShowMinimum = viewModel.ShowMinimum, ShowMaximum = viewModel.ShowMaximum, RangeDuration = viewModel.Range };
        chart.SetBinding(SensorChart.PointsProperty, nameof(viewModel.Points));
        chart.SetBinding(SensorChart.UnitProperty, nameof(viewModel.Unit));
        chart.SetBinding(SensorChart.RangeEndProperty, nameof(viewModel.RangeEnd));
        chart.SetBinding(SensorChart.IsLoadingProperty, nameof(viewModel.IsLoading));
        var current = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold };
        current.SetBinding(Label.TextProperty, nameof(viewModel.CurrentValue), stringFormat: "Current {0}");
        var stale = new Label { FontSize = 11, Opacity = 0.65, HorizontalOptions = LayoutOptions.End };
        stale.SetBinding(Label.TextProperty, nameof(viewModel.Freshness));
        var footer = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Children = { current, stale } };
        footer.SetColumn(stale, 1);
        Content = new VerticalStackLayout { Spacing = 7, Children = { chart, footer } };
    }
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
