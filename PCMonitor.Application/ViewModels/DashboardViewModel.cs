using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Models;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services;
using PCMonitor.Application.Services.Api;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;

namespace PCMonitor.Application.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardWidgetRepository _widgets;
    private readonly HistoryRepository _history;
    private readonly AlertRepository _alerts;
    private readonly MonitorApiClient _api;
    private readonly HistorySyncService _historySync;
    private readonly AlertSyncService _alertSync;
    private readonly CurrentSensorStateService _currentState;
    private readonly TimeProvider _timeProvider;

    public DashboardViewModel(DashboardWidgetRepository widgets, HistoryRepository history,
        AlertRepository alerts, MonitorApiClient api, HistorySyncService historySync,
        AlertSyncService alertSync, CurrentSensorStateService currentState, MonitorWebSocketClient webSocket,
        TimeProvider timeProvider)
    {
        _widgets = widgets; _history = history; _alerts = alerts; _api = api;
        _historySync = historySync; _alertSync = alertSync; _currentState = currentState; _timeProvider = timeProvider;
        currentState.SnapshotReceived += OnSnapshotReceived;
        webSocket.AlertReceived += OnAlertReceived;
    }

    public ObservableCollection<DashboardWidgetViewModelBase> Widgets { get; } = [];
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial bool IsEditMode { get; set; }
    [ObservableProperty] public partial string ConnectionState { get; set; } = "Offline";
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string MachineName { get; set; } = "Configured PC";
    [ObservableProperty] public partial DateTimeOffset? LastUpdateTimestamp { get; set; }
    [ObservableProperty] public partial string LastUpdateText { get; set; } = "No local data yet";
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    public event EventHandler? LayoutChanged;
    public event EventHandler? AddWidgetRequested;
    public event EventHandler<DashboardWidgetViewModelBase>? EditWidgetRequested;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try { await UpdateConnectionAsync(); await ReloadWidgetsAsync(); ErrorMessage = string.Empty; }
        catch (Exception exception) { ErrorMessage = $"Could not load Dashboard: {exception.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            try { await _historySync.SyncAsync(); } catch { }
            try { await _alertSync.SyncAsync(); } catch { }
            await UpdateConnectionAsync(); await ReloadWidgetsAsync();
        }
        finally { IsRefreshing = false; }
    }

    [RelayCommand] private void EnterEditMode() { IsEditMode = true; ApplyEditMode(); }
    [RelayCommand] private void ExitEditMode() { IsEditMode = false; ApplyEditMode(); }
    [RelayCommand] private void AddWidget() => AddWidgetRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void EditWidget(DashboardWidgetViewModelBase widget) => EditWidgetRequested?.Invoke(this, widget);

    public async Task DeleteWidgetAsync(DashboardWidgetViewModelBase widget)
    { await _widgets.DeleteAsync(widget.Id); await ReloadWidgetsAsync(); }
    public async Task ToggleWidgetAsync(DashboardWidgetViewModelBase widget)
    { await _widgets.SaveAsync(widget.Definition with { IsEnabled = !widget.IsEnabled }); await ReloadWidgetsAsync(); }
    public async Task MoveWidgetAsync(DashboardWidgetViewModelBase widget, int direction)
    {
        var ordered = Widgets.Select(x => x.Id).ToList();
        var oldIndex = ordered.IndexOf(widget.Id); var newIndex = Math.Clamp(oldIndex + direction, 0, ordered.Count - 1);
        if (oldIndex == newIndex) return;
        (ordered[oldIndex], ordered[newIndex]) = (ordered[newIndex], ordered[oldIndex]);
        await _widgets.ReorderAsync(ordered); await ReloadWidgetsAsync();
    }

    public async Task ReloadWidgetsAsync()
    {
        var sensorOptions = await _history.GetSensorOptionsAsync();
        await _widgets.InitializeDefaultsIfPendingAsync(sensorOptions);
        var sensors = sensorOptions.ToDictionary(x => x.SensorId, StringComparer.Ordinal);
        var definitions = await _widgets.GetAllAsync();
        Widgets.Clear();
        foreach (var definition in definitions)
        {
            var sensor = SensorFor(definition, sensors);
            DashboardWidgetViewModelBase item = definition.Type switch
            {
                DashboardWidgetType.CurrentValue => new CurrentValueWidgetViewModel(definition, sensor, _history, _currentState, _timeProvider),
                DashboardWidgetType.Graph => new GraphWidgetViewModel(definition, sensor, _history, _currentState, _timeProvider),
                DashboardWidgetType.Alerts => new AlertsWidgetViewModel(definition, _alerts, _timeProvider),
                _ => throw new InvalidOperationException($"Unsupported widget type {definition.Type}.")
            };
            item.IsEditMode = IsEditMode; Widgets.Add(item);
            if (definition.IsEnabled || IsEditMode) _ = item.LoadAsync();
        }
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task UpdateConnectionAsync()
    {
        try
        {
            var status = await _api.GetStatusAsync(); MachineName = status.MachineName;
            ConnectionState = "Connected"; IsConnected = true;
        }
        catch { ConnectionState = "Offline"; IsConnected = false; }
        var latest = _currentState.LastSnapshotTimestamp ?? await _history.GetNewestTimestampAsync();
        LastUpdateTimestamp = latest;
        LastUpdateText = latest is null ? "No local data yet" : IsConnected
            ? $"Last update: {RelativeTime(latest.Value, _timeProvider.GetUtcNow())}"
            : $"Last data: {latest.Value.ToLocalTime():g}";
    }

    private void ApplyEditMode()
    { foreach (var widget in Widgets) widget.IsEditMode = IsEditMode; LayoutChanged?.Invoke(this, EventArgs.Empty); }
    private void OnSnapshotReceived(object? sender, SensorSnapshotDto snapshot) => MainThread.BeginInvokeOnMainThread(() =>
    {
        IsConnected = true; ConnectionState = "Connected"; LastUpdateTimestamp = snapshot.Timestamp;
        LastUpdateText = "Last update: just now";
        foreach (var widget in Widgets) widget.ApplyLiveSnapshot(snapshot);
    });
    private void OnAlertReceived(object? sender, MonitorAlertDto alert) => MainThread.BeginInvokeOnMainThread(() =>
    {
        foreach (var widget in Widgets.OfType<AlertsWidgetViewModel>()) _ = widget.LoadAsync();
    });
    private static HistoricalSensorEntity? SensorFor(DashboardWidgetDefinition definition,
        IReadOnlyDictionary<string, HistoricalSensorEntity> sensors)
    {
        var id = definition.Configuration switch
        { CurrentValueWidgetConfiguration x => x.SensorId, GraphWidgetConfiguration x => x.SensorId, AlertWidgetConfiguration x => x.SensorId, _ => null };
        return id is not null && sensors.TryGetValue(id, out var sensor) ? sensor : null;
    }
    internal static string RelativeTime(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var age = now - timestamp;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{Math.Max(1, (int)age.TotalMinutes)} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{Math.Max(1, (int)age.TotalHours)} h ago";
        return timestamp.ToLocalTime().ToString("g");
    }
}

