using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Views;

public sealed class AddDashboardWidgetPage : ContentPage
{
    public AddDashboardWidgetPage(Func<DashboardWidgetType, Page> editorFactory)
    {
        Title = "Add widget";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        var content = new VerticalStackLayout { Padding = 18, Spacing = 12,
            Children = { new Label { Text = "Choose a widget", FontSize = 24, FontAttributes = FontAttributes.Bold } } };
        foreach (var descriptor in DashboardWidgetCatalog.Available)
        {
            var button = new Button { Text = descriptor.DisplayName, HorizontalOptions = LayoutOptions.Fill };
            button.Clicked += async (_, _) => await Navigation.PushAsync(editorFactory(descriptor.Type));
            var card = new Border { Padding = 14, Content = new VerticalStackLayout { Spacing = 7, Children =
            {
                button, new Label { Text = descriptor.Description, FontSize = 12, Opacity = 0.72 }
            }}};
            card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
            card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
            content.Add(card);
        }
        Content = new ScrollView { Content = content };
    }
}

public sealed class DashboardWidgetEditorPage : ContentPage
{
    private readonly DashboardWidgetDefinition _definition;
    private readonly DashboardWidgetRepository _repository;
    private readonly HistoryRepository _history;
    private readonly Func<Task> _completed;
    private readonly Entry _title = new();
    private readonly Picker _sensor = new() { Title = "Select sensor", ItemDisplayBinding = new Binding(nameof(WidgetSensorOption.DisplayName)) };
    private readonly Picker _width = new() { ItemsSource = Enum.GetValues<DashboardWidgetWidth>().ToList() };
    private readonly Switch _enabled = new();
    private readonly Picker _precision = new() { ItemsSource = Enumerable.Range(0, 5).ToList() };
    private readonly Switch _showMinMax = new();
    private readonly Picker _range = new() { ItemDisplayBinding = new Binding(nameof(WidgetRangeOption.Name)) };
    private readonly Switch _showAverage = new();
    private readonly Switch _showMinimum = new();
    private readonly Switch _showMaximum = new();
    private readonly Picker _severity = new() { ItemsSource = new[] { "All severities", "Information", "Warning", "Critical" } };
    private readonly Entry _itemLimit = new() { Keyboard = Keyboard.Numeric };
    private readonly Label _error = new() { TextColor = Colors.DarkOrange, FontSize = 12 };
    private IReadOnlyList<WidgetSensorOption> _sensors = [];

    public DashboardWidgetEditorPage(DashboardWidgetDefinition definition, bool isNew,
        DashboardWidgetRepository repository, HistoryRepository history, Func<Task> completed)
    {
        _definition = definition; _repository = repository; _history = history; _completed = completed;
        Title = isNew ? $"Add {TypeName(definition.Type)}" : $"Edit {TypeName(definition.Type)}";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        _title.Text = definition.Title;
        _width.SelectedItem = definition.Width;
        _enabled.IsToggled = definition.IsEnabled;

        var form = new VerticalStackLayout { Padding = 18, Spacing = 12,
            Children = { Heading("Title"), _title, Heading("Size"), _width, Row("Enabled", _enabled) } };
        switch (definition.Configuration)
        {
            case CurrentValueWidgetConfiguration config:
                form.Add(Heading("Sensor")); form.Add(_sensor);
                form.Add(Heading("Decimal places")); form.Add(_precision);
                form.Add(Row("Show 24-hour minimum and maximum", _showMinMax));
                _precision.SelectedItem = config.DecimalPlaces; _showMinMax.IsToggled = config.ShowMinimumAndMaximum;
                break;
            case GraphWidgetConfiguration config:
                form.Add(Heading("Sensor")); form.Add(_sensor); form.Add(Heading("Time range")); form.Add(_range);
                form.Add(Row("Show average", _showAverage)); form.Add(Row("Show minimum", _showMinimum));
                form.Add(Row("Show maximum", _showMaximum));
                _showAverage.IsToggled = config.ShowAverage; _showMinimum.IsToggled = config.ShowMinimum; _showMaximum.IsToggled = config.ShowMaximum;
                break;
            case AlertWidgetConfiguration config:
                _sensor.Title = "All sensors"; form.Add(Heading("Sensor filter")); form.Add(_sensor);
                form.Add(Heading("Minimum severity")); form.Add(_severity); form.Add(Heading("Maximum alerts")); form.Add(_itemLimit);
                _severity.SelectedIndex = SeverityIndex(config.MinimumSeverity); _itemLimit.Text = config.MaximumItems.ToString();
                break;
        }
        form.Add(_error);
        var save = new Button { Text = "Save widget" }; save.Clicked += async (_, _) => await SaveAsync();
        form.Add(save);
        Content = new ScrollView { Content = form };
        _ = LoadOptionsAsync();
    }

