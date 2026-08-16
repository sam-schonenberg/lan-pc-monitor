# LAN PC Monitor

LAN PC Monitor is a Windows hardware-monitoring service built with C# and .NET 10. It reads hardware sensors through LibreHardwareMonitor and exposes current readings, live WebSocket updates, detected load sessions, and recent historical data to devices on the same private network.

The solution also contains an optional WinForms tray companion for controlling the Windows Service. The monitoring service remains completely headless and does not depend on the tray application.

## Features

- CPU, GPU, memory, and motherboard monitoring through LibreHardwareMonitor
- Approximately one sensor update per second
- HTTP API and standard WebSocket live updates
- Automatic sustained CPU/GPU load-session detection
- Stable GUIDs assigned when load-session candidates begin
- Lightweight dominant-process CPU tracking during candidate and active sessions
- Server-side temperature alerts with sustained thresholds and hysteresis
- Per-session minimum, maximum, average, current value, and sample count
- Clock-aligned historical aggregation, with one bucket per minute by default
- Rolling in-memory history and JSONL recovery after restarts
- Compact, compressed cursor-based history synchronization with minute/hour/day views
- Local setup page with a dynamically generated LAN QR code
- Windows Service hosting support
- Private-profile, local-subnet Windows Firewall rule
- Optional notification-area companion application

There is no cloud backend, account system, router configuration, or internet dependency for monitoring.

## Requirements

- Windows 10 or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for development
- Administrator access when installing or controlling the Windows Service and firewall rule

Some hardware sensors may require the service or development process to run with elevated privileges. Available sensors depend on the installed hardware, drivers, and LibreHardwareMonitor support.

## Solution structure

```text
PCMonitor.Service/
├── PCMonitor.Service/          Monitoring service and HTTP API
│   ├── History/                Rolling historical aggregation and persistence
│   ├── Alerts/                 Alert evaluation, retention, and live events
│   ├── Models/                 API and sensor models
│   ├── Sensors/                Hardware-provider abstraction and LibreHardwareMonitor
│   ├── Services/               Polling, current snapshots, and setup page
│   └── SessionDetection/       Automatic load-session detection
├── LanPcMonitor.Tray/          Optional WinForms notification-area companion
├── PCMonitor.Application/      .NET MAUI Android/mobile application foundation
├── PCMonitor.Service.Tests/    History, session, and alert tests
├── scripts/                    Service and firewall maintenance scripts
└── PCMonitor.Service.slnx
```

## Run for development

From the repository root:

```powershell
dotnet restore .\PCMonitor.Service.slnx
dotnet run --project .\PCMonitor.Service\PCMonitor.Service.csproj
```

The console should report:

```text
Now listening on: http://0.0.0.0:5005
```

Open:

- Setup page: <http://localhost:5005/setup>
- Service status: <http://localhost:5005/status>
- Current sensors: <http://localhost:5005/api/sensors>
- Current load session: <http://localhost:5005/api/session>
- Most recently completed session: <http://localhost:5005/api/session/last>
- Historical records: <http://localhost:5005/api/history>
- Sensor catalog: <http://localhost:5005/api/sensors/catalog>

Use `http://`, not `https://`. HTTPS is not configured in the current LAN-only MVP.

## API

### `GET /status`

Returns basic health information, the machine name, and the current UTC time.

### `GET /api/sensors`

Returns the most recent complete sensor snapshot. Requests do not trigger additional hardware polling.

### `WS /ws/sensors`

Sends the current sensor snapshot immediately after connection and then sends sensor and alert events. Messages use an envelope:

```json
{
  "type": "sensors",
  "data": {
    "timestamp": "2026-08-14T12:00:00Z",
    "sensors": []
  }
}
```

An alert is sent once when raised or escalated:

```json
{
  "type": "alert",
  "data": {
    "id": "3ff0eb75-f14b-49fa-8c4e-d7acec1ee322",
    "severity": "critical",
    "sensorName": "CPU Package",
    "value": 96,
    "threshold": 95,
    "unit": "°C"
  }
}
```

A WebSocket URL cannot be tested directly in a browser address bar. Use the browser developer console:

