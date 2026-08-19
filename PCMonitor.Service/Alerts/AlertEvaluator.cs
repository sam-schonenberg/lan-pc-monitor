using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;
using PCMonitor.Service.Notifications;

namespace PCMonitor.Service.Alerts;

public sealed class AlertEvaluator
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, SensorAlertState> _states = new(StringComparer.Ordinal);
    private readonly AlertStore _store;
    private readonly ILogger<AlertEvaluator> _logger;
    private readonly AlertOptions _options;
    private readonly INotificationDispatcher _notifications;
    private readonly CustomAlertRuleStore _customRules;
    private DateTimeOffset? _lastEvaluation;

    public AlertEvaluator(IOptions<AlertOptions> options, AlertStore store, INotificationDispatcher notifications,
        CustomAlertRuleStore customRules, ILogger<AlertEvaluator> logger)
    { _options = Normalize(options.Value, logger); _store = store; _logger = logger; _notifications = notifications;
        _customRules = customRules; }

    public void Process(SensorSnapshot snapshot)
    {
        if (!_options.Enabled) return;
        List<RaisedAlert> raised = [];
        lock (_sync)
        {
            if (_lastEvaluation is { } last && snapshot.Timestamp - last < TimeSpan.FromSeconds(_options.EvaluationIntervalSeconds)) return;
            _lastEvaluation = snapshot.Timestamp;
            foreach (var sensor in snapshot.Sensors.Where(x => x.Value is not null && float.IsFinite(x.Value.Value)))
            {
                if (IsPrimaryTemperature(sensor)) EvaluateHigh("temperature", sensor, snapshot.Timestamp,
                    _options.Temperature.WarningThresholdCelsius, _options.Temperature.CriticalThresholdCelsius,
                    _options.Temperature.ResetBelowCelsius, _options.Temperature.MinimumDurationSeconds, raised);
                else if (_options.MemoryPressure.Enabled && IsMemoryPressure(sensor)) EvaluateHigh("memory", sensor, snapshot.Timestamp,
                    _options.MemoryPressure.WarningThreshold, _options.MemoryPressure.CriticalThreshold,
                    _options.MemoryPressure.ResetBelow, _options.MemoryPressure.MinimumDurationSeconds, raised);
                else if (_options.Utilization.Enabled && IsUtilization(sensor)) EvaluateHigh("utilization", sensor, snapshot.Timestamp,
                    _options.Utilization.WarningThreshold, _options.Utilization.CriticalThreshold,
                    _options.Utilization.ResetBelow, _options.Utilization.MinimumDurationSeconds, raised);
                else if (_options.Fan.Enabled && IsMonitoredFan(sensor)) EvaluateFan(sensor, snapshot, raised);
            }
            foreach (var rule in _customRules.GetAll().Where(x => x.Enabled))
            {
                var sensor = snapshot.Sensors.FirstOrDefault(x => x.Id == rule.SensorId && x.Value is not null &&
                    float.IsFinite(x.Value.Value));
                if (sensor is not null) EvaluateCustom(rule, sensor, snapshot.Timestamp, raised);
            }
        }
        foreach (var item in raised)
        {
            var alert = item.Alert;
            _store.Add(alert); if (item.Notify) _notifications.Enqueue(alert);
            try { _logger.LogWarning("{Severity} alert raised for {Sensor}: {Value}{Unit}", alert.Severity,
                alert.SensorName, alert.Value, alert.Unit); } catch { }
        }
    }

    public AlertStatusResponse GetStatus(SensorSnapshot snapshot)
    {
        lock (_sync)
        {
            List<AlertMetricStatus> result = [];
            foreach (var sensor in snapshot.Sensors.Where(x => x.Value is not null && float.IsFinite(x.Value.Value)))
            {
                if (IsPrimaryTemperature(sensor)) result.Add(Status("temperature", "high", sensor, snapshot.Timestamp,
                    _options.Temperature.WarningThresholdCelsius, _options.Temperature.CriticalThresholdCelsius, null));
                else if (_options.MemoryPressure.Enabled && IsMemoryPressure(sensor)) result.Add(Status("memory", "high", sensor,
                    snapshot.Timestamp, _options.MemoryPressure.WarningThreshold, _options.MemoryPressure.CriticalThreshold, null));
                else if (_options.Utilization.Enabled && IsUtilization(sensor)) result.Add(Status("utilization", "high", sensor,
                    snapshot.Timestamp, _options.Utilization.WarningThreshold, _options.Utilization.CriticalThreshold, null));
                else if (_options.Fan.Enabled && IsMonitoredFan(sensor))
                {
                    var hot = FanTemperature(snapshot, sensor);
                    result.Add(Status("fan", "low", sensor, snapshot.Timestamp, _options.Fan.WarningBelowRpm,
                        _options.Fan.CriticalBelowRpm, hot >= _options.Fan.HardwareTemperatureGateCelsius
                            ? $"Hardware temperature is {hot:0.#}°C." : $"Armed above {_options.Fan.HardwareTemperatureGateCelsius:0.#}°C."));
                }
            }
            return new(snapshot.Timestamp, result.OrderBy(x => x.Category).ThenBy(x => x.Hardware).ThenBy(x => x.SensorName).ToArray());
        }
    }

    private void EvaluateHigh(string category, SensorReading sensor, DateTimeOffset timestamp, double warning,
        double critical, double reset, double duration, List<RaisedAlert> raised)
    {
        var value = sensor.Value!.Value;
        AlertSeverity? target = value >= critical ? AlertSeverity.Critical : value >= warning ? AlertSeverity.Warning : null;
        Evaluate(category, sensor, timestamp, target, value < reset, duration,
            target == AlertSeverity.Critical ? critical : warning, raised);
    }

    private void EvaluateFan(SensorReading sensor, SensorSnapshot snapshot, List<RaisedAlert> raised)
    {
        var value = sensor.Value!.Value;
        var hot = FanTemperature(snapshot, sensor) >= _options.Fan.HardwareTemperatureGateCelsius;
        AlertSeverity? target = hot ? value <= _options.Fan.CriticalBelowRpm ? AlertSeverity.Critical
            : value <= _options.Fan.WarningBelowRpm ? AlertSeverity.Warning : null : null;
        // Once raised, a fan alert remains latched until RPM recovers. Cooling below the
        // temperature gate is not proof that a stopped fan started working again.
        Evaluate("fan", sensor, snapshot.Timestamp, target, value > _options.Fan.ResetAboveRpm,
            _options.Fan.MinimumDurationSeconds, target == AlertSeverity.Critical
                ? _options.Fan.CriticalBelowRpm : _options.Fan.WarningBelowRpm, raised);
    }

    private void EvaluateCustom(CustomAlertRule rule, SensorReading sensor, DateTimeOffset timestamp,
        List<RaisedAlert> raised)
    {
        var value = sensor.Value!.Value;
        var triggered = rule.Direction == AlertRuleDirection.Above ? value >= rule.Threshold : value <= rule.Threshold;
        var reset = rule.Direction == AlertRuleDirection.Above ? value < rule.ResetThreshold : value > rule.ResetThreshold;
        Evaluate($"custom:{rule.Id:N}", sensor, timestamp, triggered ? rule.Severity : null, reset,
            rule.MinimumDurationSeconds, rule.Threshold, raised, rule.NotificationsEnabled, rule.Name,
            $"{rule.Name}: {sensor.Name} is {value:0.#}{sensor.Unit}." );
    }

    private void Evaluate(string category, SensorReading sensor, DateTimeOffset timestamp, AlertSeverity? target,
        bool reset, double duration, double threshold, List<RaisedAlert> raised, bool notify = true,
        string? alertName = null, string? message = null)
    {
        var key = Key(category, sensor.Id);
        if (!_states.TryGetValue(key, out var state)) _states[key] = state = new();
        if (reset) { state.Active = null; state.Candidate = null; return; }
        if (state.Active == AlertSeverity.Critical || state.Active == AlertSeverity.Warning && target != AlertSeverity.Critical) return;
        if (target is null) { state.Candidate = null; return; }
        if (state.Candidate != target) { state.Candidate = target; state.CandidateSince = timestamp; }
        if (timestamp - state.CandidateSince < TimeSpan.FromSeconds(duration)) return;
        state.Active = target; state.Candidate = null;
        raised.Add(new(new(Guid.NewGuid(), timestamp, target.Value, sensor.Id, sensor.Hardware,
            alertName is null ? sensor.Name : $"{alertName} · {sensor.Name}", sensor.Type,
            sensor.Value!.Value, threshold, sensor.Unit,
            message ?? $"{sensor.Name} {(category == "fan" ? "fell to" : "reached")} {sensor.Value.Value:0.#}{sensor.Unit}."), notify));
    }

    private AlertMetricStatus Status(string category, string direction, SensorReading sensor, DateTimeOffset timestamp,
        double warning, double critical, string? condition)
    {
        _states.TryGetValue(Key(category, sensor.Id), out var state);
        var value = sensor.Value!.Value;
        var stateName = state?.Active is { } active ? active.ToString().ToLowerInvariant()
            : state?.Candidate is not null ? "pending" : "safe";
        var duration = category switch { "temperature" => _options.Temperature.MinimumDurationSeconds,
            "memory" => _options.MemoryPressure.MinimumDurationSeconds, "utilization" => _options.Utilization.MinimumDurationSeconds,
            _ => _options.Fan.MinimumDurationSeconds };
        double? remaining = state?.Candidate is null ? null : Math.Max(0, duration - (timestamp - state.CandidateSince).TotalSeconds);
        var progress = direction == "high" ? Math.Clamp(value / critical, 0, 1)
            : Math.Clamp((warning - value) / Math.Max(1, warning - critical), 0, 1);
        return new(category, direction, sensor.Id, sensor.Hardware, sensor.Name, sensor.Type, value, sensor.Unit,
            warning, critical, stateName, Math.Round(progress, 3),
            Math.Round(direction == "high" ? critical - value : value - critical, 1), remaining, condition);
    }

    private static bool IsTemperature(SensorReading x) => x.Type.Equals("Temperature", StringComparison.OrdinalIgnoreCase);
    private static bool IsPrimaryTemperature(SensorReading x)
    {
        if (!IsTemperature(x) || x.Name.Contains("Distance to", StringComparison.OrdinalIgnoreCase)) return false;
        var cpu = x.Hardware.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                  x.Hardware.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                  x.Hardware.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
        var gpu = x.Hardware.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
                  x.Hardware.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                  x.Hardware.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        return cpu && (x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                       x.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                       x.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) ||
               gpu && x.Name.Contains("Core", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsFan(SensorReading x) => x.Type.Equals("Fan", StringComparison.OrdinalIgnoreCase);
    private bool IsMonitoredFan(SensorReading x)
    {
        if (!IsFan(x)) return false;
        return _options.Fan.MonitorCpuFans && x.Name.Contains("CPU Fan", StringComparison.OrdinalIgnoreCase) ||
               _options.Fan.MonitorGpuFans && x.Name.Contains("GPU Fan", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsMemoryPressure(SensorReading x) => x.Type.Equals("Load", StringComparison.OrdinalIgnoreCase) &&
        x.Name.Contains("Memory Usage", StringComparison.OrdinalIgnoreCase) &&
        (x.Hardware.Contains("Total Memory", StringComparison.OrdinalIgnoreCase) ||
         x.Hardware.Equals("Memory", StringComparison.OrdinalIgnoreCase));
    private static bool IsUtilization(SensorReading x) => x.Type.Equals("Load", StringComparison.OrdinalIgnoreCase) && !IsMemoryPressure(x) &&
        (x.Name.Contains("Overall CPU", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase) ||
         x.Name.Contains("GPU Core", StringComparison.OrdinalIgnoreCase));
    private static double HardwareTemperature(SensorSnapshot snapshot, string hardware) => snapshot.Sensors
        .Where(x => x.Hardware == hardware && IsActualTemperature(x)).Select(x => (double)x.Value!.Value)
        .DefaultIfEmpty(double.NegativeInfinity).Max();
    private static double FanTemperature(SensorSnapshot snapshot, SensorReading fan)
    {
        if (fan.Name.Contains("CPU Fan", StringComparison.OrdinalIgnoreCase))
            return snapshot.Sensors.Where(x => IsActualTemperature(x) &&
                    (x.Hardware.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                     x.Hardware.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                     x.Hardware.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase) ||
                     x.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)))
                .Select(x => (double)x.Value!.Value).DefaultIfEmpty(double.NegativeInfinity).Max();
        return HardwareTemperature(snapshot, fan.Hardware);
    }
    private static bool IsActualTemperature(SensorReading x) => IsTemperature(x) && x.Value is not null &&
        !x.Name.Contains("Distance to", StringComparison.OrdinalIgnoreCase);
    private static string Key(string category, string sensorId) => $"{category}:{sensorId}";

    private static AlertOptions Normalize(AlertOptions source, ILogger logger)
    {
        var valid = source.EvaluationIntervalSeconds > 0 && source.RetentionHours > 0 &&
            ValidHigh(source.Temperature.ResetBelowCelsius, source.Temperature.WarningThresholdCelsius,
                source.Temperature.CriticalThresholdCelsius, source.Temperature.MinimumDurationSeconds) &&
            ValidHigh(source.MemoryPressure.ResetBelow, source.MemoryPressure.WarningThreshold,
                source.MemoryPressure.CriticalThreshold, source.MemoryPressure.MinimumDurationSeconds) &&
            ValidHigh(source.Utilization.ResetBelow, source.Utilization.WarningThreshold,
                source.Utilization.CriticalThreshold, source.Utilization.MinimumDurationSeconds) &&
            source.Fan.CriticalBelowRpm < source.Fan.WarningBelowRpm && source.Fan.WarningBelowRpm < source.Fan.ResetAboveRpm &&
            source.Fan.MinimumDurationSeconds >= 0;
        if (valid) return source;
        try { logger.LogWarning("Invalid alert configuration; using safe defaults"); } catch { }
        return new AlertOptions { Enabled = source.Enabled };
    }

    private static bool ValidHigh(double reset, double warning, double critical, double duration) =>
        double.IsFinite(reset) && double.IsFinite(warning) && double.IsFinite(critical) && reset < warning && warning < critical && duration >= 0;

    private sealed class SensorAlertState
    { public AlertSeverity? Active { get; set; } public AlertSeverity? Candidate { get; set; } public DateTimeOffset CandidateSince { get; set; } }
    private sealed record RaisedAlert(MonitorAlert Alert, bool Notify);
}
