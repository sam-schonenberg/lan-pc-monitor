using PCMonitor.Application.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Controls;

namespace PCMonitor.Application.Views;

public sealed class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;

    public HistoryPage(HistoryViewModel viewModel)
    {
        Title = "History"; BindingContext = _viewModel = viewModel;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F4F7FB"), Color.FromArgb("#141414"));

        var records = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            RemainingItemsThreshold = 10,
            Header = CreateHeader(),
            ItemTemplate = new DataTemplate(CreateRecordCard),
            EmptyView = CreateEmptyView(),
            Footer = CreateFooter(),
            Margin = new Thickness(18, 0)
        };
        records.SetBinding(ItemsView.ItemsSourceProperty, nameof(viewModel.DetailedRecords));
        records.SetBinding(ItemsView.RemainingItemsThresholdReachedCommandProperty, nameof(viewModel.LoadMoreCommand));

        var refresh = new RefreshView { Content = records };
        refresh.SetBinding(RefreshView.CommandProperty, nameof(viewModel.RefreshCommand));
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(viewModel.IsRefreshing));
        Content = refresh;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.InitializeCommand.Execute(null);
    }

    private View CreateHeader()
    {
        var sensorPicker = new Picker { Title = "Sensor", ItemsSource = _viewModel.AvailableSensors,
            ItemDisplayBinding = new Binding(nameof(HistorySensorOption.DisplayName)) };
        var rangePicker = new Picker { Title = "Range", ItemDisplayBinding = new Binding(nameof(HistoryRangeOption.ShortName)) };
        rangePicker.ItemsSource = _viewModel.AvailableRanges;

        sensorPicker.SelectedIndexChanged += (_, _) =>
        {
            if (sensorPicker.SelectedIndex >= 0 && sensorPicker.SelectedIndex < _viewModel.AvailableSensors.Count)
                _viewModel.SelectedSensor = _viewModel.AvailableSensors[sensorPicker.SelectedIndex];
        };
        rangePicker.SelectedIndexChanged += (_, _) =>
        {
            if (rangePicker.SelectedIndex >= 0 && rangePicker.SelectedIndex < _viewModel.AvailableRanges.Count)
                _viewModel.SelectedRange = _viewModel.AvailableRanges[rangePicker.SelectedIndex];
        };
        void UpdatePickerSelections()
        {
            var sensorIndex = _viewModel.SelectedSensor is null
                ? -1 : _viewModel.AvailableSensors.IndexOf(_viewModel.SelectedSensor);
            if (sensorPicker.SelectedIndex != sensorIndex) sensorPicker.SelectedIndex = sensorIndex;
            var rangeIndex = _viewModel.AvailableRanges.IndexOf(_viewModel.SelectedRange);
            if (rangePicker.SelectedIndex != rangeIndex) rangePicker.SelectedIndex = rangeIndex;
        }
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(_viewModel.SelectedSensor) or nameof(_viewModel.SelectedRange))
                UpdatePickerSelections();
        };
        UpdatePickerSelections();

        var filters = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(126)) },
            ColumnSpacing = 10, Children = { Card(sensorPicker, 10), Card(rangePicker, 10) }
        };
        filters.SetColumn(filters.Children[1], 1);

        var sensorType = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        sensorType.SetBinding(Label.TextProperty, nameof(_viewModel.ChartSensorType));
        var sensorHardware = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1, Opacity = 0.8 };
        sensorHardware.SetBinding(Label.TextProperty, nameof(_viewModel.ChartSensorHardware));
        var sensorName = new Label { FontSize = 13, LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1, Opacity = 0.8 };
        sensorName.SetBinding(Label.TextProperty, nameof(_viewModel.ChartSensorName));
        var chartTitle = new VerticalStackLayout { Spacing = 2,
            Children = { sensorType, sensorHardware, sensorName } };
        var chartRange = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Fill, HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center };
        chartRange.SetBinding(Label.TextProperty, nameof(_viewModel.ChartRangeLabel));
        var chartHeading = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(52)) }, ColumnSpacing = 12,
            Children = { chartTitle, chartRange }
        };
        chartHeading.SetColumn(chartRange, 1);

        var chartContent = new SensorChart { ShowAverage = true, ShowMinimum = true, ShowMaximum = true };
        chartContent.SetBinding(SensorChart.PointsProperty, nameof(_viewModel.ChartPoints));
        chartContent.SetBinding(SensorChart.UnitProperty, $"{nameof(_viewModel.SelectedSensor)}.{nameof(HistorySensorOption.Unit)}");
        chartContent.SetBinding(SensorChart.SensorNameProperty, $"{nameof(_viewModel.SelectedSensor)}.{nameof(HistorySensorOption.Name)}");
        chartContent.SetBinding(SensorChart.RangeDurationProperty, $"{nameof(_viewModel.SelectedRange)}.{nameof(HistoryRangeOption.Duration)}");
        chartContent.SetBinding(SensorChart.RangeEndProperty, nameof(_viewModel.ChartRangeEnd));
        chartContent.SetBinding(SensorChart.IsLoadingProperty, nameof(_viewModel.IsChartLoading));
        chartContent.SetBinding(SensorChart.ErrorMessageProperty, nameof(_viewModel.ChartErrorMessage));

        var statistics = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        statistics.Add(Statistic("Average", nameof(_viewModel.Average)), 0);
        statistics.Add(Statistic("Minimum", nameof(_viewModel.Minimum)), 1);
        statistics.Add(Statistic("Maximum", nameof(_viewModel.Maximum)), 2);
        statistics.Add(Statistic("Latest", nameof(_viewModel.Latest)), 3);

        var detailHeading = new Label { Text = "Detailed history", FontSize = 20, FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 8, 0, 0) };
        var status = new Label { FontSize = 12, Opacity = 0.7 };
        status.SetBinding(Label.TextProperty, nameof(_viewModel.StatusMessage));
        var error = new Label { FontSize = 12, TextColor = Colors.DarkOrange };
        error.SetBinding(Label.TextProperty, nameof(_viewModel.ErrorMessage));

        var syncProgress = new ProgressBar { HeightRequest = 5, ProgressColor = Color.FromArgb("#512BD4") };
        syncProgress.SetBinding(ProgressBar.ProgressProperty, nameof(_viewModel.HistorySyncProgress));
        syncProgress.SetBinding(IsVisibleProperty, nameof(_viewModel.IsHistorySyncing));
        var syncProgressText = new Label { FontSize = 12, Opacity = 0.75 };
        syncProgressText.SetBinding(Label.TextProperty, nameof(_viewModel.HistorySyncProgressText));
        syncProgressText.SetBinding(IsVisibleProperty, nameof(_viewModel.IsHistorySyncing));

        var loading = new HorizontalStackLayout { Spacing = 8, Children = { new ActivityIndicator { IsRunning = true },
            new Label { Text = "Loading history…", VerticalTextAlignment = TextAlignment.Center } } };
        loading.SetBinding(IsVisibleProperty, nameof(_viewModel.IsLoading));

        return new VerticalStackLayout
        {
            Padding = new Thickness(0, 18, 0, 12), Spacing = 14,
            Children =
            {
                filters, chartHeading, chartContent, Card(statistics, 14), detailHeading, status, error,
                syncProgress, syncProgressText, loading
            }
        };
    }

    private static View CreateRecordCard()
    {
        var timestamp = BoundLabel(nameof(HistoryRecordItem.DisplayTimestamp), 16, FontAttributes.Bold);
        var average = BoundLabel(nameof(HistoryRecordItem.DisplayAverage), 15, FontAttributes.Bold);
        var minimum = BoundLabel(nameof(HistoryRecordItem.DisplayMinimum), 13);
        var maximum = BoundLabel(nameof(HistoryRecordItem.DisplayMaximum), 13);
        var values = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 18,
            Children = { LabeledValue("Average", average), LabeledValue("Min", minimum), LabeledValue("Max", maximum) }
        };
        values.SetColumn(values.Children[1], 1); values.SetColumn(values.Children[2], 2);
        return Card(new VerticalStackLayout { Spacing = 8, Children = { timestamp, values } }, 14,
            new Thickness(0, 0, 0, 10));
    }

    private View CreateEmptyView()
    {
        var message = new Label { HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.7,
            Margin = new Thickness(24, 36), LineBreakMode = LineBreakMode.WordWrap };
        message.SetBinding(Label.TextProperty, nameof(_viewModel.EmptyMessage));
        return message;
    }

    private View CreateFooter()
    {
        var indicator = new ActivityIndicator { IsRunning = true };
        indicator.SetBinding(IsVisibleProperty, nameof(_viewModel.IsLoadingMore));
        return new Grid { HeightRequest = 48, Children = { indicator } };
    }

    private static View Statistic(string heading, string binding)
    {
        var value = BoundLabel(binding, 15, FontAttributes.Bold); value.HorizontalTextAlignment = TextAlignment.Center;
        return new VerticalStackLayout { Spacing = 4, Children =
        {
            new Label { Text = heading, FontSize = 11, Opacity = 0.65, HorizontalTextAlignment = TextAlignment.Center }, value
        } };
    }

    private static View LabeledValue(string heading, View value) => new VerticalStackLayout { Spacing = 2, Children =
    {
        new Label { Text = heading, FontSize = 10, Opacity = 0.6 }, value
    } };

    private static Label BoundLabel(string binding, double size, FontAttributes attributes = FontAttributes.None)
    {
        var label = new Label { FontSize = size, FontAttributes = attributes };
        label.SetBinding(Label.TextProperty, binding); return label;
    }

    private static Border Card(View content, double padding, Thickness? margin = null)
    {
        var border = new Border { Content = content, Padding = padding, Margin = margin ?? Thickness.Zero,
            StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1 };
        border.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#212121"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D8DEE9"), Color.FromArgb("#404040"));
        return border;
    }
}