```javascript
const socket = new WebSocket("ws://localhost:5005/ws/sensors");
socket.onmessage = event => console.log(JSON.parse(event.data));
socket.onerror = event => console.error(event);
```

Disconnect with:

```javascript
socket.close();
```

### `GET /api/session`

Returns `idle`, `candidate`, or `active`. Candidate and active responses include the stable session GUID, candidate start time, current CPU/GPU load, current dominant process when sampled, and the session's primary process so far. Active sessions also include incremental sensor statistics.

The primary process is the process that ranked first for the greatest number of valid sampling intervals, rather than merely the process seen in the latest sample.

### `GET /api/session/last`

Returns metadata for the most recently completed session retained in memory, including its GUID, boundaries, duration, and primary process. It does not duplicate the historical sensor arrays.

### `GET /api/alerts`

Returns retained alerts in chronological order. Alerts remain in memory for 24 hours by default.

```text
/api/alerts?from=2026-08-14T12:00:00Z
/api/alerts?severity=Warning
/api/alerts?severity=Critical
```

`from` is exclusive. The evaluator currently handles temperature sensors. A threshold must remain exceeded for five seconds by default; a one-sample spike does not alert. After a warning or critical alert is active, that severity does not fire again until the sensor falls below the reset temperature. A warning may still escalate to one critical alert.

### `GET /api/history`

Returns finalized historical buckets using compact numeric sensor IDs. Sensor metadata is supplied separately by `GET /api/sensors/catalog`; the catalog response has a version, and every history response names the catalog version it uses. Numeric IDs are transport identifiers—the stable LibreHardwareMonitor-derived string keys remain the internal identities.

Normal synchronization is cursor-based and includes all sensors:

```text
GET /api/sensors/catalog
GET /api/history?afterSequence=12345&limit=500
```

Each finalized minute bucket has a persistent, monotonically increasing `sequence`. The response includes `fromSequence`, `toSequence`, `hasMore`, and `nextSequence`. Page size defaults to 500 and is capped at 2000. Pass the returned `nextSequence` as the next `afterSequence` while `hasMore` is true.

Optional filters:

```text
/api/history?from=2026-08-14T12:00:00Z
/api/history?from=2026-08-14T12:00:00Z&to=2026-08-14T14:00:00Z
/api/history?sensorId=/gpu-nvidia/0/temperature/0
/api/history?sessionId=6e78182d-6a2b-4b79-9d33-cfb4327e65b8
/api/history?from=2026-07-01T00:00:00Z&to=2026-08-01T00:00:00Z&resolution=hour
/api/history?sensorId=17&sensorId=23
```

- `from` is exclusive, making it suitable for incremental synchronization.
- `to` is inclusive.
- Times must be valid UTC-capable ISO 8601 values.
- If both are supplied, `from` must be earlier than `to`.
- `sessionId` accepts a GUID and returns buckets associated with that confirmed session.
- `resolution` accepts `minute` (default), `hour`, or `day`.
- Repeated numeric `sensorId` values can narrow interactive graph/debugging queries. Background synchronization should omit this filter so all diagnostic sensors are retained.

Minute history remains the persisted server representation. Hour and day buckets are generated on request, aligned to UTC clock hours and UTC calendar days. Their minimum and maximum are extrema of the source buckets, sample counts are summed, and averages are weighted by sample count (not averaged as averages). Historical values are rounded to one decimal only in the compact API DTO; internal calculations and JSONL data retain their precision.

API clients that send `Accept-Encoding: br` or `Accept-Encoding: gzip` receive built-in ASP.NET Core response compression for suitable responses, including history, catalog, and alerts.

### `GET /api/history/manifest`

Returns a compact inventory of the history currently retained by the Windows service: persistent `streamId`, catalog version, oldest/newest sequence and timestamps, bucket count, resolution, retention window, and compressed sequence ranges. Clients can send the returned `ETag` with `If-None-Match`; an unchanged inventory returns `304 Not Modified` without retransmitting JSON.

Progressive synchronization follows this architecture:

```text
Service history manifest
        ↓
Phone sequence-coverage ledger
        ↓
Newest missing data first
        ↓
UI becomes useful immediately
        ↓
WorkManager fills older gaps in background
```

