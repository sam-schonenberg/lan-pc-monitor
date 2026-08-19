using System.Text.Json;
using Microsoft.Extensions.Options;
using PCMonitor.Service.Models;

namespace PCMonitor.Service.Alerts;

public enum AlertRuleDirection { Above, Below }

public sealed record CustomAlertRule(Guid Id, string Name, string SensorId, AlertRuleDirection Direction,
    double Threshold, double ResetThreshold, double MinimumDurationSeconds, AlertSeverity Severity,
    bool Enabled = true, bool NotificationsEnabled = true);

public sealed record CustomAlertRuleRequest(string Name, string SensorId, AlertRuleDirection Direction,
    double Threshold, double ResetThreshold, double MinimumDurationSeconds, AlertSeverity Severity,
    bool Enabled = true, bool NotificationsEnabled = true);

public sealed record CustomAlertRulesResponse(IReadOnlyList<CustomAlertRule> Rules);

public sealed class CustomAlertRuleStore
{
    public const int MaximumRules = 32;
    public const int MaximumRulesPerSensor = 2;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, CustomAlertRule> _rules = [];
    private readonly string _path;
    private readonly ILogger<CustomAlertRuleStore> _logger;

    public CustomAlertRuleStore(IOptions<AlertOptions> options, ILogger<CustomAlertRuleStore> logger)
    {
        _logger = logger;
        _path = string.IsNullOrWhiteSpace(options.Value.RuleStoreFile)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "LanPcMonitor", "alerts", "custom-rules.json")
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Value.RuleStoreFile));
        Restore();
    }

    public IReadOnlyList<CustomAlertRule> GetAll()
    { lock (_sync) return _rules.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }

    public CustomAlertRule? Get(Guid id)
    { lock (_sync) return _rules.GetValueOrDefault(id); }

    public CustomAlertRule Create(CustomAlertRuleRequest request)
    {
        var rule = FromRequest(Guid.NewGuid(), request);
        lock (_sync) { _rules.Add(rule.Id, rule); PersistLocked(); }
        return rule;
    }

    public CustomAlertRule? Update(Guid id, CustomAlertRuleRequest request)
    {
        lock (_sync)
        {
            if (!_rules.ContainsKey(id)) return null;
            var rule = FromRequest(id, request); _rules[id] = rule; PersistLocked(); return rule;
        }
    }

    public bool Remove(Guid id)
    {
        lock (_sync) { if (!_rules.Remove(id)) return false; PersistLocked(); return true; }
    }

    public static string? Validate(CustomAlertRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 80)
            return "Rule name is required and cannot exceed 80 characters.";
        if (string.IsNullOrWhiteSpace(request.SensorId) || request.SensorId.Trim().Length > 256)
            return "A valid sensor ID is required.";
        if (!double.IsFinite(request.Threshold) || !double.IsFinite(request.ResetThreshold))
            return "Thresholds must be finite numbers.";
        var minimumDuration = request.NotificationsEnabled ? 30 : 5;
        if (!double.IsFinite(request.MinimumDurationSeconds) ||
            request.MinimumDurationSeconds < minimumDuration || request.MinimumDurationSeconds > 86400)
            return $"Minimum duration must be between {minimumDuration} and 86400 seconds" +
                   (request.NotificationsEnabled ? " when notifications are enabled." : ".");
        if (request.Direction == AlertRuleDirection.Above && request.ResetThreshold >= request.Threshold)
            return "An above-threshold rule must reset below its trigger threshold.";
        if (request.Direction == AlertRuleDirection.Below && request.ResetThreshold <= request.Threshold)
            return "A below-threshold rule must reset above its trigger threshold.";
        return null;
    }

    public string? ValidateForSensor(CustomAlertRuleRequest request, SensorReading sensor, Guid? replacingId = null)
    {
        var rules = GetAll();
        if (rules.Count(rule => rule.Id != replacingId) >= MaximumRules)
            return $"At most {MaximumRules} custom alert rules can be configured.";
        if (rules.Count(rule => rule.Id != replacingId &&
                               rule.SensorId.Equals(request.SensorId, StringComparison.OrdinalIgnoreCase)) >=
            MaximumRulesPerSensor)
            return $"At most {MaximumRulesPerSensor} custom alert rules can monitor the same sensor.";
        if (sensor.Value is not { } current || !float.IsFinite(current))
            return "The selected sensor does not currently have a usable reading.";

        var triggerMargin = RequiredTriggerMargin(sensor, current);
        if (request.Direction == AlertRuleDirection.Above && request.Threshold < current + triggerMargin)
            return $"The trigger must be at least {Format(current + triggerMargin)} {sensor.Unit} " +
                   $"for the current reading of {Format(current)} {sensor.Unit}.";
        if (request.Direction == AlertRuleDirection.Below && request.Threshold > current - triggerMargin)
            return $"The trigger must be at most {Format(current - triggerMargin)} {sensor.Unit} " +
                   $"for the current reading of {Format(current)} {sensor.Unit}.";

        var recoveryGap = RequiredRecoveryGap(sensor, current);
        if (Math.Abs(request.Threshold - request.ResetThreshold) < recoveryGap)
            return $"Trigger and recovery values must be at least {Format(recoveryGap)} {sensor.Unit} apart.";
        return null;
    }

    private static double RequiredTriggerMargin(SensorReading sensor, double current) => sensor.Unit switch
    {
        "°C" => 5, "%" => 5, "RPM" => 100, "MHz" => 100, "W" => 5, "V" => 0.1,
        _ => Math.Max(Math.Abs(current) * 0.1, 1)
    };

    private static double RequiredRecoveryGap(SensorReading sensor, double current) => sensor.Unit switch
    {
        "°C" => 2, "%" => 3, "RPM" => 100, "MHz" => 100, "W" => 2, "V" => 0.05,
        _ => Math.Max(Math.Abs(current) * 0.05, 0.5)
    };

    private static string Format(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static CustomAlertRule FromRequest(Guid id, CustomAlertRuleRequest request) => new(id,
        request.Name.Trim(), request.SensorId.Trim(), request.Direction, request.Threshold, request.ResetThreshold,
        request.MinimumDurationSeconds, request.Severity, request.Enabled, request.NotificationsEnabled);

    private void Restore()
    {
        try
        {
            if (!File.Exists(_path)) return;
            foreach (var rule in JsonSerializer.Deserialize<CustomAlertRule[]>(File.ReadAllText(_path), JsonOptions) ?? [])
                _rules[rule.Id] = rule;
        }
        catch (Exception exception) { _logger.LogError(exception, "Could not restore custom alert rules from {Path}", _path); }
    }

    private void PersistLocked()
    {
        var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_rules.Values.ToArray(), JsonOptions));
        File.Move(temporary, _path, true);
    }
}
