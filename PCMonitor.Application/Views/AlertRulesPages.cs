using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Models;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;

namespace PCMonitor.Application.Views;

public sealed class AlertRulesPage : ContentPage
{
    private readonly MonitorApiClient _api;
    private readonly HistoryRepository _history;
    private readonly VerticalStackLayout _rules = new() { Spacing = 10 };
    private readonly Label _status = new() { FontSize = 12, Opacity = .72 };

    public AlertRulesPage(MonitorApiClient api, HistoryRepository history)
    {
        _api = api; _history = history; Title = "Custom alert rules";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        var add = new Button { Text = "+ Add alert rule" };
        add.Clicked += async (_, _) => await OpenEditorAsync(null);
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = 18, Spacing = 12, Children =
            {
                new Label { Text = "CUSTOM ALERT RULES", FontSize = 24, FontAttributes = FontAttributes.Bold },
                new Label { Text = "Choose any sensor and define when it should create an alert. Rules run on the PC even when the phone app is closed.",
                    FontSize = 13, Opacity = .76 }, add, _status, _rules
            }
        }};
    }

    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }

    private async Task LoadAsync()
    {
        try
        {
            var response = await _api.GetAlertRulesAsync();
            var sensors = (await _history.GetSensorOptionsAsync()).ToDictionary(x => x.SensorId, StringComparer.Ordinal);
            _rules.Clear();
            foreach (var rule in response.Rules.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                _rules.Add(RuleCard(rule, sensors.GetValueOrDefault(rule.SensorId)?.SensorName));
            _status.Text = response.Rules.Count == 0 ? "No custom rules yet. Built-in safety alerts remain active."
                : $"{response.Rules.Count} custom {(response.Rules.Count == 1 ? "rule" : "rules")}";
        }
        catch (Exception exception) { _status.Text = $"Could not load rules: {exception.Message}"; }
    }

    private View RuleCard(CustomAlertRuleDto rule, string? sensorName)
    {
        var title = new Label { Text = rule.Name, FontSize = 17, FontAttributes = FontAttributes.Bold };
        var comparison = rule.Direction.Equals("above", StringComparison.OrdinalIgnoreCase) ? "above" : "below";
        var details = new Label
        {
            Text = $"{sensorName ?? rule.SensorId} · {comparison} {rule.Threshold:0.##} · {rule.Severity}",
            FontSize = 12, Opacity = .72
        };
        var state = new Label { Text = rule.Enabled ? "Enabled" : "Disabled", FontSize = 12,
            TextColor = rule.Enabled ? Color.FromArgb("#16A34A") : Color.FromArgb("#66768A") };
        var edit = new Button { Text = "Edit", FontSize = 12 };
        edit.Clicked += async (_, _) => await OpenEditorAsync(rule);
        var delete = new Button { Text = "Delete", FontSize = 12, BackgroundColor = Color.FromArgb("#B91C1C") };
        delete.Clicked += async (_, _) =>
        {
            if (!await DisplayAlertAsync("Delete alert rule?", $"Delete “{rule.Name}”?", "Delete", "Cancel")) return;
            try { await _api.DeleteAlertRuleAsync(rule.Id); await LoadAsync(); }
            catch (Exception exception) { _status.Text = $"Could not delete rule: {exception.Message}"; }
        };
        return Card(new VerticalStackLayout { Spacing = 7, Children =
        {
            title, details, state, new HorizontalStackLayout { Spacing = 8, Children = { edit, delete } }
        }});
    }

    private async Task OpenEditorAsync(CustomAlertRuleDto? rule) => await Navigation.PushAsync(
        new AlertRuleEditorPage(rule, _api, _history));

    private static Border Card(View content)
    {
        var card = new Border { Content = content, Padding = 14, StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 } };
        card.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        card.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
        return card;
    }
}

public sealed class AlertRuleEditorPage : ContentPage
{
    private readonly CustomAlertRuleDto? _rule;
    private readonly MonitorApiClient _api;
    private readonly HistoryRepository _history;
    private readonly Entry _name = new() { Placeholder = "Example: SSD running hot" };
    private readonly Picker _sensor = new() { Title = "Sensor", ItemDisplayBinding = new Binding(nameof(AlertRuleSensorOption.DisplayName)) };
    private readonly Picker _direction = new() { ItemsSource = new[] { "Above", "Below" }, SelectedIndex = 0 };
    private readonly Entry _threshold = Number("85");
    private readonly Entry _reset = Number("80");
    private readonly Entry _duration = Number("10");
    private readonly Picker _severity = new() { ItemsSource = new[] { "Warning", "Critical" }, SelectedIndex = 0 };
    private readonly Switch _enabled = new() { IsToggled = true };
    private readonly Switch _notifications = new() { IsToggled = true };
    private readonly Label _error = new() { FontSize = 12, TextColor = Colors.DarkOrange };
    private IReadOnlyList<AlertRuleSensorOption> _sensors = [];
    private bool _loaded;