The phone persists a sequence-coverage ledger keyed by the manifest's `streamId`. Successfully committed pages are compressed into non-overlapping intervals, and manifest comparison produces precise missing ranges without scanning per-sensor history rows. A changed stream identity starts independent coverage while previously saved measurements remain available.

Foreground synchronization requests the newest missing range first and commits one 60-bucket page (normally about one hour), so History can redraw without waiting for the complete retained archive. If gaps remain, a network-constrained Android WorkManager job stores older pages in bounded batches. Unique periodic maintenance runs at Android's 15-minute minimum interval and resumes from the same ledger, including after app restarts. The scheduler does not request a battery-optimization exemption; Android remains free to defer background work.

Historical buckets include nullable `sessionId` and `dominantProcess` fields. A bucket is associated with a session when at least one normalized sensor snapshot observed that session in the confirmed `active` state. Candidate-only activity is never persisted as a session association. Buckets are not split when a session begins or ends partway through a minute.

### `GET /setup`

Displays the service status, detected LAN address, and a locally generated QR code. The QR currently opens the setup page through the detected IPv4 address. Stable device pairing and mDNS discovery are planned but not yet implemented.

## Configuration

The main settings are in [`PCMonitor.Service/appsettings.json`](PCMonitor.Service/appsettings.json).

```json
{
  "Monitoring": {
    "PollingIntervalMilliseconds": 1000
  },
  "SessionDetection": {
    "Enabled": true,
    "StartCpuLoadPercent": 40,
    "StartGpuLoadPercent": 40,
    "StartWindowSeconds": 10,
    "StartDurationSeconds": 30,
    "EndCpuLoadPercent": 20,
    "EndGpuLoadPercent": 20,
    "EndWindowSeconds": 30,
    "EndDurationSeconds": 90
  },
  "HistoricalMonitoring": {
    "Enabled": true,
    "BucketDurationSeconds": 60,
    "RetentionHours": 168,
    "DefaultPageSize": 500,
    "MaximumPageSize": 2000
  },
  "ProcessMonitoring": {
    "Enabled": true,
    "SamplingIntervalSeconds": 5,
    "TopProcessCount": 3
  },
  "Alerts": {
    "Enabled": true,
    "EvaluationIntervalSeconds": 1,
    "Temperature": {
      "WarningThresholdCelsius": 85,
      "CriticalThresholdCelsius": 95,
      "ResetBelowCelsius": 80,
      "MinimumDurationSeconds": 5
    },
    "RetentionHours": 24
  },
  "Server": {
    "Port": 5005
  },
  "Setup": {
    "AppStoreUrl": "",
    "GooglePlayUrl": ""
  }
}
```

When changing the server port for an installed deployment, also update `PORT` near the top of [`scripts/_common.bat`](scripts/_common.bat) or pass the port to the firewall installation script:

```powershell
.\scripts\install-firewall-rule.bat 5010
```

Store buttons appear on the setup page only when valid HTTPS URLs are configured.

## Historical data

By default, finalized history is stored at:

```text
%ProgramData%\LanPcMonitor\history\sensor-history.jsonl
```

Each line is an independent JSON historical bucket. The service appends approximately once per minute, keeps a rolling seven days by default, restores retained records during startup, and periodically compacts the file. The mobile database retains synchronized records for offline browsing; phone-side long-term retention/downsampling policy is intentionally deferred. History remains in memory if PC persistence becomes unavailable.

All available supported sensors continue to be recorded, including zero-valued and unchanged readings. Metadata deduplication, paging, aggregation, compact DTOs, and HTTP compression reduce transfer cost without weakening the service's crash-analysis semantics. A missing minute remains a genuine history gap; no “last value continues” behavior is used. Older JSONL records without a sequence remain readable and receive valid sequences during recovery.

Confirmed buckets retain their session GUID and per-minute dominant-process summary. This makes the historical timeline groupable by session after restart, although exact standalone session-boundary metadata and `/api/session/last` remain memory-only.

## Process monitoring and privacy

Process CPU sampling runs only while a load session is `candidate` or `active`, at a configurable five-second interval by default. It compares `TotalProcessorTime` deltas and normalizes them against elapsed wall time and the machine's logical processor count. Percentages therefore represent total machine CPU capacity: one saturated logical processor on a 16-thread machine is approximately `6.25%`, and total capacity is `100%`.

