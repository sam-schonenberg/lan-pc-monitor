namespace PCMonitor.Service.Diagnostics;

public static class WindowsEventClassifier
{
    public static WindowsDiagnosticSeverity? Severity(byte level) => level switch
    {
        1 => WindowsDiagnosticSeverity.Critical,
        2 => WindowsDiagnosticSeverity.Error,
        _ => null
    };

    public static string Category(string provider, int eventId) => provider.ToLowerInvariant() switch
    {
        "microsoft-windows-whea-logger" => "hardware-error",
        "microsoft-windows-kernel-power" when eventId == 41 => "unexpected-shutdown",
        "microsoft-windows-kernel-power" => "power",
        "microsoft-windows-kernel-pnp" => "device",
        "microsoft-windows-bugcheck" => "system-crash",
        "disk" or "storahci" or "stornvme" or "volmgr" => "storage",
        "display" or "nvlddmkm" => "display",
        _ => "windows"
    };

    public static WindowsDiagnosticEvent Enrich(WindowsDiagnosticEvent item)
    {
        var description = Describe(item.Provider, item.EventId);
        return item with
        {
            Category = string.IsNullOrWhiteSpace(item.Category)
                ? Category(item.Provider, item.EventId) : item.Category,
            Title = description.Title,
            Summary = description.Summary
        };
    }

    public static WindowsEventDescription Describe(string provider, int eventId) =>
        provider.ToLowerInvariant() switch
        {
            "microsoft-windows-whea-logger" => new("Hardware error",
                "Windows Hardware Error Architecture reported a hardware-related problem. The Event ID identifies the specific WHEA report type."),
            "microsoft-windows-kernel-power" when eventId == 41 => new("Unexpected shutdown",
                "Windows detected that the PC restarted without completing a normal shutdown. A crash, power loss, or forced reset may have caused it."),
            "microsoft-windows-kernel-power" => new("Power management error",
                "Windows reported a problem while managing system power, startup, shutdown, sleep, or resume."),
            "microsoft-windows-kernel-pnp" => new("Device or driver error",
                "Windows Plug and Play reported a problem configuring, starting, or communicating with a device or its driver."),
            "microsoft-windows-bugcheck" => new("Windows system crash",
                "Windows recorded a bug check, commonly known as a blue-screen crash. The Event ID and related crash data can help identify the cause."),
            "disk" => new("Disk error",
                "Windows reported an error while accessing a disk. This can indicate an I/O failure, connection problem, or storage-device issue."),
            "storahci" => new("SATA controller error",
                "The Windows SATA storage driver reported a controller, command, or device communication problem."),
            "stornvme" => new("NVMe controller error",
                "The Windows NVMe storage driver reported a controller, command, or device communication problem."),
            "volmgr" => new("Volume manager error",
                "Windows reported a problem managing a storage volume or writing information associated with a system crash."),
            "display" => new("Display driver error",
                "Windows reported a problem with the display driver or graphics device."),
            "nvlddmkm" => new("NVIDIA display driver error",
                "The NVIDIA Windows display driver reported a graphics-driver or GPU communication problem."),
            _ => new("Windows system error",
                $"Windows reported an error from {provider}. The provider and Event ID identify the event type.")
        };
}

public sealed record WindowsEventDescription(string Title, string Summary);
