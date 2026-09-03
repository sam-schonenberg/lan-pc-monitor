using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCMonitor.Application.Models.Api;
using PCMonitor.Application.Services.Api;

namespace PCMonitor.Application.ViewModels;

public partial class WindowsDiagnosticsViewModel(MonitorApiClient api) : ObservableObject
{
    private long? _beforeSequence;
    private bool _initialized;
    public ObservableCollection<WindowsDiagnosticEventItem> Events { get; } = [];
    [ObservableProperty] public partial bool IsRefreshing { get; set; }
    [ObservableProperty] public partial bool IsLoadingMore { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Loading Windows diagnostics…";
    [ObservableProperty] public partial string SelectedSeverity { get; set; } = "all";

    [RelayCommand]
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            Events.Clear(); _beforeSequence = null;
            await LoadPageAsync();
        }
        catch (Exception exception) { Status = $"Could not load diagnostics: {exception.Message}"; }
        finally { IsRefreshing = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoadingMore) return;
        IsLoadingMore = true;
        try { await LoadPageAsync(); }
        catch (Exception exception) { Status = $"Could not load older events: {exception.Message}"; }
        finally { IsLoadingMore = false; }
    }

    [RelayCommand]
    private async Task SetSeverityAsync(string severity)
    {
        SelectedSeverity = severity;
        await RefreshAsync();
    }

    private async Task LoadPageAsync()
    {
        var minimum = SelectedSeverity == "critical" ? "critical" : null;
        var result = await api.GetWindowsDiagnosticEventsAsync(_beforeSequence, 50, minimum);
        foreach (var item in result.Events) Events.Add(new(item));
        _beforeSequence = result.PreviousSequence;
        HasMore = result.HasMore;
        Status = Events.Count == 0 ? "No retained events match this filter."
            : $"{Events.Count} event{(Events.Count == 1 ? string.Empty : "s")} loaded";
    }
}

public sealed record WindowsDiagnosticEventItem(WindowsDiagnosticEventDto Event)
{
    public string Title => Event.Title;
    public string Summary => Event.Summary;
    public string Metadata => $"{Event.Provider} · Event {Event.EventId}";
    public string Severity => Event.Severity.ToUpperInvariant();
    public Color SeverityColor => Event.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase)
        ? Color.FromArgb("#DC2626") : Color.FromArgb("#F59E0B");
    public string DisplayTimestamp => Event.Timestamp.ToLocalTime().ToString("g");
}