Only normalized process names such as `witcher3.exe` and aggregate CPU percentages are retained. The service does not collect executable paths, command lines, arguments, environment variables, window titles, documents, process memory, or per-process network activity. GPU usage by process is not monitored.

## MAUI application foundation

`PCMonitor.Application` is the initial .NET MAUI client architecture. It uses:

- `CommunityToolkit.Mvvm` for observable state and commands
- `sqlite-net-pcl` with one lazily initialized `SQLiteAsyncConnection`
- `HttpClient` for typed status, sensor, session, history, and alert requests
- `ClientWebSocket` for `sensors` and `alert` envelopes
- Dependency injection for pages, ViewModels, repositories, sync services, and network clients

On first launch, the app opens Setup instead of the dashboard. The user must enter a private LAN endpoint and successfully call `/status` before saving it. Inputs such as `192.168.1.50:5005` are normalized to HTTP. Public/internet host addresses and HTTPS endpoints are rejected.

After setup, Shell provides Dashboard, History, Alerts, and Settings tabs. History and alerts are stored under `FileSystem.AppDataDirectory` in `lanpcmonitor.db3`, remain viewable while the PC is offline, and use stable identities to avoid duplicates during incremental synchronization.

The History tab is a local-first browser. It offers a friendly hardware/sensor picker, remembers the selected sensor, and supports 1-hour, 6-hour, 24-hour, 7-day, 30-day, and 1-year ranges. Summary minimum/maximum/latest values and the sample-count-weighted average are queried separately from SQLite. Detailed records are newest-first and load from SQLite in 60-record pages as the user scrolls; scrolling never issues history API calls. Pull-to-refresh runs catalog/cursor synchronization and then reloads the current view. If the PC is offline, already saved history remains displayed.

History synchronization runs once when the app becomes active and presents short foreground progress; manual synchronization and its last-successful timestamp live in Settings. Synchronization is serialized so lifecycle, Settings, Dashboard refresh, and History refresh cannot start overlapping transfers.

The History chart uses the open-source LiveCharts2 MAUI package through one reusable `SensorChart` control. It plots the selected sensor's average as the primary line with subtle minimum and maximum boundaries, local-time axes, unit-aware values, and touch tooltips. Dashboard Graph widgets reuse this control in compact mode rather than maintaining a second chart implementation.

Dashboard is customizable with persisted Current Value, Graph, and Alerts widgets. Stored order plus half/full width determines a two-column packed layout. Edit mode supports catalog-driven add/edit, Move Up/Move Down, enable/disable, and confirmed deletion; touch drag-and-drop is not implemented. Current Value widgets use the shared WebSocket sensor state when online and clearly labeled latest SQLite history when offline, with optional 24-hour minimum/maximum values. Graph widgets query only their configured local history range and continue working offline. Alerts widgets use filtered local alert history and refresh from the existing shared alert stream. Widget configurations and ordering remain in `lanpcmonitor.db3`.

Chart data always comes from local SQLite. The 1-hour, 6-hour, and 24-hour views use stored minute records; 7-day and 30-day views create clock-aligned hourly points at query time; the 1-year view creates daily points. Aggregates preserve extrema, sample counts, and sample-count-weighted averages without modifying the underlying minute history. Detailed-list pagination remains an independent 60-row SQLite query path.

Progressive destructive downsampling, QR scanning, and Android notification presentation are not implemented yet.

Android permits cleartext traffic because the current PC service intentionally uses local HTTP. The app limits configured endpoints to private IPv4 addresses, localhost, `.local` names, or unqualified LAN hostnames. `INTERNET` and `ACCESS_NETWORK_STATE` are the only requested network permissions.

Build the Android target with:

```powershell
dotnet build .\PCMonitor.Application\PCMonitor.Application.csproj -f net10.0-android
```

The mobile application decides how to present server alerts. Android local notifications and notification permissions are intentionally deferred.

Only finalized buckets are persisted. During a normal shutdown, the current partial bucket is preserved with its actual last-sample time. A sudden crash may therefore lose the current unfinished minute, but previously finalized buckets remain recoverable.

