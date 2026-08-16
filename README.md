# LAN PC Monitor

LAN PC Monitor is a self-hosted hardware monitor for Windows. A lightweight Windows service reads CPU, GPU, memory, motherboard, and other supported sensors through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), then makes current and historical readings available to devices on the same local network.

There is no cloud backend, account, telemetry service, remote relay, or router configuration. During normal operation, the monitoring service makes no outbound internet connections. Monitoring data stays on the PC and on clients that you explicitly connect over your LAN.

> [!IMPORTANT]
> The current API uses HTTP and WebSockets without authentication or encryption. The supplied firewall rule limits inbound access to the local subnet on Windows networks classified as **Private**, but every device on that trusted subnet may be able to read the monitoring data. Never forward the API port, expose it to the internet, or use it on an untrusted network.

## What it does

- Polls supported hardware sensors about once per second.
- Streams live readings and alerts over a standard WebSocket connection.
- Detects sustained CPU/GPU load sessions and records per-session statistics.
- Samples only normalized process names and aggregate CPU usage while a load session is being detected or is active.
- Evaluates sustained temperature warnings and critical alerts with hysteresis.
- Stores clock-aligned minute history locally and restores it after restarts.
- Offers compressed, cursor-based history synchronization and minute/hour/day views.
- Provides a local-first .NET MAUI client with offline history, charts, and configurable dashboard widgets.
- Includes an optional Windows notification-area companion for service administration.

## Components

| Project | Purpose |
|---|---|
| `PCMonitor.Service` | Headless Windows monitoring service, local HTTP API, history, alerts, and load-session detection. |
| `PCMonitor.Application` | .NET MAUI client for setup, live dashboards, alerts, and offline history. |
| `LanPcMonitor.Tray` | Optional WinForms tray companion for controlling the Windows service. |
| `PCMonitor.Service.Tests` | Service history, alert, and session tests. |
| `PCMonitor.Application.Tests` | Application storage, chart, and dashboard tests. |
| `scripts` | Service installation and narrowly scoped Windows Firewall maintenance. |

The service does not depend on the tray application or mobile client. Any LAN client can use the documented API.

## Network and security model

LAN PC Monitor is designed around an explicit trusted-LAN boundary:

- **No runtime cloud dependency:** the service does not upload sensor data, contact an external API, open a tunnel, or require an account.
- **No automatic internet exposure:** the scripts do not configure UPnP, NAT, router port forwarding, public DNS, or a cloud relay.
- **Restricted firewall rule:** installation permits inbound TCP only on the configured port, only for the Windows **Private** profile, and only from `LocalSubnet`.
- **Private endpoints in the client:** the MAUI client accepts HTTP endpoints only when the host is loopback, private IPv4 (`10/8`, `172.16/12`, or `192.168/16`), `.local`, or an unqualified LAN hostname. Public IP addresses and HTTPS endpoints are rejected in the current version.
- **Local persistence:** service history is stored under `%ProgramData%\LanPcMonitor`; client history and settings are stored in the app's local SQLite database.
- **Data minimization:** process monitoring retains normalized executable names such as `game.exe` and aggregate CPU percentages. It does not collect executable paths, command lines, environment variables, window titles, document names, process memory, or per-process network activity.
- **Bounded retention:** history is retained for seven days and alerts for 24 hours by default; both are configurable.
- **No hidden write API:** the public API currently exposes read-only `GET` endpoints and a read-only event stream.

Package restore, SDK installation, and source links in this README may naturally use the internet during development. That is separate from the running monitoring service, which has no outbound network integration.

For the full threat model, limitations, and responsible-reporting guidance, see [Security](docs/SECURITY.md). For every route, query parameter, response shape, and synchronization rule, see the [API reference](docs/API.md).

## Requirements

- Windows 10 or Windows 11 for the monitoring service
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for development
- Administrator access to install/control the Windows service and firewall rule

Some sensors may require elevation. The exact sensor set depends on the hardware, drivers, and LibreHardwareMonitor support.

## Quick start for development

```powershell
git clone <your-repository-url>
cd PCMonitor.Service
dotnet restore .\PCMonitor.Service.slnx
dotnet run --project .\PCMonitor.Service\PCMonitor.Service.csproj
```

The service listens on all local interfaces on port `5005` by default:

```text
http://0.0.0.0:5005
```

Useful local URLs:

- Setup page: <http://localhost:5005/setup>
- Health/status: <http://localhost:5005/status>
- Current sensors: <http://localhost:5005/api/sensors>
- API reference: [docs/API.md](docs/API.md)