    private async Task LoadOptionsAsync()
    {
        var options = (await _history.GetSensorOptionsAsync())
            .OrderByDescending(x => SensorDisplayText.CommonSensorPriority(x.Hardware, x.SensorName, x.SensorType, x.Unit))
            .ThenBy(x => x.Hardware, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SensorName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new WidgetSensorOption(x.SensorId,
                SensorDisplayText.PickerLabel(x.Hardware, x.SensorName, x.SensorType, x.Unit))).ToList();
        if (_definition.Type == DashboardWidgetType.Alerts) options.Insert(0, new WidgetSensorOption(null, "All sensors"));
        _sensors = options; _sensor.ItemsSource = options;
        var configuredSensor = _definition.Configuration switch
        { CurrentValueWidgetConfiguration x => x.SensorId, GraphWidgetConfiguration x => x.SensorId, AlertWidgetConfiguration x => x.SensorId, _ => null };
        _sensor.SelectedIndex = Math.Max(0, options.FindIndex(x => x.Id == configuredSensor));
        var ranges = new[] { new WidgetRangeOption("1h", TimeSpan.FromHours(1)), new("6h", TimeSpan.FromHours(6)),
            new("24h", TimeSpan.FromHours(24)), new("7d", TimeSpan.FromDays(7)), new("30d", TimeSpan.FromDays(30)), new("1y", TimeSpan.FromDays(365)) };
        _range.ItemsSource = ranges;
        if (_definition.Configuration is GraphWidgetConfiguration graph)
            _range.SelectedIndex = Math.Max(0, Array.FindIndex(ranges, x => x.Range == graph.EffectiveRange));
    }

    private async Task SaveAsync()
    {
        try
        {
            var title = string.IsNullOrWhiteSpace(_title.Text)
                ? DashboardWidgetCatalog.Available.Single(x => x.Type == _definition.Type).DisplayName : _title.Text.Trim();
            var sensorId = _sensor.SelectedIndex >= 0 && _sensor.SelectedIndex < _sensors.Count ? _sensors[_sensor.SelectedIndex].Id : null;
            IDashboardWidgetConfiguration configuration = _definition.Type switch
            {
                DashboardWidgetType.CurrentValue => new CurrentValueWidgetConfiguration(sensorId,
                    _precision.SelectedItem is int precision ? precision : 1, _showMinMax.IsToggled),
                DashboardWidgetType.Graph => new GraphWidgetConfiguration(sensorId,
                    (_range.SelectedItem as WidgetRangeOption)?.Range ?? TimeSpan.FromHours(1),
                    _showAverage.IsToggled, _showMinimum.IsToggled, _showMaximum.IsToggled,
                    (_definition.Configuration as GraphWidgetConfiguration)?.ComparisonSensorIds),
                DashboardWidgetType.Alerts => new AlertWidgetConfiguration(sensorId,
                    _severity.SelectedIndex <= 0 ? null : _severity.SelectedItem?.ToString(),
                    int.TryParse(_itemLimit.Text, out var limit) ? limit : 5),
                _ => throw new InvalidOperationException()
            };
            if (_definition.Type is DashboardWidgetType.CurrentValue or DashboardWidgetType.Graph && sensorId is null)
                throw new ArgumentException("Select a sensor for this widget.");
            var updated = _definition with { Title = title,
                Width = _width.SelectedItem is DashboardWidgetWidth width ? width : DashboardWidgetWidth.Full,
                IsEnabled = _enabled.IsToggled, Configuration = configuration };
            DashboardWidgetConfigurationCodec.Validate(updated);
            await _repository.SaveAsync(updated); await _completed();
        }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private static Label Heading(string text) => new() { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, Opacity = 0.72 };
    private static Grid Row(string text, View control)
    {
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { new Label { Text = text, VerticalTextAlignment = TextAlignment.Center }, control } };
        grid.SetColumn(control, 1); return grid;
    }
    private static string TypeName(DashboardWidgetType type) => DashboardWidgetCatalog.Available.Single(x => x.Type == type).DisplayName;
    private static int SeverityIndex(string? severity) => severity?.ToLowerInvariant() switch
    { "information" or "info" => 1, "warning" or "warn" => 2, "critical" or "error" => 3, _ => 0 };
}

public sealed record WidgetSensorOption(string? Id, string DisplayName);
public sealed record WidgetRangeOption(string Name, TimeSpan Range);
