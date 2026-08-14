using System.ComponentModel;
using System.ServiceProcess;
using Microsoft.Win32;

namespace LanPcMonitor.Tray.Services;

internal sealed class ServiceStatusReader(string serviceName)
{
    public ServiceState GetState()
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            _ = controller.Status;

            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            if (key?.GetValue("Start") is int startType && startType == 4)
            {
                return ServiceState.Disabled;
            }

            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServiceState.Running,
                ServiceControllerStatus.Stopped => ServiceState.Stopped,
                ServiceControllerStatus.StartPending => ServiceState.StartPending,
                ServiceControllerStatus.StopPending => ServiceState.StopPending,
                ServiceControllerStatus.Paused => ServiceState.Paused,
                ServiceControllerStatus.PausePending => ServiceState.PausePending,
                ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
                _ => ServiceState.Unknown
            };
        }
        catch (InvalidOperationException)
        {
            return ServiceState.NotInstalled;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1060)
        {
            return ServiceState.NotInstalled;
        }
    }
}

internal enum ServiceState
{
    Running,
    Stopped,
    StartPending,
    StopPending,
    Paused,
    PausePending,
    ContinuePending,
    Disabled,
    NotInstalled,
    Unknown
}
