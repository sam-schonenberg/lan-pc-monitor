# LAN PC Monitor 0.1.7

Version 0.1.7 improves Windows updates, notification delivery, alert customization, dashboards, and history storage.

## Highlights

- Added the hosted notification relay for opt-in background alerts without placing Firebase administrator credentials
  on monitored PCs.
- Added custom sensor alert rules and improved temperature, memory-pressure, fan, and utilization alert behavior.
- Expanded dashboard widget configuration and sensor display formatting.
- Improved history synchronization and added idle-aware daily cleanup with a 128 MB maintenance trigger.
- Made MSI updates clearly identify themselves as updates, close the tray before replacing files, preserve existing
  data and configuration, and restart the updated tray afterward.
- Added the signed PawnIO hardware-access dependency to the Windows installer for supported low-level sensors.

Monitoring, history, dashboards, and pairing remain local. Only enabled push notifications use the hosted relay and
Firebase Cloud Messaging; the relay stores no alert history and cannot access or control the monitored PC.

## Upgrade notes

- **Windows:** install `LanPcMonitor-0.1.7-win-x64.msi` over the existing version. Configuration and monitoring history
  are preserved.
- **Android:** update through the configured Google Play testing or production track. Version code 7 is used for this
  release.
