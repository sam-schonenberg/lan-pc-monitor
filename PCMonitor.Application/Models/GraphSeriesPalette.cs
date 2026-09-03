namespace PCMonitor.Application.Models;

/// <summary>Data-visualization colors; intentionally independent from the app's brand accent tokens.</summary>
public static class GraphSeriesPalette
{
    public const string DarkAverage = "#5EEAD4";
    public const string DarkBoundary = "#94A3B8";
    public const string LightAverage = "#087F72";
    public const string LightBoundary = "#52657A";

    public static IReadOnlyList<string> DarkSeries { get; } =
    [
        "#5EEAD4", // teal
        "#FBBF24", // amber
        "#A78BFA", // violet
        "#FB7185", // coral
        "#60A5FA", // blue
        "#F472B6", // pink
        "#A3E635", // lime
        "#F97316"  // orange
    ];

    public static IReadOnlyList<string> LightSeries { get; } =
    [
        "#087F72", "#9A6700", "#6D4CC7", "#C43D58",
        "#2563A9", "#B83280", "#527A13", "#C2410C"
    ];
}
