using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Data.Entities;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Sync;
using PCMonitor.Application.Models;

namespace PCMonitor.Application.ViewModels;

public partial class HistoryViewModel(
    HistorySyncService sync,
    HistoryRepository repository,
    IAppSettingsService settings,
    TimeProvider timeProvider) : ObservableObject
{
    private static readonly HistoryRangeOption DefaultRange =
        new(HistoryRange.TwentyFourHours, "24 hours", "24h", TimeSpan.FromHours(24));
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _chartCancellation;
    private DateTimeOffset _rangeFrom;
    private DateTimeOffset _rangeTo;
    private DateTimeOffset? _oldestLoadedTimestamp;
    private long _loadGeneration;
    private long _chartGeneration;
    private bool _initialized;
    private bool _updatingSensorOptions;

    public ObservableCollection<HistorySensorOption> AvailableSensors { get; } = [];
    public ObservableCollection<HistoryRangeOption> AvailableRanges { get; } =
    [
        new(HistoryRange.OneHour, "1 hour", "1h", TimeSpan.FromHours(1)),
        new(HistoryRange.SixHours, "6 hours", "6h", TimeSpan.FromHours(6)),
        DefaultRange,
        new(HistoryRange.SevenDays, "7 days", "7d", TimeSpan.FromDays(7)),
        new(HistoryRange.ThirtyDays, "30 days", "30d", TimeSpan.FromDays(30)),
        new(HistoryRange.OneYear, "1 year", "1y", TimeSpan.FromDays(365))
    ];
    public ObservableCollection<HistoryRecordItem> DetailedRecords { get; } = [];

    // Compatibility aliases for the original placeholder bindings.
    public ObservableCollection<HistorySensorOption> Sensors => AvailableSensors;
    public ObservableCollection<HistoryRecordItem> Readings => DetailedRecords;

    [ObservableProperty] public partial HistorySensorOption? SelectedSensor { get; set; }
    [ObservableProperty] public partial HistoryRangeOption SelectedRange { get; set; } = DefaultRange;
    [ObservableProperty] public partial string Average { get; set; } = "—";
    [ObservableProperty] public partial string Minimum { get; set; } = "—";
    [ObservableProperty] public partial string Maximum { get; set; } = "—";
    [ObservableProperty] public partial string Latest { get; set; } = "—";
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool IsLoadingMore { get; set; }
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial bool HasMoreRecords { get; set; }
    [ObservableProperty] public partial bool HasSensors { get; set; }
    [ObservableProperty] public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusMessage { get; set; } = "Loading history…";
    [ObservableProperty] public partial string EmptyMessage { get; set; } = "Loading history…";
    [ObservableProperty] public partial IReadOnlyList<SensorChartPoint> ChartPoints { get; set; } = Array.Empty<SensorChartPoint>();
    [ObservableProperty] public partial bool IsChartLoading { get; set; }
    [ObservableProperty] public partial string ChartErrorMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTimeOffset ChartRangeEnd { get; set; } = DateTimeOffset.UtcNow;
    [ObservableProperty] public partial bool IsHistorySyncing { get; set; }
    [ObservableProperty] public partial double HistorySyncProgress { get; set; }
    [ObservableProperty] public partial string HistorySyncProgressText { get; set; } = string.Empty;

    public bool IsBusy => IsLoading || IsRefreshing;
    public DateTimeOffset? OldestLoadedTimestamp => _oldestLoadedTimestamp;
    public string ChartTitle => SelectedSensor?.DisplayName ?? "History";
    public string ChartSensorType => SelectedSensor?.Type ?? "History";
    public string ChartSensorHardware => SelectedSensor?.Hardware ?? string.Empty;
    public string ChartSensorName => SelectedSensor?.Name ?? string.Empty;
    public string ChartRangeLabel => SelectedRange?.ShortName ?? string.Empty;

    partial void OnSelectedSensorChanged(HistorySensorOption? value)
    {
        OnPropertyChanged(nameof(ChartTitle));
        OnPropertyChanged(nameof(ChartSensorType));
        OnPropertyChanged(nameof(ChartSensorHardware));
        OnPropertyChanged(nameof(ChartSensorName));
        if (!_initialized || _updatingSensorOptions) return;
        if (value is not null) _ = settings.SetHistorySensorIdAsync(value.Id);
        _ = ResetAndLoadAsync();
    }

    partial void OnSelectedRangeChanged(HistoryRangeOption value)
    {
        OnPropertyChanged(nameof(ChartRangeLabel));
        if (_initialized && value is not null) _ = ResetAndLoadAsync();
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (IsLoading) return;
        if (_initialized)
        {
            var selectedId = SelectedSensor?.Id ?? await settings.GetHistorySensorIdAsync();
            await LoadSensorOptionsAsync(selectedId);
            if (SelectedSensor is null) await BootstrapHistoryAsync(selectedId);
            if (SelectedSensor is not null) await ResetAndLoadAsync();
            else SetNoSensorsState();
            return;
        }
        IsLoading = true;
        try
        {
            var savedSensor = await settings.GetHistorySensorIdAsync();
            await LoadSensorOptionsAsync(savedSensor);
            if (SelectedSensor is null)
                await BootstrapHistoryAsync(savedSensor);
            _initialized = true;
            if (SelectedSensor is not null) await ResetAndLoadAsync();
            else SetNoSensorsState();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not load saved history: {exception.Message}";
            StatusMessage = ErrorMessage;
        }
        finally { IsLoading = false; OnPropertyChanged(nameof(IsBusy)); }
    }

    private async Task BootstrapHistoryAsync(string? preferredSensorId)
    {
        IsHistorySyncing = true;
        HistorySyncProgress = 0.03;
        HistorySyncProgressText = "Downloading the sensor catalog and latest history…";
        try
        {
            var progress = new Progress<HistorySyncProgress>(update =>
            {
                HistorySyncProgress = update.BarProgress;
                HistorySyncProgressText = update.Message;
            });
            await sync.SyncAsync(progress);
            await LoadSensorOptionsAsync(preferredSensorId);
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No saved sensors are available and the PC could not be synchronized. {exception.Message}";
        }
        finally
        {
            IsHistorySyncing = false;
        }
    }

    [RelayCommand]
    public async Task LoadAsync() => await ResetAndLoadAsync();

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsLoading || IsLoadingMore || !HasMoreRecords || SelectedSensor is null) return;
        var generation = _loadGeneration;
        var token = _loadCancellation?.Token ?? CancellationToken.None;
        IsLoadingMore = true;
        try
        {
            var rows = await repository.GetSensorHistoryPageAsync(SelectedSensor.Id, _rangeFrom, _rangeTo,
                _oldestLoadedTimestamp, HistoryRepository.DetailPageSize, token);
            if (generation != _loadGeneration || token.IsCancellationRequested) return;
            Append(rows, SelectedSensor);
            HasMoreRecords = rows.Count == HistoryRepository.DetailPageSize &&
                             _oldestLoadedTimestamp is not null && _oldestLoadedTimestamp > _rangeFrom;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ErrorMessage = $"Could not load older history: {exception.Message}"; }
        finally { if (generation == _loadGeneration) IsLoadingMore = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true; OnPropertyChanged(nameof(IsBusy));
        IsHistorySyncing = true;
        HistorySyncProgress = 0.03;
        HistorySyncProgressText = "Starting history synchronization…";
        var selectedId = SelectedSensor?.Id;
        try
        {
            var progress = new Progress<HistorySyncProgress>(update =>
            {
                HistorySyncProgress = update.BarProgress;
                HistorySyncProgressText = update.Message;
            });
            await sync.SyncAsync(progress);
            ErrorMessage = string.Empty;
            await LoadSensorOptionsAsync(selectedId);
            await ResetAndLoadAsync();
            StatusMessage = "History is up to date.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"PC unavailable. Showing saved history. {exception.Message}";
            StatusMessage = ErrorMessage;
            // Do not clear useful local records on synchronization failure.
        }
        finally
        {
            IsRefreshing = false;
            IsHistorySyncing = false;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    private async Task ResetAndLoadAsync()
    {
        _loadCancellation?.Cancel(); _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;
        var generation = ++_loadGeneration;
        DetailedRecords.Clear(); _oldestLoadedTimestamp = null; HasMoreRecords = false;
        ClearStatistics(); ErrorMessage = string.Empty;
        if (SelectedSensor is null || SelectedRange is null) { SetNoSensorsState(); return; }

        IsLoading = true; OnPropertyChanged(nameof(IsBusy));
        StatusMessage = "Loading history…"; EmptyMessage = StatusMessage;
        try
        {
            _rangeTo = timeProvider.GetLocalNow().ToUniversalTime();
            _rangeFrom = _rangeTo - SelectedRange.Duration;
            ChartRangeEnd = _rangeTo;
            var statisticsTask = repository.GetStatisticsAsync(SelectedSensor.Id, _rangeFrom, _rangeTo, token);
            var pageTask = repository.GetSensorHistoryPageAsync(SelectedSensor.Id, _rangeFrom, _rangeTo, null,
                HistoryRepository.DetailPageSize, token);
            // The chart can aggregate substantially more data for long ranges. Keep it on its own
            // loading state so it cannot hold the statistics and detailed history UI hostage.
            _ = LoadChartAsync(SelectedSensor, SelectedRange, _rangeFrom, _rangeTo);
            await Task.WhenAll(statisticsTask, pageTask);
            if (generation != _loadGeneration || token.IsCancellationRequested) return;

            ApplyStatistics(await statisticsTask, SelectedSensor.Unit);
            var rows = await pageTask;
            Append(rows, SelectedSensor);
            HasMoreRecords = rows.Count == HistoryRepository.DetailPageSize &&
                             _oldestLoadedTimestamp is not null && _oldestLoadedTimestamp > _rangeFrom;
            StatusMessage = rows.Count == 0 ? "No saved records in this range." : $"{rows.Count} newest records loaded";
            EmptyMessage = "No history is available for this sensor and time range.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not load history: {exception.Message}"; StatusMessage = ErrorMessage;
        }
        finally { if (generation == _loadGeneration) { IsLoading = false; OnPropertyChanged(nameof(IsBusy)); } }
    }

    private async Task LoadChartAsync(HistorySensorOption sensor, HistoryRangeOption range,
        DateTimeOffset from, DateTimeOffset to)
    {
        _chartCancellation?.Cancel(); _chartCancellation?.Dispose();
        _chartCancellation = new CancellationTokenSource();
        var token = _chartCancellation.Token; var generation = ++_chartGeneration;
        IsChartLoading = true; ChartErrorMessage = string.Empty;
        try
        {
            var resolution = range.Range switch
            {
                HistoryRange.SevenDays or HistoryRange.ThirtyDays => SensorChartResolution.Hour,
                HistoryRange.OneYear => SensorChartResolution.Day,
                _ => SensorChartResolution.Minute
            };
            var points = await repository.GetChartDataAsync(sensor.Id, from, to, resolution, token);
            if (generation == _chartGeneration && !token.IsCancellationRequested) ChartPoints = points;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _chartGeneration) ChartErrorMessage = $"Could not load chart: {exception.Message}";
        }
        finally { if (generation == _chartGeneration) IsChartLoading = false; }
    }

    private async Task LoadSensorOptionsAsync(string? preferredId)
    {
        var hidden = await settings.GetHiddenSensorIdsAsync();
        var options = (await repository.GetSensorOptionsAsync())
            .Where(x => !hidden.Contains(x.SensorId))
            .Select(x => new HistorySensorOption(x.SensorId, x.Hardware, x.SensorName, x.SensorType, x.Unit)).ToArray();
        _updatingSensorOptions = true;
        try
        {
            // Force the Picker to adopt an object from the rebuilt collection. Equal record values
            // can otherwise leave SelectedItem referencing an instance that was just removed.
            SelectedSensor = null;
            AvailableSensors.Clear();
            foreach (var option in options) AvailableSensors.Add(option);
            HasSensors = options.Length > 0;
            SelectedSensor = options.FirstOrDefault(x => x.Id == preferredId) ?? ChooseDefault(options);
        }
        finally
        {
            _updatingSensorOptions = false;
        }
    }

    private static HistorySensorOption? ChooseDefault(IReadOnlyList<HistorySensorOption> options) =>
        options.FirstOrDefault(x => x.IsGpuTemperature) ??
        options.FirstOrDefault(x => x.IsCpuPackageTemperature) ??
        options.FirstOrDefault(x => x.IsTemperature) ?? options.FirstOrDefault();

    private void Append(IEnumerable<HistoricalSensorEntity> rows, HistorySensorOption sensor)
    {
        foreach (var row in rows)
        {
            DetailedRecords.Add(HistoryRecordItem.From(row, sensor.Unit, timeProvider.GetLocalNow()));
            _oldestLoadedTimestamp = row.BucketStartTime;
        }
    }

    private void ApplyStatistics(HistoricalRangeStatistics? statistics, string? unit)
    {
        if (statistics is null) { ClearStatistics(); return; }
        Average = HistoryValueFormatter.Format(statistics.Average, unit);
        Minimum = HistoryValueFormatter.Format(statistics.Minimum, unit);
        Maximum = HistoryValueFormatter.Format(statistics.Maximum, unit);
        Latest = HistoryValueFormatter.Format(statistics.Latest, unit);
    }
    private void ClearStatistics() => Average = Minimum = Maximum = Latest = "—";
    private void SetNoSensorsState()
    {
        HasSensors = false; HasMoreRecords = false;
        ChartPoints = Array.Empty<SensorChartPoint>();
        StatusMessage = EmptyMessage = "No sensor catalog is available yet. Connect to the PC and synchronize first.";
    }
}