Use `http://`, not `https://`. TLS is not configured in the current version.

## Configuration

Settings live in [`PCMonitor.Service/appsettings.json`](PCMonitor.Service/appsettings.json). The principal options are:

| Section | Controls |
|---|---|
| `Monitoring` | Hardware polling interval. |
| `SessionDetection` | CPU/GPU start and end thresholds, windows, and durations. |
| `HistoricalMonitoring` | Bucket duration, retention, and API page-size limits. |
| `ProcessMonitoring` | Whether and how often dominant processes are sampled during load sessions. |
| `Alerts` | Evaluation interval, temperature thresholds, hysteresis, and retention. |
| `Server` | LAN API port; default `5005`. |
| `Setup` | Optional app-store links shown on the local setup page. |

If you change the installed service port, update `PORT` in [`scripts/_common.bat`](scripts/_common.bat) or pass the same port to the firewall script:

```powershell
.\scripts\install-firewall-rule.bat 5010
```

## Local data

Finalized service history is stored by default at:

```text
%ProgramData%\LanPcMonitor\history\sensor-history.jsonl
```

Each line is an independent historical bucket. The service appends roughly once per minute, periodically compacts the file, and keeps seven days by default. A persistent stream identity sits beside the history file. If persistence becomes unavailable, monitoring continues in memory.

All supported readings—including zero and unchanged values—are retained. A missing minute remains a real gap; the service never fabricates a “last value continues” reading. A graceful shutdown preserves the partial current bucket, while a sudden crash may lose only that unfinished bucket.

The MAUI client stores synchronized history, alerts, endpoint settings, and dashboard layout in `lanpcmonitor.db3` under its application data directory. Charts and scrolling read from SQLite, so saved data remains usable while the PC is offline.

## Build and test

```powershell
dotnet build .\PCMonitor.Service.slnx --configuration Release
dotnet test .\PCMonitor.Service.Tests\PCMonitor.Service.Tests.csproj
dotnet test .\PCMonitor.Application.Tests\PCMonitor.Application.Tests.csproj
```

Build only the Android client with:

```powershell
dotnet build .\PCMonitor.Application\PCMonitor.Application.csproj -f net10.0-android
```

## Publish and install

Publish the service and optional tray companion into one directory:

```powershell
dotnet publish .\PCMonitor.Service\PCMonitor.Service.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output C:\LanPcMonitor

dotnet publish .\LanPcMonitor.Tray\LanPcMonitor.Tray.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output C:\LanPcMonitor
```

From an elevated terminal in that directory:

```powershell
.\scripts\install-service.bat
```

The installer registers `PCMonitor.Service.exe` as the automatically started `PCMonitor` Windows service, installs the firewall rule, and starts the service. The resulting firewall scope is:

```text
Direction:       Inbound
Action:          Allow
Protocol:        TCP
Port:            5005 (configurable)
Profile:         Private
Remote address:  LocalSubnet
```

Uninstallation removes the service registration and its firewall rule without deleting arbitrary directories:

```powershell
.\scripts\uninstall-service.bat
```

## Connect from another LAN device

Find the PC's private IPv4 address with `ipconfig`, then connect from a device on the same reachable LAN:

```text
http://192.168.1.50:5005/status
ws://192.168.1.50:5005/ws/sensors
```

Guest Wi-Fi isolation, VPN routing, host firewalls, or a Windows network classified as Public may intentionally prevent access.

## Current limitations

- The monitoring service is Windows-only.
- The API has no authentication, device pairing, authorization, rate limiting, or TLS yet.
- The QR code contains the current private IP address; mDNS rediscovery is not implemented.
- `/api/session/last` is memory-only, although finalized history retains session associations.
- A sudden crash can lose the currently accumulating history bucket.
- Android notification presentation, an installer UI, automatic updates, and cloud access are not implemented.

## Contributing

Issues and focused pull requests are welcome. Please include tests for behavioral changes, keep the API backward-compatible where practical, and avoid adding telemetry or outbound services without an explicit design discussion and opt-in model.

Before opening a pull request, run the build and both test projects shown above. Security concerns should follow [docs/SECURITY.md](docs/SECURITY.md) instead of being disclosed in a public issue.

## License

This repository does not currently contain a license file. Until one is added, copyright law reserves reuse and redistribution rights to the copyright holder; public source availability alone does not grant an open-source license. Choose and add an OSI-approved license before presenting the project as freely reusable open source.