## Build and test

Build the complete solution:

```powershell
dotnet build .\PCMonitor.Service.slnx --configuration Release
```

Run the focused tests:

```powershell
dotnet test .\PCMonitor.Service.Tests\PCMonitor.Service.Tests.csproj
```

## Publish

Publish the monitoring service:

```powershell
dotnet publish .\PCMonitor.Service\PCMonitor.Service.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output C:\LanPcMonitor
```

Publish the optional tray application into the same deployment directory:

```powershell
dotnet publish .\LanPcMonitor.Tray\LanPcMonitor.Tray.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output C:\LanPcMonitor
```

The published directory should contain both executables, `appsettings.json`, and the `scripts` directory.

## Install as a Windows Service

Open PowerShell or Command Prompt as Administrator and run from the published directory:

```powershell
.\scripts\install-service.bat
```

The installation script:

1. Registers `PCMonitor.Service.exe` as the `PCMonitor` Windows Service.
2. Configures automatic startup.
3. Creates or refreshes the firewall rule.
4. Starts the service.

The firewall rule is deliberately limited to:

```text
Name:            LAN PC Monitor - API
Direction:       Inbound
Action:          Allow
Protocol:        TCP
Port:            5005
Profile:         Private
Remote address:  LocalSubnet
```

No Public-profile rule, router port forwarding, or UPnP configuration is created. For LAN access, Windows must classify the active network as **Private**.

If the service executable is elsewhere, pass its full path:

```powershell
.\scripts\install-service.bat "C:\Somewhere\PCMonitor.Service.exe"
```

## Maintenance scripts

Run administrative scripts from an elevated terminal, or invoke the corresponding action through the tray application:

| Script | Behavior |
|---|---|
| `start-service.bat` | Starts the installed service; succeeds if already running. |
| `stop-service.bat` | Stops the service; succeeds if already stopped. |
| `restart-service.bat` | Gracefully stops and starts the service. |
| `disable-service.bat` | Stops the service and changes startup type to Disabled. |
| `enable-service.bat` | Restores Automatic startup and starts the service. |
| `install-firewall-rule.bat` | Creates the Private/LocalSubnet firewall rule without duplicates. |
| `remove-firewall-rule.bat` | Removes the application firewall rule. |
| `uninstall-service.bat` | Stops and removes the service registration and firewall rule. |

Uninstalling does not delete application files or arbitrary directories.

## Tray companion

Launch `LanPcMonitor.Tray.exe` after publishing. It opens without a normal window and places an icon in the Windows notification area.

The tray menu can:

- Show the current service state
- Start, stop, or restart the service
- Enable automatic startup
- Stop and disable the service
- Open API, status, and setup pages
- Uninstall the service after confirmation
- Exit the tray application without stopping monitoring

Administrative actions launch the maintenance scripts through Windows UAC. The service does not require the tray application to be running, and tray autostart is not configured by this MVP.

## LAN access

To connect from another device, find the PC's IPv4 address:

```powershell
ipconfig
```

Then use, for example:

```text
http://192.168.1.50:5005/status
ws://192.168.1.50:5005/ws/sensors
```

Both devices must be on the same reachable local network. Guest Wi-Fi client isolation, VPN routing, or a Public Windows network profile can prevent connectivity.

## Security status

The firewall restricts access to the private local subnet, but the HTTP API currently has no authentication or encryption. Treat the current version as a LAN-only MVP:

- Do not expose port `5005` through a router.
- Do not create an internet-facing firewall rule.
- Do not deploy it on an untrusted network.
- Do not classify public Wi-Fi as Private merely to make the API reachable.

Authentication, one-time QR pairing, stable device identity, and mDNS rediscovery are planned future improvements.

## Current limitations

- Windows only
- HTTP rather than HTTPS
- No API authentication or device pairing yet
- QR code uses the current IP address
- No mDNS discovery yet
- Exact standalone session boundaries and the `/api/session/last` record are memory-only; finalized historical buckets persist their session GUID associations
- Historical coverage exists only while the service is running
- The currently accumulating history bucket can be lost during a sudden crash
- No installer, updater, mobile application, or cloud service