public enum HistoryRange { OneHour, SixHours, TwentyFourHours, SevenDays, ThirtyDays, OneYear }
public sealed record HistoryRangeOption(HistoryRange Range, string DisplayName, string ShortName, TimeSpan Duration);

public sealed record HistorySensorOption(string Id, string Hardware, string Name, string Type, string? Unit)
{
    public string DisplayName => $"{Type} — {Hardware} — {Name}";
    public bool IsTemperature => Type.Contains("temperature", StringComparison.OrdinalIgnoreCase);
    public bool IsGpuTemperature => IsTemperature && Hardware.Contains("gpu", StringComparison.OrdinalIgnoreCase) ||
                                    IsTemperature && Hardware.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
                                    IsTemperature && Hardware.Contains("radeon", StringComparison.OrdinalIgnoreCase);
    public bool IsCpuPackageTemperature => IsTemperature &&
        (Hardware.Contains("cpu", StringComparison.OrdinalIgnoreCase) || Name.Contains("cpu", StringComparison.OrdinalIgnoreCase)) &&
        Name.Contains("package", StringComparison.OrdinalIgnoreCase);
}

public sealed record HistoryRecordItem(DateTimeOffset Timestamp, double AverageValue, double MinimumValue,
    double MaximumValue, long SampleCount, string DisplayTimestamp, string DisplayAverage,
    string DisplayMinimum, string DisplayMaximum)
{
    public static HistoryRecordItem From(HistoricalSensorEntity item, string? unit, DateTimeOffset localNow) => new(
        item.BucketStartTime, item.Average, item.Min, item.Max, item.SampleCount,
        HistoryValueFormatter.FormatTimestamp(item.BucketStartTime, localNow),
        HistoryValueFormatter.Format(item.Average, unit), HistoryValueFormatter.Format(item.Min, unit),
        HistoryValueFormatter.Format(item.Max, unit));
}

public static class HistoryValueFormatter
{
    public static string Format(double? value, string? unit)
    {
        if (value is null || !double.IsFinite(value.Value)) return "—";
        var number = string.Equals(unit, "RPM", StringComparison.OrdinalIgnoreCase) ? value.Value.ToString("0") : value.Value.ToString("0.0");
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    public static string FormatTimestamp(DateTimeOffset utc, DateTimeOffset localNow)
    {
        var local = utc.ToLocalTime(); var today = localNow.Date;
        if (local.Date == today) return local.ToString("HH:mm");
        return local.Year == localNow.Year ? local.ToString("dd MMM, HH:mm") : local.ToString("dd MMM yyyy, HH:mm");
    }
}
