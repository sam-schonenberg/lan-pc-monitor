namespace PCMonitor.Service.Diagnostics;

public enum WindowsDiagnosticSeverity { Error, Critical }

public sealed record WindowsDiagnosticEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    string Channel,
    string Provider,
    int EventId,
    byte Version,
    byte WindowsLevel,
    long RecordId,
    WindowsDiagnosticSeverity Severity,
    string Category,
    int OccurrenceCount = 1,
    string Title = "",
    string Summary = "");

public sealed record WindowsEventCheckpoint(string Channel, long RecordId);

public sealed record WindowsEventBatch(
    string Channel,
    IReadOnlyList<WindowsDiagnosticEvent> Events,
    WindowsEventCheckpoint? Checkpoint,
    bool HasMore);

public sealed record WindowsDiagnosticEventsResponse(
    long? FromSequence,
    long? ToSequence,
    bool HasMore,
    long? PreviousSequence,
    IReadOnlyList<WindowsDiagnosticEvent> Events);

public sealed record WindowsDiagnosticsStatusResponse(
    bool Enabled,
    int ScanIntervalMinutes,
    int RetentionDays,
    int MaximumStorageMegabytes,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string> Providers,
    int StoredEventCount,
    long? OldestSequence,
    long? NewestSequence,
    DateTimeOffset? LastSuccessfulScan,
    string? LastError);
