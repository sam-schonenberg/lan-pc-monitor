namespace PCMonitor.Application.Models;

public sealed record DashboardWidgetRow<T>(T First, T? Second, bool IsFullWidth) where T : class;

public static class DashboardWidgetLayout
{
    public static IReadOnlyList<DashboardWidgetRow<T>> Pack<T>(IEnumerable<T> widgets,
        Func<T, DashboardWidgetWidth> widthSelector) where T : class
    {
        var result = new List<DashboardWidgetRow<T>>();
        T? pendingHalf = null;
        foreach (var widget in widgets)
        {
            if (widthSelector(widget) == DashboardWidgetWidth.Full)
            {
                if (pendingHalf is not null) { result.Add(new(pendingHalf, null, false)); pendingHalf = null; }
                result.Add(new(widget, null, true));
            }
            else if (pendingHalf is null) pendingHalf = widget;
            else { result.Add(new(pendingHalf, widget, false)); pendingHalf = null; }
        }
        if (pendingHalf is not null) result.Add(new(pendingHalf, null, false));
        return result;
    }
}

public sealed record DashboardResolvedValue(double? Value, bool IsLive, DateTimeOffset? Timestamp);

public static class DashboardWidgetPresentation
{
    public static bool ShouldRender(DashboardWidgetDefinition widget, bool isEditMode) => widget.IsEnabled || isEditMode;

    public static string ResolveTitle(DashboardWidgetDefinition definition, string? sensorName,
        string? sensorType = null, string? hardware = null)
    {
        var catalogDefault = DashboardWidgetCatalog.Available.Single(x => x.Type == definition.Type).DisplayName;
        if (!string.IsNullOrWhiteSpace(definition.Title) && definition.Title != catalogDefault) return definition.Title;
        if (!string.IsNullOrWhiteSpace(sensorName)) return sensorName;
        return !string.IsNullOrWhiteSpace(sensorType) && !string.IsNullOrWhiteSpace(hardware)
            ? $"{sensorType} — {hardware}" : catalogDefault;
    }

    public static string FormatValue(double? value, string? unit, int precision) => value is null ? "—"
        : $"{value.Value.ToString($"F{Math.Clamp(precision, 0, 4)}")}{(string.IsNullOrWhiteSpace(unit) ? "" : $" {unit}")}";

    public static DashboardResolvedValue ResolveCurrent(double? liveValue, DateTimeOffset? liveTimestamp,
        double? localValue, DateTimeOffset? localTimestamp) => liveValue is not null
        ? new(liveValue, true, liveTimestamp) : new(localValue, false, localTimestamp);
}