public abstract partial class DashboardWidgetViewModelBase(DashboardWidgetDefinition definition) : ObservableObject
{
    public DashboardWidgetDefinition Definition { get; } = definition;
    public Guid Id => Definition.Id;
    public DashboardWidgetType Type => Definition.Type;
    public DashboardWidgetWidth Width => Definition.Width;
    public bool IsEnabled => Definition.IsEnabled;
    [ObservableProperty] public partial bool IsEditMode { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    public abstract string Title { get; }
    public abstract Task LoadAsync();
    public virtual void ApplyLiveSnapshot(SensorSnapshotDto snapshot) { }
}

public partial class CurrentValueWidgetViewModel(DashboardWidgetDefinition definition, HistoricalSensorEntity? sensor,
    HistoryRepository history, CurrentSensorStateService currentState, TimeProvider timeProvider)
    : DashboardWidgetViewModelBase(definition)
{
    private CurrentValueWidgetConfiguration Config => (CurrentValueWidgetConfiguration)Definition.Configuration;
    public override string Title => WidgetTitle.Resolve(Definition, sensor);
    public bool ShowMinimumAndMaximum => Config.ShowMinimumAndMaximum;
    [ObservableProperty] public partial string Value { get; set; } = "—";
    [ObservableProperty] public partial string Minimum { get; set; } = "—";
    [ObservableProperty] public partial string Maximum { get; set; } = "—";
    [ObservableProperty] public partial string Freshness { get; set; } = "No recorded value";
    public override async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Config.SensorId)) return;
        IsLoading = true;
        try
        {
            if (currentState.TryGet(Config.SensorId, out var live) && live?.Value is not null) ApplyLive(live);
            else
            {
                var latest = (await history.GetRecentAsync(Config.SensorId, 1)).FirstOrDefault();
                if (latest is not null) { Value = Format(latest.Average, latest.Unit, Config.DecimalPlaces);
                    Freshness = $"Last recorded {DashboardViewModel.RelativeTime(latest.BucketStartTime, timeProvider.GetUtcNow())}"; }
            }
            if (Config.ShowMinimumAndMaximum)
            {
                var now = timeProvider.GetUtcNow(); var stats = await history.GetStatisticsAsync(Config.SensorId, now.AddHours(-24), now);
                Minimum = Format(stats?.Minimum, sensor?.Unit, Config.DecimalPlaces);
                Maximum = Format(stats?.Maximum, sensor?.Unit, Config.DecimalPlaces);
            }
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsLoading = false; }
    }
    public override void ApplyLiveSnapshot(SensorSnapshotDto snapshot)
    { var reading = snapshot.Sensors.FirstOrDefault(x => x.Id == Config.SensorId && x.Value is not null); if (reading is not null) ApplyLive(reading); }
    private void ApplyLive(SensorReadingDto reading) { Value = Format(reading.Value, reading.Unit, Config.DecimalPlaces); Freshness = "Live"; }
    internal static string Format(double? value, string? unit, int precision) =>
        DashboardWidgetPresentation.FormatValue(value, unit, precision);
}

