namespace PCMonitor.Service.Diagnostics;

public sealed class WindowsDiagnosticsOptions
{
    public const string SectionName = "WindowsDiagnostics";

    public bool Enabled { get; set; } = true;
    public int ScanIntervalMinutes { get; set; } = 20;
    public int InitialLookbackHours { get; set; } = 24;
    public int RetentionDays { get; set; } = 30;
    public int MaximumStorageMegabytes { get; set; } = 25;
    public int MaximumEventsPerScan { get; set; } = 1000;
    public int DefaultPageSize { get; set; } = 100;
    public int MaximumPageSize { get; set; } = 500;
    public string? StoreFilePath { get; set; }
    public string[] Channels { get; set; } = ["System"];
    public string[] Providers { get; set; } =
    [
        "Microsoft-Windows-WHEA-Logger", "Microsoft-Windows-Kernel-Power",
        "Microsoft-Windows-Kernel-PnP", "Microsoft-Windows-BugCheck", "Disk",
        "storahci", "stornvme", "volmgr", "Display", "nvlddmkm"
    ];
}
