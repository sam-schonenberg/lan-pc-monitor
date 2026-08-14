using Microsoft.Extensions.Options;

namespace PCMonitor.Service.Alerts;

public sealed class AlertStore(
    IOptions<AlertOptions> options,
    TimeProvider timeProvider,
    LiveEventHub events)
{
    private readonly Lock _sync = new();
    private readonly List<MonitorAlert> _alerts = [];
    private readonly TimeSpan _retention = TimeSpan.FromHours(
        options.Value.RetentionHours > 0 && double.IsFinite(options.Value.RetentionHours)
            ? options.Value.RetentionHours : 24);

    public void Add(MonitorAlert alert)
    {
        lock (_sync)
        {
            _alerts.Add(alert);
            _alerts.RemoveAll(item => item.Timestamp < timeProvider.GetUtcNow() - _retention);
        }
        events.Publish(new LiveEventEnvelope("alert", alert));
    }

    public AlertHistoryResponse Query(DateTimeOffset? from, AlertSeverity? severity)
    {
        MonitorAlert[] result;
        lock (_sync)
        {
            result = _alerts
                .Where(alert => from is null || alert.Timestamp > from.Value)
                .Where(alert => severity is null || alert.Severity == severity)
                .OrderBy(alert => alert.Timestamp)
                .ToArray();
        }
        return new(result.FirstOrDefault()?.Timestamp, result.LastOrDefault()?.Timestamp, result);
    }
}
