using System.Text.Json;
namespace PCMonitor.Application.Models.Api;

public sealed record ApiErrorDto(string Error);
public sealed record ServiceStatusDto(string Status, string Service, string MachineName, DateTimeOffset Timestamp,
    string? Version = null, string? ApiVersion = null, IReadOnlyList<string>? Capabilities = null);
public sealed record SensorReadingDto(string Id, string Hardware, string Name, string Type, float? Value, string? Unit);
public sealed record SensorSnapshotDto(DateTimeOffset Timestamp, IReadOnlyList<SensorReadingDto> Sensors);
public sealed record HistoricalSensorReadingDto(string Id, string Hardware, string Name, string Type, string? Unit, float Min, float Max, double Average, long SampleCount);
public sealed record HistoricalProcessSummaryDto(string Name, double AverageCpuPercent, double MaxCpuPercent, long SampleCount);
public sealed record HistoricalSnapshotDto(DateTimeOffset StartTime, DateTimeOffset EndTime, IReadOnlyList<HistoricalSensorReadingDto> Sensors, Guid? SessionId, HistoricalProcessSummaryDto? DominantProcess);
public sealed record HistoricalHistoryResponseDto(DateTimeOffset? From, DateTimeOffset? To, int ResolutionSeconds, IReadOnlyList<HistoricalSnapshotDto> Snapshots);
public sealed record SensorCatalogEntryDto(int Id, string Key, string Hardware, string Name, string Type, string? Unit);
public sealed record SensorCatalogResponseDto(string Version, IReadOnlyList<SensorCatalogEntryDto> Sensors);
public sealed record CompactHistoricalSensorDto(int SensorId, double Min, double Max, double Avg, long Count);
public sealed record CompactHistoricalSnapshotDto(long Sequence, DateTimeOffset StartTime, DateTimeOffset EndTime,
    IReadOnlyList<CompactHistoricalSensorDto> Sensors, Guid? SessionId, HistoricalProcessSummaryDto? DominantProcess);
public sealed record CompactHistoryResponseDto(string CatalogVersion, string Resolution, long? FromSequence,
    long? ToSequence, bool HasMore, long? NextSequence, IReadOnlyList<CompactHistoricalSnapshotDto> Snapshots,
    long? AvailableToSequence = null, int RemainingBuckets = 0, long? PreviousSequence = null);
public sealed record HistorySequenceRangeDto(long FromSequence, long ToSequence, int BucketCount);
public sealed record HistoryManifestResponseDto(Guid StreamId, string CatalogVersion, long? OldestSequence,
    long? NewestSequence, int BucketCount, DateTimeOffset? OldestTimestamp, DateTimeOffset? NewestTimestamp,
    int ResolutionSeconds, double RetentionHours, IReadOnlyList<HistorySequenceRangeDto> SequenceRanges,
    DateTimeOffset GeneratedAt);
public sealed record MonitorAlertDto(Guid Id, DateTimeOffset Timestamp, string Severity, string SensorId, string Hardware, string SensorName, string SensorType, double Value, double Threshold, string? Unit, string Message);
public sealed record AlertHistoryResponseDto(DateTimeOffset? From, DateTimeOffset? To, IReadOnlyList<MonitorAlertDto> Alerts);
public sealed record AlertMetricStatusDto(string Category, string Direction, string SensorId, string Hardware,
    string SensorName, string SensorType, double Value, string? Unit, double WarningThreshold,
    double CriticalThreshold, string State, double Progress, double DistanceToCritical,
    double? PendingSecondsRemaining, string? Condition);
public sealed record AlertStatusResponseDto(DateTimeOffset Timestamp, IReadOnlyList<AlertMetricStatusDto> Sensors);
public sealed record CustomAlertRuleDto(Guid Id, string Name, string SensorId, string Direction,
    double Threshold, double ResetThreshold, double MinimumDurationSeconds, string Severity,
    bool Enabled, bool NotificationsEnabled);
public sealed record CustomAlertRuleRequestDto(string Name, string SensorId, string Direction,
    double Threshold, double ResetThreshold, double MinimumDurationSeconds, string Severity,
    bool Enabled, bool NotificationsEnabled);
public sealed record CustomAlertRulesResponseDto(IReadOnlyList<CustomAlertRuleDto> Rules);
public sealed record DeviceRegistrationRequestDto(string InstallationId, string SendSecret, string Platform,
    string? DeviceName);
public sealed record DeviceRegistrationResponseDto(string InstallationId, string Platform, string? DeviceName,
    DateTimeOffset UpdatedAt);
public sealed record NotificationStatusDto(bool Enabled, bool Configured, int RegisteredDevices,
    string MinimumSeverity);
public sealed record WindowsDiagnosticEventDto(long Sequence, DateTimeOffset Timestamp, string Channel,
    string Provider, int EventId, byte Version, byte WindowsLevel, long RecordId, string Severity,
    string Category, int OccurrenceCount, string Title, string Summary);
public sealed record WindowsDiagnosticEventsResponseDto(long? FromSequence, long? ToSequence, bool HasMore,
    long? PreviousSequence, IReadOnlyList<WindowsDiagnosticEventDto> Events);
public sealed record WindowsDiagnosticsStatusResponseDto(bool Enabled, int ScanIntervalMinutes, int RetentionDays,
    int MaximumStorageMegabytes, IReadOnlyList<string> Channels, IReadOnlyList<string> Providers,
    int StoredEventCount, long? OldestSequence, long? NewestSequence, DateTimeOffset? LastSuccessfulScan,
    string? LastError);
public sealed record SessionStatusDto(string State, JsonElement? Session);
public sealed record LiveEventEnvelopeDto(string Type, JsonElement Data);
