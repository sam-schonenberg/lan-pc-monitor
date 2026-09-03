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
    public static readonly BindableProperty ComparisonSeriesProperty = BindableProperty.Create(nameof(ComparisonSeries),
        typeof(IReadOnlyList<SensorGraphSeries>), typeof(SensorChart), Array.Empty<SensorGraphSeries>(),
        propertyChanged: OnChartPropertyChanged);

    private readonly CartesianChart _chart;
    private readonly ActivityIndicator _loading;
    private readonly Label _empty;
    private readonly Grid _layers;
    private readonly FlexLayout _comparisonLegend;

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
        _layers = new Grid { MinimumHeightRequest = 220, Children = { _chart, _empty, _loading } };
        _comparisonLegend = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            IsVisible = false
        };
        var border = new Border { Padding = new Thickness(8, 12), StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = 1, Content = new VerticalStackLayout
            { Spacing = 6, Children = { _layers, _comparisonLegend } } };
        border.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
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
    public IReadOnlyList<SensorGraphSeries> ComparisonSeries { get =>
        (IReadOnlyList<SensorGraphSeries>)GetValue(ComparisonSeriesProperty); set => SetValue(ComparisonSeriesProperty, value); }

    private void RebuildChart()
    {
        var points = Points ?? Array.Empty<SensorChartPoint>();
        var comparisons = ComparisonSeries ?? Array.Empty<SensorGraphSeries>();
        _chart.HeightRequest = IsCompact ? comparisons.Count > 1 ? 205 : 170 : 220;
        _layers.MinimumHeightRequest = _chart.HeightRequest;
        var darkTheme = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
        var averageColor = SKColor.Parse(darkTheme ? GraphSeriesPalette.DarkAverage : GraphSeriesPalette.LightAverage);
        var boundaryColor = SKColor.Parse(darkTheme ? GraphSeriesPalette.DarkBoundary : GraphSeriesPalette.LightBoundary);
        var series = new List<ISeries>();
        var expectedInterval = ExpectedInterval(RangeDuration);
        if (comparisons.Count > 0)
        {
            var colors = SeriesColors();
            for (var index = 0; index < comparisons.Count; index++)
            {
                var item = comparisons[index];
                series.Add(Line(item.Name, WithGaps(item.Points, x => x.Average, expectedInterval),
                    colors[index % colors.Length], 2.5f, 0));
            }
            BuildComparisonLegend(comparisons, colors);
        }
        else
        {
            if (ShowMaximum) series.Add(Line("Maximum", WithGaps(points, x => x.Maximum, expectedInterval), boundaryColor, 1, 0));
            if (ShowAverage) series.Add(Line("Average", WithGaps(points, x => x.Average, expectedInterval), averageColor, 2.5f, 0));
            if (ShowMinimum) series.Add(Line("Minimum", WithGaps(points, x => x.Minimum, expectedInterval), boundaryColor, 1, 0));
            _comparisonLegend.Clear();
            _comparisonLegend.IsVisible = false;
        }
        _chart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
        _chart.Series = series;

        var text = darkTheme ? new SKColor(245, 248, 252) : new SKColor(7, 20, 38);
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
        var hasData = Points is { Count: > 0 } || ComparisonSeries is { Count: > 0 } comparisons &&
            comparisons.Any(x => x.Points.Count > 0);
        _loading.IsVisible = IsLoading;
        _chart.IsVisible = !IsLoading && hasData;
        _empty.IsVisible = !IsLoading && !hasData;
        _empty.Text = string.IsNullOrWhiteSpace(ErrorMessage) ? "No history available for this range" : ErrorMessage;
    }

    private static SKColor ThemeColor(string key, Color fallback)
    {
        var value = Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var resource) == true && resource is Color color
            ? color : fallback;
        return new SKColor((byte)(value.Red * 255), (byte)(value.Green * 255), (byte)(value.Blue * 255), 255);
    }

    private static SKColor[] SeriesColors()
    {
        var darkTheme = Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
        return (darkTheme ? GraphSeriesPalette.DarkSeries : GraphSeriesPalette.LightSeries).Select(SKColor.Parse).ToArray();
    }

    private void BuildComparisonLegend(IReadOnlyList<SensorGraphSeries> comparisons, IReadOnlyList<SKColor> colors)
    {
        _comparisonLegend.Clear();
        for (var index = 0; index < comparisons.Count; index++)
        {
            var color = colors[index % colors.Count];
            var marker = new BoxView
            {
                WidthRequest = 8, HeightRequest = 8, CornerRadius = 4,
                Color = Color.FromRgb(color.Red, color.Green, color.Blue),
                VerticalOptions = LayoutOptions.Center
            };
            var name = comparisons[index].Name;
            if (name.EndsWith(" Temperature", StringComparison.OrdinalIgnoreCase))
                name = name[..^" Temperature".Length];
            var label = new Label { Text = name, FontSize = 10, MaxLines = 1,
                LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center };
            _comparisonLegend.Add(new HorizontalStackLayout
            {
                Spacing = 5, Margin = new Thickness(4, 2, 10, 2),
                Children = { marker, label }
            });
        }
        _comparisonLegend.IsVisible = comparisons.Count > 1;
    }

    private static void OnChartPropertyChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SensorChart)bindable).RebuildChart();
    private static void OnStatePropertyChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((SensorChart)bindable).UpdateState();
}
