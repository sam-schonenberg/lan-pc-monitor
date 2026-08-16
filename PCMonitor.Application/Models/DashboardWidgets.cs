using System.Text.Json;

namespace PCMonitor.Application.Models;

public enum DashboardWidgetType { CurrentValue, Graph, Alerts }
public enum DashboardWidgetWidth { Half = 1, Full = 2 }

public interface IDashboardWidgetConfiguration;

public sealed record CurrentValueWidgetConfiguration(
    string? SensorId = null,
    int DecimalPlaces = 1,
    bool ShowMinimumAndMaximum = false) : IDashboardWidgetConfiguration;

public sealed record GraphWidgetConfiguration(
    string? SensorId = null,
    TimeSpan? Range = null,
    bool ShowAverage = true,
    bool ShowMinimum = true,
    bool ShowMaximum = true) : IDashboardWidgetConfiguration
{
    public TimeSpan EffectiveRange => Range ?? TimeSpan.FromHours(1);
}

public sealed record AlertWidgetConfiguration(
    string? SensorId = null,
    string? MinimumSeverity = null,
    int MaximumItems = 5) : IDashboardWidgetConfiguration;

public sealed record DashboardWidgetDefinition(
    Guid Id,
    DashboardWidgetType Type,
    string Title,
    int Position,
    DashboardWidgetWidth Width,
    bool IsEnabled,
    IDashboardWidgetConfiguration Configuration);

public sealed record DashboardWidgetDescriptor(
    DashboardWidgetType Type,
    string DisplayName,
    string Description,
    DashboardWidgetWidth DefaultWidth);

public static class DashboardWidgetCatalog
{
    public static IReadOnlyList<DashboardWidgetDescriptor> Available { get; } =
    [
        new(DashboardWidgetType.CurrentValue, "Current value", "The latest reading from one sensor.", DashboardWidgetWidth.Half),
        new(DashboardWidgetType.Graph, "Graph", "A live or historical sensor timeline.", DashboardWidgetWidth.Full),
        new(DashboardWidgetType.Alerts, "Alerts", "Recent alerts with optional sensor and severity filters.", DashboardWidgetWidth.Full)
    ];

    public static DashboardWidgetDefinition Create(DashboardWidgetType type, int position) => new(
        Guid.NewGuid(), type, Available.Single(x => x.Type == type).DisplayName, Math.Max(0, position),
        Available.Single(x => x.Type == type).DefaultWidth, true, type switch
        {
            DashboardWidgetType.CurrentValue => new CurrentValueWidgetConfiguration(),
            DashboardWidgetType.Graph => new GraphWidgetConfiguration(),
            DashboardWidgetType.Alerts => new AlertWidgetConfiguration(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        });
}

public static class DashboardWidgetConfigurationCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(DashboardWidgetType type, IDashboardWidgetConfiguration configuration)
    {
        ValidateType(type, configuration);
        return JsonSerializer.Serialize(configuration, configuration.GetType(), JsonOptions);
    }

    public static IDashboardWidgetConfiguration Deserialize(DashboardWidgetType type, string json) => type switch
    {
        DashboardWidgetType.CurrentValue => JsonSerializer.Deserialize<CurrentValueWidgetConfiguration>(json, JsonOptions)
            ?? new CurrentValueWidgetConfiguration(),
        DashboardWidgetType.Graph => JsonSerializer.Deserialize<GraphWidgetConfiguration>(json, JsonOptions)
            ?? new GraphWidgetConfiguration(),
        DashboardWidgetType.Alerts => JsonSerializer.Deserialize<AlertWidgetConfiguration>(json, JsonOptions)
            ?? new AlertWidgetConfiguration(),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static void Validate(DashboardWidgetDefinition widget)
    {
        if (widget.Id == Guid.Empty) throw new ArgumentException("A widget must have an identity.", nameof(widget));
        if (widget.Position < 0) throw new ArgumentException("Widget position cannot be negative.", nameof(widget));
        if (!Enum.IsDefined(widget.Width)) throw new ArgumentException("Widget width is invalid.", nameof(widget));
        if (string.IsNullOrWhiteSpace(widget.Title)) throw new ArgumentException("Widget title is required.", nameof(widget));
        ValidateType(widget.Type, widget.Configuration);
        if (widget.Configuration is CurrentValueWidgetConfiguration current && current.DecimalPlaces is < 0 or > 4)
            throw new ArgumentException("Current-value precision must be between zero and four.", nameof(widget));
        if (widget.Configuration is GraphWidgetConfiguration graph && graph.EffectiveRange <= TimeSpan.Zero)
            throw new ArgumentException("Graph range must be positive.", nameof(widget));
        if (widget.Configuration is AlertWidgetConfiguration alerts && alerts.MaximumItems is < 1 or > 100)
            throw new ArgumentException("Alert count must be between one and 100.", nameof(widget));
    }

    private static void ValidateType(DashboardWidgetType type, IDashboardWidgetConfiguration configuration)
    {
        var matches = type switch
        {
            DashboardWidgetType.CurrentValue => configuration is CurrentValueWidgetConfiguration,
            DashboardWidgetType.Graph => configuration is GraphWidgetConfiguration,
            DashboardWidgetType.Alerts => configuration is AlertWidgetConfiguration,
            _ => false
        };
        if (!matches) throw new ArgumentException($"Configuration does not match widget type {type}.", nameof(configuration));
    }
}
