using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Maui;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Models;
using SkiaSharp;

namespace PCMonitor.Application.Controls;

public sealed class SensorChart : ContentView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(nameof(Points),
        typeof(IReadOnlyList<SensorChartPoint>), typeof(SensorChart), Array.Empty<SensorChartPoint>(),
        propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty UnitProperty = BindableProperty.Create(nameof(Unit), typeof(string),
        typeof(SensorChart), string.Empty, propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty SensorNameProperty = BindableProperty.Create(nameof(SensorName),
        typeof(string), typeof(SensorChart), string.Empty);
    public static readonly BindableProperty RangeDurationProperty = BindableProperty.Create(nameof(RangeDuration),
        typeof(TimeSpan), typeof(SensorChart), TimeSpan.FromHours(24), propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty RangeEndProperty = BindableProperty.Create(nameof(RangeEnd),
        typeof(DateTimeOffset), typeof(SensorChart), DateTimeOffset.UtcNow, propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty ShowAverageProperty = BindableProperty.Create(nameof(ShowAverage),
        typeof(bool), typeof(SensorChart), true, propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty ShowMinimumProperty = BindableProperty.Create(nameof(ShowMinimum),
        typeof(bool), typeof(SensorChart), true, propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty ShowMaximumProperty = BindableProperty.Create(nameof(ShowMaximum),
        typeof(bool), typeof(SensorChart), true, propertyChanged: OnChartPropertyChanged);
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading),
        typeof(bool), typeof(SensorChart), false, propertyChanged: OnStatePropertyChanged);
    public static readonly BindableProperty ErrorMessageProperty = BindableProperty.Create(nameof(ErrorMessage),
        typeof(string), typeof(SensorChart), string.Empty, propertyChanged: OnStatePropertyChanged);
    public static readonly BindableProperty IsCompactProperty = BindableProperty.Create(nameof(IsCompact),
        typeof(bool), typeof(SensorChart), false, propertyChanged: OnChartPropertyChanged);

    private readonly CartesianChart _chart;
    private readonly ActivityIndicator _loading;
    private readonly Label _empty;

    public SensorChart()
    {
        _chart = new CartesianChart
        {
            HeightRequest = 220,
            AnimationsSpeed = TimeSpan.FromMilliseconds(120),
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top,
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden
        };
        _loading = new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center };
        _empty = new Label { Text = "No history available for this range", Opacity = 0.7,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var layers = new Grid { MinimumHeightRequest = 220, Children = { _chart, _empty, _loading } };
        var border = new Border { Padding = new Thickness(8, 12), StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = 1, Content = layers };
        border.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#212121"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8DEE9"), Color.FromArgb("#404040"));
        Content = border;
        RebuildChart(); UpdateState();
    }

    public IReadOnlyList<SensorChartPoint> Points { get => (IReadOnlyList<SensorChartPoint>)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string SensorName { get => (string)GetValue(SensorNameProperty); set => SetValue(SensorNameProperty, value); }
    public TimeSpan RangeDuration { get => (TimeSpan)GetValue(RangeDurationProperty); set => SetValue(RangeDurationProperty, value); }
    public DateTimeOffset RangeEnd { get => (DateTimeOffset)GetValue(RangeEndProperty); set => SetValue(RangeEndProperty, value); }
    public bool ShowAverage { get => (bool)GetValue(ShowAverageProperty); set => SetValue(ShowAverageProperty, value); }
    public bool ShowMinimum { get => (bool)GetValue(ShowMinimumProperty); set => SetValue(ShowMinimumProperty, value); }
    public bool ShowMaximum { get => (bool)GetValue(ShowMaximumProperty); set => SetValue(ShowMaximumProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string ErrorMessage { get => (string)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }

    private void RebuildChart()
    {
        var points = Points ?? Array.Empty<SensorChartPoint>();
        _chart.HeightRequest = IsCompact ? 170 : 220;
        var averageColor = ThemeColor("Primary", Colors.MediumPurple);
        var boundaryColor = ThemeColor("SecondaryText", Colors.SlateGray);
        var series = new List<ISeries>();
        var expectedInterval = ExpectedInterval(RangeDuration);
        if (ShowMaximum) series.Add(Line("Maximum", WithGaps(points, x => x.Maximum, expectedInterval), boundaryColor, 1, 0));
        if (ShowAverage) series.Add(Line("Average", WithGaps(points, x => x.Average, expectedInterval), averageColor, 2.5f, 4));
        if (ShowMinimum) series.Add(Line("Minimum", WithGaps(points, x => x.Minimum, expectedInterval), boundaryColor, 1, 0));
        _chart.Series = series;

        var darkTheme = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
        var text = darkTheme ? new SKColor(225, 225, 225) : new SKColor(64, 64, 64);
        var duration = RangeDuration <= TimeSpan.Zero ? TimeSpan.FromHours(24) : RangeDuration;
        var rangeEnd = RangeEnd == default ? DateTimeOffset.UtcNow : RangeEnd.ToUniversalTime();
        _chart.XAxes =
        [
            new Axis
            {
                Labeler = value => FormatTime(value, duration),
                MinStep = Math.Max(TimeSpan.TicksPerMinute, duration.Ticks / (IsCompact ? 4d : 7d)),
                MinLimit = rangeEnd.Subtract(duration).UtcTicks,
                MaxLimit = rangeEnd.UtcTicks,
                LabelsPaint = new SolidColorPaint(text) { SKTypeface = SKTypeface.Default },
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(text.WithAlpha(52)) { StrokeThickness = 1 }
            }
        ];
        _chart.YAxes =
        [
            new Axis
            {
                Name = IsCompact ? null : Unit,
                Labeler = value => string.IsNullOrWhiteSpace(Unit) ? $"{value:0.#}" : $"{value:0.#} {Unit}",
                LabelsPaint = new SolidColorPaint(text), NamePaint = new SolidColorPaint(text), TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(text.WithAlpha(52)) { StrokeThickness = 1 }
            }
        ];
        UpdateState();
    }

    private LineSeries<ObservablePoint?> Line(string name, IEnumerable<ObservablePoint?> values,
        SKColor color, float thickness, double geometrySize) => new()
    {
        Name = name, Values = values.ToArray(), Fill = null, LineSmoothness = 0,
        GeometrySize = geometrySize, Stroke = new SolidColorPaint(color) { StrokeThickness = thickness },
        GeometryStroke = new SolidColorPaint(color) { StrokeThickness = thickness },
        GeometryFill = new SolidColorPaint(color),
        XToolTipLabelFormatter = point =>
            new DateTimeOffset((long)point.Coordinate.SecondaryValue, TimeSpan.Zero).ToLocalTime().ToString("dd MMM, HH:mm"),
        YToolTipLabelFormatter = point => $"{name}  {point.Coordinate.PrimaryValue:0.0}{UnitSuffix()}"
    };

    private static IEnumerable<ObservablePoint?> WithGaps(IReadOnlyList<SensorChartPoint> points,
        Func<SensorChartPoint, double> value, TimeSpan expectedInterval)
    {
        SensorChartPoint? previous = null;
        foreach (var point in points.OrderBy(x => x.Timestamp))
        {
            if (previous is not null && point.Timestamp - previous.Timestamp > expectedInterval * 1.5)
                yield return null;
            yield return Point(point.Timestamp, value(point));
            previous = point;
        }
    }

    private static TimeSpan ExpectedInterval(TimeSpan duration) => duration <= TimeSpan.FromDays(1)
        ? TimeSpan.FromMinutes(1)
        : duration <= TimeSpan.FromDays(30) ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1);

    private static ObservablePoint Point(DateTimeOffset timestamp, double value) => new(timestamp.UtcTicks, value);
    private string UnitSuffix() => string.IsNullOrWhiteSpace(Unit) ? string.Empty : $" {Unit}";

    private static string FormatTime(double ticks, TimeSpan duration)
    {
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return string.Empty;
        var local = new DateTimeOffset((long)ticks, TimeSpan.Zero).ToLocalTime();
        if (duration <= TimeSpan.FromDays(1)) return local.ToString("HH:mm");
        if (duration <= TimeSpan.FromDays(7)) return local.ToString("ddd HH:mm");
        if (duration <= TimeSpan.FromDays(60)) return local.ToString("dd MMM");
        return local.ToString("MMM");
    }

    private void UpdateState()
    {
        _loading.IsVisible = IsLoading;
        _chart.IsVisible = !IsLoading && Points is { Count: > 0 };
        _empty.IsVisible = !IsLoading && Points is not { Count: > 0 };
        _empty.Text = string.IsNullOrWhiteSpace(ErrorMessage) ? "No history available for this range" : ErrorMessage;
    }

    private static SKColor ThemeColor(string key, Color fallback)
    {
        var value = Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Color color
            ? color : fallback;
        return new SKColor((byte)(value.Red * 255), (byte)(value.Green * 255), (byte)(value.Blue * 255), 255);
    }

    private static void OnChartPropertyChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SensorChart)bindable).RebuildChart();
    private static void OnStatePropertyChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SensorChart)bindable).UpdateState();
}
