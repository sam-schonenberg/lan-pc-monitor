using System.ComponentModel;
using System.Diagnostics;

namespace LanPcMonitor.Tray.Services;

internal sealed class MaintenanceScriptRunner
{
    private readonly string _scriptsDirectory = Path.Combine(AppContext.BaseDirectory, "scripts");

    public async Task<ScriptRunResult> RunElevatedAsync(string scriptName)
    {
        var scriptPath = Path.Combine(_scriptsDirectory, scriptName);
        if (!File.Exists(scriptPath))
        {
            return ScriptRunResult.Failed($"Maintenance script is missing: {scriptPath}");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                WorkingDirectory = _scriptsDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null)
            {
                return ScriptRunResult.Failed("Windows could not start the maintenance script.");
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? ScriptRunResult.Success()
                : ScriptRunResult.Failed($"The maintenance operation failed with exit code {process.ExitCode}.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return ScriptRunResult.Cancelled();
        }
        catch (Exception exception)
        {
            return ScriptRunResult.Failed($"The maintenance operation could not be started: {exception.Message}");
        }
    }
}

internal sealed record ScriptRunResult(bool Succeeded, bool WasCancelled, string? ErrorMessage)
{
    public static ScriptRunResult Success() => new(true, false, null);
    public static ScriptRunResult Cancelled() => new(false, true, null);
    public static ScriptRunResult Failed(string message) => new(false, false, message);
}
