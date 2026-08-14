using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;

namespace PCMonitor.Service.Alerts;

public sealed class AlertEvaluator
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SensorAlertState> _states = new(StringComparer.Ordinal);
    private readonly AlertStore _store;
    private readonly ILogger<AlertEvaluator> _logger;
    private readonly AlertOptions _options;
    private DateTimeOffset? _lastEvaluation;

    public AlertEvaluator(IOptions<AlertOptions> options, AlertStore store, ILogger<AlertEvaluator> logger)
    {
        _options = Normalize(options.Value, logger);
        _store = store;
        _logger = logger;
    }

    public void Process(SensorSnapshot snapshot)
    {
        if (!_options.Enabled) return;
        List<MonitorAlert> raised = [];
        lock (_sync)
        {
            if (_lastEvaluation is { } last &&
                snapshot.Timestamp - last < TimeSpan.FromSeconds(_options.EvaluationIntervalSeconds)) return;
            _lastEvaluation = snapshot.Timestamp;

            foreach (var sensor in snapshot.Sensors.Where(sensor =>
                         sensor.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase) && sensor.Value is not null))
            {
                EvaluateSensor(sensor, snapshot.Timestamp, raised);
            }
        }

        foreach (var alert in raised)
        {
            _store.Add(alert);
            try
            {
                _logger.LogWarning("{Severity} alert raised for {Sensor}: {Value}{Unit}",
                    alert.Severity, alert.SensorName, alert.Value, alert.Unit);
            }
            catch
            {
                // A failing logging provider must not affect monitoring or alert delivery.
            }
        }
    }

    private void EvaluateSensor(SensorReading sensor, DateTimeOffset timestamp, List<MonitorAlert> raised)
    {
        var value = sensor.Value!.Value;
        if (!float.IsFinite(value)) return;
        if (!_states.TryGetValue(sensor.Id, out var state))
        {
            state = new SensorAlertState();
            _states[sensor.Id] = state;
        }

        if (value < _options.Temperature.ResetBelowCelsius)
        {
            state.Active = null;
            state.Candidate = null;
            return;
        }

        var target = value >= _options.Temperature.CriticalThresholdCelsius
            ? AlertSeverity.Critical
            : value >= _options.Temperature.WarningThresholdCelsius ? AlertSeverity.Warning : (AlertSeverity?)null;

        if (state.Active == AlertSeverity.Critical ||
            state.Active == AlertSeverity.Warning && target != AlertSeverity.Critical) return;
        if (target is null)
        {
            state.Candidate = null;
            return;
        }

        if (state.Candidate != target)
        {
            state.Candidate = target;
            state.CandidateSince = timestamp;
        }

        if (timestamp - state.CandidateSince < TimeSpan.FromSeconds(_options.Temperature.MinimumDurationSeconds)) return;

        var threshold = target == AlertSeverity.Critical
            ? _options.Temperature.CriticalThresholdCelsius
            : _options.Temperature.WarningThresholdCelsius;
        state.Active = target;
        state.Candidate = null;
        raised.Add(new MonitorAlert(
            Guid.NewGuid(), timestamp, target.Value, sensor.Id, sensor.Hardware, sensor.Name, sensor.Type,
            value, threshold, sensor.Unit,
            $"{sensor.Name} temperature reached {value:0.#}{sensor.Unit ?? "°C"}."));
    }

    private static AlertOptions Normalize(AlertOptions source, ILogger logger)
    {
        if (source.EvaluationIntervalSeconds <= 0 || source.RetentionHours <= 0 ||
            source.Temperature.MinimumDurationSeconds < 0 ||
            source.Temperature.ResetBelowCelsius >= source.Temperature.WarningThresholdCelsius ||
            source.Temperature.WarningThresholdCelsius >= source.Temperature.CriticalThresholdCelsius)
        {
            try { logger.LogWarning("Invalid alert configuration; using safe defaults"); } catch { }
            return new AlertOptions { Enabled = source.Enabled };
        }
        return source;
    }

    private sealed class SensorAlertState
    {
        public AlertSeverity? Active { get; set; }
        public AlertSeverity? Candidate { get; set; }
        public DateTimeOffset CandidateSince { get; set; }
    }
}
