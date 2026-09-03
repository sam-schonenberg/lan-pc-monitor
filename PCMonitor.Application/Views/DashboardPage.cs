using PCMonitor.Application.Controls;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.Services.Export;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Views;

public sealed class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly DashboardWidgetRepository _widgetRepository;
    private readonly HistoryRepository _historyRepository;
    private readonly GraphImageExportService _graphImageExport;
    private readonly VerticalStackLayout _widgetRows = new() { Spacing = 12 };
    private readonly Label _empty = new() { HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.75,
        Text = "Your dashboard is empty.\n\nAdd sensors, graphs, or alerts for quick access to the PC information you care about most." };
    private readonly VerticalStackLayout _emptyState;
    private readonly Button _editButton = new() { Text = "Edit", MinimumHeightRequest = 44 };

    public DashboardPage(DashboardViewModel viewModel, DashboardWidgetRepository widgetRepository,
        HistoryRepository historyRepository, GraphImageExportService graphImageExport)
    {
        Title = "Dashboard"; BindingContext = _viewModel = viewModel;
        _widgetRepository = widgetRepository; _historyRepository = historyRepository; _graphImageExport = graphImageExport;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F5F8FC"), Color.FromArgb("#071426"));

        _editButton.Clicked += (_, _) => ToggleEditMode();
        SemanticProperties.SetDescription(_editButton, "Edit dashboard widgets");

        var machine = new Label { FontSize = 20, FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
        machine.SetBinding(Label.TextProperty, nameof(viewModel.MachineName));
        var connection = new Label { FontSize = 13, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.End, VerticalTextAlignment = TextAlignment.Center };
        connection.SetBinding(Label.TextProperty, nameof(viewModel.ConnectionState));
        var updated = new Label { FontSize = 12, Opacity = 0.7 };
        updated.SetBinding(Label.TextProperty, nameof(viewModel.LastUpdateText));
        var header = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            ColumnSpacing = 16, RowSpacing = 5,
            Children = { machine, updated, _editButton, connection }
        };
        header.SetRow(updated, 1);
        header.SetColumn(_editButton, 1);
        header.SetColumn(connection, 1);
        header.SetRow(connection, 1);
        var error = new Label { FontSize = 12, TextColor = Colors.DarkOrange };
        error.SetBinding(Label.TextProperty, nameof(viewModel.ErrorMessage));

        var add = new Button { Text = "+ Add widget", HorizontalOptions = LayoutOptions.Fill };
        add.SetBinding(IsVisibleProperty, nameof(viewModel.IsEditMode));
        add.Clicked += (_, _) => ShowAddWidget();
        SemanticProperties.SetDescription(add, "Add a dashboard widget");
        var emptyAdd = new Button { Text = "+ Add your first widget", HorizontalOptions = LayoutOptions.Center };
        emptyAdd.Clicked += (_, _) => ShowAddWidget();
        _emptyState = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(20, 32), Children = { _empty, emptyAdd } };

        var body = new VerticalStackLayout { Padding = new Thickness(18, 14, 18, 18), Spacing = 12,
            Children = { header, error, _widgetRows, _emptyState, add } };
        var refresh = new RefreshView { Content = new ScrollView { Content = body } };
        refresh.SetBinding(RefreshView.CommandProperty, nameof(viewModel.RefreshCommand));
        refresh.SetBinding(RefreshView.IsRefreshingProperty, nameof(viewModel.IsRefreshing));
        Content = refresh;

        viewModel.LayoutChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RebuildLayout);
        viewModel.AddWidgetRequested += (_, _) => ShowAddWidget();
        viewModel.EditWidgetRequested += (_, widget) => ShowEditor(widget.Definition);
    }

    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); }

    private void ToggleEditMode()
    {
        if (_viewModel.IsEditMode) _viewModel.ExitEditModeCommand.Execute(null);
        else _viewModel.EnterEditModeCommand.Execute(null);
        _editButton.Text = _viewModel.IsEditMode ? "Done" : "Edit";
    }

    private void RebuildLayout()
    {
        _widgetRows.Clear();
        var visible = _viewModel.Widgets.Where(x => DashboardWidgetPresentation.ShouldRender(x.Definition, _viewModel.IsEditMode)).ToArray();
        foreach (var row in DashboardWidgetLayout.Pack(visible, x => x.Width))
        {
            if (row.IsFullWidth) _widgetRows.Add(Host(row.First));
            else
            {
                var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
                grid.Add(Host(row.First), 0);
                if (row.Second is not null) grid.Add(Host(row.Second), 1);
                _widgetRows.Add(grid);
            }
        }
        _emptyState.IsVisible = visible.Length == 0;
    }

    private DashboardWidgetHost Host(DashboardWidgetViewModelBase widget) => new(widget,
        item => { ShowEditor(item.Definition); return Task.CompletedTask; },
        (item, direction) => _viewModel.MoveWidgetAsync(item, direction),
        item => _viewModel.ToggleWidgetAsync(item), ConfirmDeleteAsync, AddGraphComparisonAsync, RemoveGraphComparisonAsync,
        ExportGraphAsync);

    private async Task ExportGraphAsync(GraphWidgetViewModel graph)
    {
        try
        {
            var export = await _graphImageExport.GenerateAndSaveAsync(new GraphImageExportRequest(graph.Title, graph.RangeLabel,
                graph.Range, graph.RangeEnd, graph.Unit, graph.Points, graph.ComparisonSeries, graph.ShowAverage,
                graph.ShowMinimum, graph.ShowMaximum, graph.CurrentValue, _viewModel.MachineName));
            await Navigation.PushAsync(new ExportPreviewPage(export, _graphImageExport));
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Could not save graph", exception.Message, "OK");
        }
    }

    private async Task AddGraphComparisonAsync(GraphWidgetViewModel graph)
    {
        var sensors = await _historyRepository.GetSensorOptionsAsync();
        var used = graph.ComparisonSensorIds.Append(graph.SensorId ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var compatible = sensors.Where(x => !used.Contains(x.SensorId) &&
            GraphCompatibility.AreCompatible(graph.SensorType, graph.Unit, x.SensorType, x.Unit))
            .OrderBy(x => x.Hardware).ThenBy(x => x.SensorName).ToArray();
        if (compatible.Length == 0)
        {
            await DisplayAlertAsync("No compatible sensors", "There are no other sensors with this measurement type and unit.", "OK");
            return;
        }
        var labels = compatible.Select(x => SensorDisplayText.PickerLabel(x.Hardware, x.SensorName, x.SensorType, x.Unit)).ToArray();
        var choice = await DisplayActionSheetAsync("Add compatible sensor", "Cancel", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        var config = (GraphWidgetConfiguration)graph.Definition.Configuration;
        await _widgetRepository.SaveAsync(graph.Definition with
        { Configuration = config with { ComparisonSensorIds = config.EffectiveComparisonSensorIds.Append(compatible[index].SensorId).ToArray() } });
        await _viewModel.ReloadWidgetsAsync();
    }

    private async Task RemoveGraphComparisonAsync(GraphWidgetViewModel graph, string sensorId)
    {
        var config = (GraphWidgetConfiguration)graph.Definition.Configuration;
        await _widgetRepository.SaveAsync(graph.Definition with
        { Configuration = config with { ComparisonSensorIds = config.EffectiveComparisonSensorIds.Where(x => x != sensorId).ToArray() } });
        await _viewModel.ReloadWidgetsAsync();
    }

    private async Task ConfirmDeleteAsync(DashboardWidgetViewModelBase widget)
    {
        if (await DisplayAlertAsync("Delete widget?", $"Delete “{widget.Title}” from the Dashboard?", "Delete", "Cancel"))
            await _viewModel.DeleteWidgetAsync(widget);
    }

    private async void ShowAddWidget()
    {
        var page = new AddDashboardWidgetPage(type => new DashboardWidgetEditorPage(
            DashboardWidgetCatalog.Create(type, _viewModel.Widgets.Count), true, _widgetRepository,
            _historyRepository, SaveCompletedAsync));
        await Navigation.PushModalAsync(new NavigationPage(page));
    }

    private async void ShowEditor(DashboardWidgetDefinition definition) =>
        await Navigation.PushModalAsync(new NavigationPage(new DashboardWidgetEditorPage(definition, false,
            _widgetRepository, _historyRepository, SaveCompletedAsync)));

    private async Task SaveCompletedAsync()
    {
        await Navigation.PopModalAsync();
        await _viewModel.ReloadWidgetsAsync();
    }
}
