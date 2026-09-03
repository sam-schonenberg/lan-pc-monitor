<div align="center">
  <img src="assets/branding/lan-pc-monitor-icon.png" width="112" alt="LAN PC Monitor icon">
  <h1>LAN PC Monitor</h1>
  <p>Understand your Windows PC's temperatures, usage, clocks, fans, and memory from another device on your local network.</p>

  <a href="https://github.com/sam-schonenberg/lan-pc-monitor/releases/latest"><img src="https://img.shields.io/github/v/release/sam-schonenberg/lan-pc-monitor?display_name=tag&amp;sort=semver" alt="Latest release"></a>
  <a href="LICENSE.txt"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="License: MIT"></a>
</div>

> [!IMPORTANT]
> ### Want to try the Android app?
>
> I no longer publish prebuilt APK files on GitHub. If you would like to try LAN PC Monitor, send me a friend request
> on Discord using the username **`schonenbergdev`** and let me know that you would like to test the app.
>
> [**💬 Open Discord and add `schonenbergdev`**](https://discord.com/channels/@me)

LAN PC Monitor combines a lightweight Windows monitoring service with a companion app. It turns hardware-specific sensor labels into readable names, keeps recent history locally, and offers live dashboards, charts, temperature and resource-pressure alerts, fan-health checks, and load-session summaries.

> [!NOTE]
> **LAN PC Monitor is entering alpha testing, and I am looking for Android testers.** Google Play testing is the
> remaining step before the app can be published to the Play Store. If you would like to help test the app, please
> add **`schonenbergdev`** on Discord and mention that you would like to test LAN PC Monitor.

The project is usable today, but it is pre-release software: expect rough edges and verify alerts and readings against
your hardware before relying on them.

> [!IMPORTANT]
> The current API uses unencrypted HTTP and WebSockets without authentication. Install it only on a trusted local network. Never forward port `5005` or expose it to the internet.

## Install and connect

### 1. Install the Windows monitor

[**Download the latest Windows installer from GitHub Releases**](https://github.com/sam-schonenberg/lan-pc-monitor/releases/latest)

Open the release's **Assets** section and download `LanPcMonitor-0.2.0-win-x64.msi`. Run the installer as an administrator. It installs:

- the background monitoring service;
- the signed PawnIO hardware-access driver required for supported CPU and motherboard sensors;
- the notification-area companion;
- a Start Menu shortcut; and
- a Windows Firewall rule limited to the local subnet.

The setup completion page opens the PC's local pairing page. Windows may show an unknown-publisher warning while public code signing is not configured. See [Installer and updates](docs/INSTALLER.md) for detailed behavior and verification.

### 2. Install the companion app

The app is not publicly listed, and prebuilt APK files are no longer posted on GitHub. To request access, open
[Discord](https://discord.com/channels/@me), add **`schonenbergdev`**, and mention that you would like to test LAN PC Monitor.

The Android app is only a viewer; the Windows monitor must be installed and running on the PC.

### 3. Connect

Keep the phone and PC on the same local network, open the app, and scan the QR code shown by the PC's setup page. You can also enter the PC address manually, for example:

```text
http://192.168.1.50:5005
```

Guest Wi-Fi isolation, VPN routing, or another firewall can prevent devices on the same Wi-Fi from reaching each other.

## Features

- Human-readable CPU, GPU, memory, fan, power, voltage, and temperature labels.
- Live readings updated about once per second.
- A customizable dashboard with current values, graphs, and alerts.
- Local minute-by-minute history with hour, day, and longer-range charts.
- Sustained temperature warnings and critical alerts with hysteresis.
- Custom above/below alert rules for individual sensors.
- Optional critical push notifications, enabled from the app's Settings page.
- CPU/GPU load-session detection and per-session statistics.
- Offline access to history already synchronized to the app.
- Local storage with configurable retention and no telemetry.

The exact sensor set depends on the PC's hardware, drivers, permissions, and [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) support.

## Architecture and privacy

| Component | Role |
|---|---|
| Windows service | Reads hardware sensors, records history, evaluates alerts, and exposes the LAN API. |
| Android app | Displays live readings, dashboards, alerts, and synchronized offline history. |
| Tray companion | Opens the pairing page and lets you test notifications, enable, disable, or uninstall the service. |
| Notification relay | Forwards opt-in critical alerts to Firebase; it cannot connect to the PC and stores no alert history. |

Monitoring, pairing, live dashboards, and history synchronization stay on the local network and require no account.
There is no telemetry. Monitoring data is stored under `%ProgramData%\LanPcMonitor` on the PC, while the app keeps
synchronized history and preferences in its private local database. Process monitoring retains only normalized process
names such as `game.exe` and aggregate CPU statistics during detected load sessions—never paths, command lines, window
titles, documents, memory contents, or network activity.

Push notifications are the only feature that uses an internet service. After you opt in from **Settings**, the Windows
service sends a bounded alert to the hosted relay, which forwards it through Firebase Cloud Messaging. The relay cannot
access or control the monitored PC and does not receive sensor history. Notifications can be disabled again from the
app. See the [notification relay and privacy boundary](docs/NOTIFICATION_RELAY.md).

Read the full [security model](docs/SECURITY.md) before using the service on a shared or untrusted network.

## Requirements

For alpha testing:

- x64 Windows 10 or Windows 11 for the monitoring service;
- an Android device with access to the private Google Play testing track; and
- both devices on the same reachable local network.

For development:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0);
- the .NET MAUI Android workload for Android builds; and
- administrator access for service and firewall testing.

## Build from source

```powershell
git clone https://github.com/sam-schonenberg/lan-pc-monitor.git
cd lan-pc-monitor
dotnet restore .\PCMonitor.Service.slnx
dotnet run --project .\PCMonitor.Service\PCMonitor.Service.csproj
```

The service listens on all local interfaces on port `5005` by default. Start with:

- Setup and pairing: <http://localhost:5005/setup>
- Service status: <http://localhost:5005/api/v1/status>
- Current sensors: <http://localhost:5005/api/v1/sensors>
- Public v1 contract and integration guide: [API documentation](docs/API.md)
- Machine-readable OpenAPI 3.1 document: `http://<pc-address>:5005/openapi/v1.json`

Use `http://`, not `https://`; TLS is not configured in this release.

### Test

```powershell
dotnet build .\PCMonitor.Service.slnx --configuration Release
dotnet test .\PCMonitor.Service.Tests\PCMonitor.Service.Tests.csproj --configuration Release
dotnet test .\PCMonitor.Application.Tests\PCMonitor.Application.Tests.csproj --configuration Release -f net10.0-windows10.0.19041.0
```

### Package builds

Build the Android client:

```powershell
dotnet build .\PCMonitor.Application\PCMonitor.Application.csproj --configuration Release -f net10.0-android
```

Build the self-contained Windows MSI:

```powershell
.\scripts\build-installer.ps1 -Version 0.2.0
```

Release maintainers should follow the [release checklist](docs/RELEASING.md).

## Service configuration

Defaults live in [`PCMonitor.Service/appsettings.json`](PCMonitor.Service/appsettings.json). An installed copy is
preserved across upgrades, so local changes are not overwritten.

| Section | Controls |
|---|---|
| `Monitoring` | Hardware polling interval. |
| `SessionDetection` | CPU/GPU load thresholds and timing. |
| `HistoricalMonitoring` | Bucket duration, retention, and API limits. |
| `ProcessMonitoring` | Dominant-process sampling during load sessions. |
| `Alerts` | Temperature thresholds, hysteresis, and retention. |
| `Notifications` | Hosted relay, minimum push severity, delivery interval, and enable/disable state. |
| `Server` | LAN API port; default `5005`. |
| `Setup` | Optional app-store links on the pairing page. |

If you change the service port, update the firewall rule to match:

```powershell
.\scripts\install-firewall-rule.bat 5010
```

Most users do not need to edit these values. Phone notification enrollment happens in the Android app under
**Settings → Critical notifications**; no Firebase project or relay configuration is required.

## Alpha limitations

- The monitoring service is Windows-only and the installer is x64-only.
- The LAN API has no authentication, pairing authorization, TLS, or rate limiting.
- QR setup uses the PC's current private IP address; automatic mDNS rediscovery is not implemented.
- A sudden crash can lose the unfinished current history bucket.
- Windows packages are not yet Authenticode-signed.
- The Android app is available only through the private Google Play testing track.
- Updates are installed manually; there is no in-app update checker.

## Documentation

- [API reference](docs/API.md)
- [Installer and updates](docs/INSTALLER.md)
- [Security and safe deployment](docs/SECURITY.md)
- [Notification relay architecture](docs/NOTIFICATION_RELAY.md)
- [Release checklist](docs/RELEASING.md)
- [v0.2.0 release notes](docs/RELEASE_NOTES_0.2.0.md)

## Contributing

Focused issues and pull requests are welcome. Keep the API backward-compatible where practical, add tests for behavioral changes, and do not introduce telemetry or outbound services without an explicit design discussion and opt-in model.

Report security concerns privately through GitHub Private Vulnerability Reporting rather than a public issue.

## License

Copyright © 2026 Schonenberg Developments. Distributed under the [MIT License](LICENSE.txt).
