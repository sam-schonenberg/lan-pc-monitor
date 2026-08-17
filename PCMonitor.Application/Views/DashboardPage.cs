using PCMonitor.Application.Controls;
using PCMonitor.Application.Models;
using PCMonitor.Application.Services.Storage;
using PCMonitor.Application.ViewModels;

namespace PCMonitor.Application.Views;

public sealed class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _viewModel;
    private readonly DashboardWidgetRepository _widgetRepository;
    private readonly HistoryRepository _historyRepository;
    private readonly VerticalStackLayout _widgetRows = new() { Spacing = 12 };
    private readonly Label _empty = new() { HorizontalTextAlignment = TextAlignment.Center, Opacity = 0.75,
        Text = "Your dashboard is empty.\n\nAdd sensors, graphs, or alerts for quick access to the PC information you care about most." };
    private readonly VerticalStackLayout _emptyState;
    private readonly Button _editButton = new() { Text = "Edit", MinimumHeightRequest = 44 };

    public DashboardPage(DashboardViewModel viewModel, DashboardWidgetRepository widgetRepository,
        HistoryRepository historyRepository)
    {
        Title = "Dashboard"; BindingContext = _viewModel = viewModel;
        _widgetRepository = widgetRepository; _historyRepository = historyRepository;
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F4F7FB"), Color.FromArgb("#141414"));

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
        item => _viewModel.ToggleWidgetAsync(item), ConfirmDeleteAsync);

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