    public AlertRuleEditorPage(CustomAlertRuleDto? rule, MonitorApiClient api, HistoryRepository history)
    {
        _rule = rule; _api = api; _history = history; Title = rule is null ? "Add alert rule" : "Edit alert rule";
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));
        if (rule is not null)
        {
            _name.Text = rule.Name; _direction.SelectedIndex = rule.Direction == "below" ? 1 : 0;
            _threshold.Text = rule.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _reset.Text = rule.ResetThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _duration.Text = rule.MinimumDurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _severity.SelectedIndex = rule.Severity == "critical" ? 1 : 0;
            _enabled.IsToggled = rule.Enabled; _notifications.IsToggled = rule.NotificationsEnabled;
        }
        var save = new Button { Text = "Save rule" }; save.Clicked += async (_, _) => await SaveAsync();
        Content = new ScrollView { Content = new VerticalStackLayout { Padding = 18, Spacing = 11, Children =
        {
            Heading("Rule name"), _name, Heading("Sensor"), _sensor, Heading("Alert when value is"), _direction,
            Heading("Trigger threshold"), _threshold, Heading("Recovery threshold"), _reset,
            new Label { Text = "For an ‘Above’ rule, recovery must be lower than the trigger. For ‘Below’, it must be higher.", FontSize = 11, Opacity = .68 },
            Heading("Required duration in seconds"), _duration, Heading("Severity"), _severity,
            Row("Rule enabled", _enabled), Row("Send phone notification", _notifications), _error, save
        }} };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing(); if (_loaded) return; _loaded = true;
        var sensors = await _history.GetSensorOptionsAsync();
        _sensors = sensors.OrderByDescending(x => SensorDisplayText.CommonSensorPriority(x.Hardware, x.SensorName, x.SensorType, x.Unit))
            .ThenBy(x => x.Hardware, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.SensorName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new AlertRuleSensorOption(x.SensorId,
                SensorDisplayText.PickerLabel(x.Hardware, x.SensorName, x.SensorType, x.Unit))).ToArray();
        _sensor.ItemsSource = _sensors.ToList();
        _sensor.SelectedIndex = _rule is null ? (_sensors.Count > 0 ? 0 : -1)
            : Math.Max(0, _sensors.ToList().FindIndex(x => x.Id == _rule.SensorId));
    }

    private async Task SaveAsync()
    {
        try
        {
            if (_sensor.SelectedIndex < 0 || _sensor.SelectedIndex >= _sensors.Count) throw new ArgumentException("Select a sensor.");
            if (string.IsNullOrWhiteSpace(_name.Text)) throw new ArgumentException("Enter a rule name.");
            if (!TryNumber(_threshold.Text, out var threshold) || !TryNumber(_reset.Text, out var reset) ||
                !TryNumber(_duration.Text, out var duration)) throw new ArgumentException("Enter valid numeric thresholds and duration.");
            var direction = _direction.SelectedIndex == 1 ? "below" : "above";
            if (direction == "above" && reset >= threshold || direction == "below" && reset <= threshold)
                throw new ArgumentException(direction == "above" ? "Recovery must be below the trigger." : "Recovery must be above the trigger.");
            var request = new CustomAlertRuleRequestDto(_name.Text.Trim(), _sensors[_sensor.SelectedIndex].Id,
                direction, threshold, reset, duration, _severity.SelectedIndex == 1 ? "critical" : "warning",
                _enabled.IsToggled, _notifications.IsToggled);
            if (_rule is null) await _api.CreateAlertRuleAsync(request); else await _api.UpdateAlertRuleAsync(_rule.Id, request);
            await Navigation.PopAsync();
        }
        catch (Exception exception) { _error.Text = exception.Message; }
    }

    private static Entry Number(string placeholder) => new() { Placeholder = placeholder, Keyboard = Keyboard.Numeric };
    private static bool TryNumber(string? text, out double value) => double.TryParse(text,
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, out value);
    private static Label Heading(string text) => new() { Text = text, FontSize = 12, FontAttributes = FontAttributes.Bold, Opacity = .75 };
    private static Grid Row(string text, View control)
    {
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { new Label { Text = text, VerticalTextAlignment = TextAlignment.Center }, control } };
        grid.SetColumn(control, 1); return grid;
    }
}

public sealed record AlertRuleSensorOption(string Id, string DisplayName);
