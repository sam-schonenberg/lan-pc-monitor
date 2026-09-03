using PCMonitor.Application.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using PCMonitor.Application.Controls;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Export;

namespace PCMonitor.Application.Views;

public sealed class HistoryPage : ContentPage
{
    private readonly HistoryViewModel _viewModel;
    private readonly GraphImageExportService _graphImageExport;

    public HistoryPage(HistoryViewModel viewModel, GraphImageExportService graphImageExport)
    {
        Title = "History"; BindingContext = _viewModel = viewModel; _graphImageExport = graphImageExport;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));

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
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(viewModel.IsRefreshing), mode: BindingMode.TwoWay);
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

        var addComparison = new Button { Text = "+ Add sensor", FontSize = 12, Padding = new Thickness(10, 5),
            MinimumHeightRequest = 38, HorizontalOptions = LayoutOptions.Start };
        addComparison.Clicked += async (_, _) => await ShowComparisonPickerAsync();
        var selectedSensors = new HorizontalStackLayout { Spacing = 6 };
        BindableLayout.SetItemsSource(selectedSensors, _viewModel.SelectedSensors);
        BindableLayout.SetItemTemplate(selectedSensors, new DataTemplate(() =>
        {
            var name = new Label { FontSize = 11, VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
            name.SetBinding(Label.TextProperty, nameof(HistorySensorOption.DisplayName));
            var remove = new Button { Text = "×", FontSize = 14, Padding = 0, WidthRequest = 28,
                HeightRequest = 30, MinimumHeightRequest = 30 };
            remove.BindingContextChanged += (_, _) =>
            {
                if (remove.BindingContext is HistorySensorOption option)
                    remove.IsVisible = option.Id != _viewModel.SelectedSensor?.Id;
            };
            remove.Clicked += async (sender, _) =>
            {
                if ((sender as BindableObject)?.BindingContext is HistorySensorOption option)
                    await _viewModel.RemoveComparisonAsync(option);
            };
            var row = new HorizontalStackLayout { Spacing = 3, Children = { name, remove } };
            return Card(row, 5);
        }));
        var comparisonStrip = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "Compare", FontSize = 12, FontAttributes = FontAttributes.Bold,
                    VerticalTextAlignment = TextAlignment.Center },
                selectedSensors,
                addComparison
            }
        };

        var sensorName = new Label { FontSize = 15, FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        sensorName.SetBinding(Label.TextProperty, nameof(_viewModel.ChartSensorName));
        var sensorDetails = new Label { FontSize = 12, LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1, Opacity = 0.8 };
        sensorDetails.SetBinding(Label.TextProperty,
            $"{nameof(_viewModel.SelectedSensor)}.{nameof(HistorySensorOption.Details)}");
        var chartTitle = new VerticalStackLayout { Spacing = 2,
            Children = { sensorName, sensorDetails } };
        var exportGraph = new Button { Text = "⇩", FontSize = 17, Padding = 0, WidthRequest = 36,
            HeightRequest = 34, MinimumHeightRequest = 34, HorizontalOptions = LayoutOptions.End };
        SemanticProperties.SetDescription(exportGraph, "Save this sensor graph as an image");
        exportGraph.Clicked += async (_, _) =>
        {
            var group = _viewModel.ChartGroups.FirstOrDefault();
            if (group is null) return;
            exportGraph.IsEnabled = false;
            try
            {
                var result = await _graphImageExport.GenerateAndSaveAsync(new GraphImageExportRequest(
                    _viewModel.ChartTitle, _viewModel.ChartRangeLabel, _viewModel.SelectedRange.Duration,
                    _viewModel.ChartRangeEnd, group.Unit, [], group.Series, true, false, false,
                    group.Series.Count == 1 ? _viewModel.Latest : null));
                await Navigation.PushAsync(new ExportPreviewPage(result, _graphImageExport));
            }
            catch (Exception exception) { await DisplayAlertAsync("Could not save graph", exception.Message, "OK"); }
            finally { exportGraph.IsEnabled = true; }
        };
        var chartHeading = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(52)) }, ColumnSpacing = 12,
            Children = { chartTitle, exportGraph }
        };
        chartHeading.SetColumn(exportGraph, 1);

        var charts = new VerticalStackLayout { Spacing = 10 };
        BindableLayout.SetItemsSource(charts, _viewModel.ChartGroups);
        BindableLayout.SetItemTemplate(charts, new DataTemplate(() =>
        {
            var chart = new SensorChart { ShowAverage = true, ShowMinimum = false, ShowMaximum = false };
            chart.SetBinding(SensorChart.ComparisonSeriesProperty, nameof(SensorGraphGroup.Series));
            chart.SetBinding(SensorChart.UnitProperty, nameof(SensorGraphGroup.Unit));
            chart.SetBinding(SensorChart.RangeDurationProperty, new Binding(
                $"BindingContext.{nameof(_viewModel.SelectedRange)}.{nameof(HistoryRangeOption.Duration)}", source: this));
            chart.SetBinding(SensorChart.RangeEndProperty, new Binding(
                $"BindingContext.{nameof(_viewModel.ChartRangeEnd)}", source: this));
            chart.SetBinding(SensorChart.IsLoadingProperty, new Binding(
                $"BindingContext.{nameof(_viewModel.IsChartLoading)}", source: this));
            chart.SetBinding(SensorChart.ErrorMessageProperty, new Binding(
                $"BindingContext.{nameof(_viewModel.ChartErrorMessage)}", source: this));
            return chart;
        }));

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

        var syncProgress = new ProgressBar { HeightRequest = 5 };
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
                filters, Card(comparisonStrip, 8), chartHeading, charts, Card(statistics, 14), detailHeading, status, error,
                syncProgress, syncProgressText, loading
            }
        };
    }

    private async Task ShowComparisonPickerAsync()
    {
        var primary = _viewModel.SelectedSensor;
        var available = _viewModel.AvailableSensors.Where(x =>
            _viewModel.SelectedSensors.All(y => y.Id != x.Id) &&
            (primary is null || GraphCompatibility.AreCompatible(primary.Type, primary.Unit, x.Type, x.Unit))).ToArray();
        if (available.Length == 0) return;
        var labels = available.Select(x => x.DisplayName).ToArray();
        var choice = await DisplayActionSheetAsync("Add sensor comparison", "Cancel", null, labels);
        var selected = available.FirstOrDefault(x => x.DisplayName == choice);
        if (selected is not null) await _viewModel.AddComparisonAsync(selected);
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
        border.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EAF1F7"), Color.FromArgb("#0B1A2C"));
        border.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#C4D2DF"), Color.FromArgb("#1D3248"));
        return border;
    }
}