public partial class GraphWidgetViewModel(DashboardWidgetDefinition definition, HistoricalSensorEntity? sensor,
    HistoryRepository history, CurrentSensorStateService currentState, TimeProvider timeProvider)
    : DashboardWidgetViewModelBase(definition)
{
    private GraphWidgetConfiguration Config => (GraphWidgetConfiguration)Definition.Configuration;
    public override string Title => WidgetTitle.Resolve(Definition, sensor);
    public string RangeLabel => WidgetTitle.Range(Config.EffectiveRange);
    public string? Unit => sensor?.Unit;
    public TimeSpan Range => Config.EffectiveRange;
    public bool ShowAverage => Config.ShowAverage; public bool ShowMinimum => Config.ShowMinimum; public bool ShowMaximum => Config.ShowMaximum;
    [ObservableProperty] public partial IReadOnlyList<SensorChartPoint> Points { get; set; } = [];
    [ObservableProperty] public partial string CurrentValue { get; set; } = "—";
    [ObservableProperty] public partial string Freshness { get; set; } = "No recorded value";
    [ObservableProperty] public partial DateTimeOffset RangeEnd { get; set; } = DateTimeOffset.UtcNow;
    public override async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Config.SensorId)) return;
        IsLoading = true;
        try
        {
            RangeEnd = timeProvider.GetUtcNow();
            var resolution = Range > TimeSpan.FromDays(30) ? SensorChartResolution.Day : Range > TimeSpan.FromDays(1) ? SensorChartResolution.Hour : SensorChartResolution.Minute;
            Points = await history.GetChartDataAsync(Config.SensorId, RangeEnd - Range, RangeEnd, resolution);
            if (currentState.TryGet(Config.SensorId, out var live) && live?.Value is not null) ApplyLive(live);
            else
            {
                var latest = (await history.GetRecentAsync(Config.SensorId, 1)).FirstOrDefault();
                if (latest is not null) { CurrentValue = CurrentValueWidgetViewModel.Format(latest.Average, latest.Unit, 1);
                    Freshness = $"Last recorded {DashboardViewModel.RelativeTime(latest.BucketStartTime, timeProvider.GetUtcNow())}"; }
            }
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsLoading = false; }
    }
    public override void ApplyLiveSnapshot(SensorSnapshotDto snapshot)
    { var reading = snapshot.Sensors.FirstOrDefault(x => x.Id == Config.SensorId && x.Value is not null); if (reading is not null) ApplyLive(reading); }
    private void ApplyLive(SensorReadingDto reading) { CurrentValue = CurrentValueWidgetViewModel.Format(reading.Value, reading.Unit, 1); Freshness = "Live"; }
}

public partial class AlertsWidgetViewModel(DashboardWidgetDefinition definition, AlertRepository alerts, TimeProvider timeProvider)
    : DashboardWidgetViewModelBase(definition)
{
    private AlertWidgetConfiguration Config => (AlertWidgetConfiguration)Definition.Configuration;
    public override string Title => string.IsNullOrWhiteSpace(Definition.Title) || Definition.Title == "Alerts" ? "Recent alerts" : Definition.Title;
    public ObservableCollection<DashboardAlertItem> Items { get; } = [];
    public string EmptyMessage => Config.SensorId is null && Config.MinimumSeverity is null ? "No recent warnings" : "No matching alerts";
    public override async Task LoadAsync()
    {
        IsLoading = true;
        try { Items.Clear(); foreach (var alert in await alerts.GetRecentAsync(Config.SensorId, Config.MinimumSeverity, Config.MaximumItems))
                Items.Add(DashboardAlertItem.From(alert, timeProvider.GetUtcNow())); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsLoading = false; }
    }
}

public sealed record DashboardAlertItem(string Sensor, string Value, string Severity, string Timestamp)
{
    public static DashboardAlertItem From(AlertEntity alert, DateTimeOffset now) => new(
        string.IsNullOrWhiteSpace(alert.SensorName) ? alert.Hardware : alert.SensorName,
        CurrentValueWidgetViewModel.Format(alert.Value, alert.Unit, 1), alert.Severity,
        DashboardViewModel.RelativeTime(alert.Timestamp, now));
}

internal static class WidgetTitle
{
    public static string Resolve(DashboardWidgetDefinition definition, HistoricalSensorEntity? sensor)
    {
        return DashboardWidgetPresentation.ResolveTitle(definition, sensor?.SensorName, sensor?.SensorType, sensor?.Hardware);
    }
    public static string Range(TimeSpan range) => range.TotalDays >= 365 ? "1y" : range.TotalDays >= 1 ? $"{range.TotalDays:0}d" : $"{range.TotalHours:0}h";
}
