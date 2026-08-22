# LAN PC Monitor API

[Back to project overview](../README.md) · [Security](SECURITY.md)

The service exposes a versioned JSON API and WebSocket event stream on the local network. The v1 base URL is:

```text
http://<private-pc-address>:5005/api/v1
```

The API has no authentication, authorization, or TLS. Use it only across a trusted private LAN and read the [security model](SECURITY.md) before deployment.

The live OpenAPI 3.1 document is available from every running service at:

```text
http://<private-pc-address>:5005/openapi/v1.json
```

The same contract is available as YAML at `/openapi/v1.yaml`.

Import that URL into Postman, Insomnia, or an OpenAPI client generator. The document is generated from the service's
endpoint definitions and is the machine-readable HTTP contract. WebSocket behavior is documented separately below
because OpenAPI describes HTTP request/response operations, not WebSocket message streams.

## Versioning and compatibility

`/api/v1` is the stable public contract. Additive changes—new endpoints, optional response properties, and new
capabilities—may be introduced within v1. Removing or renaming fields, changing their meaning or type, or changing
existing route behavior requires a new major API prefix such as `/api/v2`.

The previous unversioned `/api/...`, `/status`, and `/ws/sensors` routes remain as compatibility aliases for existing
clients, but they are omitted from OpenAPI and new applications should not use them. Check `/api/v1/status` before
depending on optional features; it reports `apiVersion` and `capabilities`.

## Conventions

- JSON properties use `camelCase`.
- Enums are strings such as `warning`, `critical`, and `active`.
- Timestamps are ISO 8601 values with offsets; examples use UTC (`Z`).
- Nullable properties are omitted from JSON.
- Device registration uses `POST` and `DELETE`; monitoring and history routes are read-only.
- Clients may request Brotli or gzip with `Accept-Encoding: br, gzip`.
- Invalid bound values or rejected combinations return `400`; unknown routes return `404`.

Application validation errors use a small JSON object:

```json
{ "error": "'resolution' must be minute, hour, or day." }
```

Treat unknown response properties as forward-compatible additions. Clients should branch on HTTP status codes rather
than matching error-message text.

## Quick start

```bash
curl http://192.168.1.50:5005/api/v1/status
curl http://192.168.1.50:5005/api/v1/sensors
curl http://192.168.1.50:5005/api/v1/alerts/status
```

No API key or account is required. This is a LAN trust model, not a public-internet API. Browser JavaScript running
from another origin is not enabled by default because the service deliberately has no permissive CORS policy; native
apps, desktop apps, and server-side clients can call it directly.

## Endpoint summary

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/v1/status` | Service health, version, and capabilities. |
| `GET` | `/api/v1/sensors` | Latest complete sensor snapshot. |
| `GET` | `/api/v1/sensors/catalog` | Numeric-to-stable sensor identifier catalog. |
| `GET` | `/api/v1/session` | Current idle/candidate/active load session. |
| `GET` | `/api/v1/session/last` | Most recently completed in-memory session. |
| `GET` | `/api/v1/alerts` | Retained warning and critical alerts. |
| `GET` | `/api/v1/alerts/status` | Live alert metrics, thresholds, progress, and evaluator state. |
| `GET` | `/api/v1/alert-rules` | List persisted custom sensor alert rules. |
| `POST` | `/api/v1/alert-rules` | Create a custom threshold rule. |
| `PUT` | `/api/v1/alert-rules/{id}` | Replace an existing custom rule. |
| `DELETE` | `/api/v1/alert-rules/{id}` | Delete a custom rule. |
| `GET` | `/api/v1/notifications/status` | Push configuration and registered-device count. |
| `POST` | `/api/v1/notifications/test-overheating` | Queue a simulated critical GPU alert; loopback requests only. |
| `POST` | `/api/v1/notifications/devices` | Register or refresh a mobile push token. |
| `DELETE` | `/api/v1/notifications/devices/{installationId}` | Unregister a mobile installation. |
| `GET` | `/api/v1/history` | Paged and filtered historical readings. |
| `GET` | `/api/v1/history/manifest` | Retained-history inventory with ETag support. |
| `WS` | `/api/v1/ws/sensors` | Live sensor and alert event stream. |

## `GET /api/v1/status`

Recommended connectivity check. It returns service health, the Windows machine name, and server time.

```json
{
  "status": "ok",
  "service": "PCMonitor",
  "machineName": "DESKTOP-PC",
  "timestamp": "2026-08-16T10:20:30+00:00",
  "version": "0.1.7",
  "apiVersion": "1",
  "capabilities": ["sensors", "history", "sessions", "alerts", "push-notifications", "websocket"]
}
```

## `GET /api/v1/sensors`

Returns the latest complete snapshot already collected by the polling service. It does not trigger another hardware poll.

```json
{
  "timestamp": "2026-08-16T10:20:30+00:00",
  "sensors": [
    {
      "id": "/intelcpu/0/temperature/0",
      "hardware": "Intel Core i7",
      "name": "CPU Package",
      "type": "Temperature",
      "value": 62.5,
      "unit": "°C"
    }
  ]
}
```

`value` and `unit` may be omitted when the provider has no current value or unit.

Live string sensor IDs are derived from LibreHardwareMonitor identifiers and are stable across ordinary service
restarts. Clients must still tolerate catalog changes after hardware replacement, driver/provider changes, or a
service upgrade. Never infer meaning by parsing an ID; use `hardware`, `name`, `type`, and `unit`. Compact history uses
integer IDs to reduce payload size, and `/api/v1/sensors/catalog` provides the required mapping.

## `GET /api/v1/sensors/catalog`

Returns metadata for the numeric IDs used by compact history responses. `key` is the stable LibreHardwareMonitor-derived identity.

```json
{
  "version": "catalog-version",
  "sensors": [
    {
      "id": 17,
      "key": "/intelcpu/0/temperature/0",
      "hardware": "Intel Core i7",
      "name": "CPU Package",
      "type": "Temperature",
      "unit": "°C"
    }
  ]
}
```

Cache entries by `version`. Every history response identifies the catalog version used for its `sensorId` values.

## `GET /api/v1/session`

Returns `idle`, `candidate`, or `active`. Candidate and active states share the stable GUID assigned when candidacy begins. Active sessions include incremental sensor statistics.

```json
{
  "state": "active",
  "session": {
    "id": "6e78182d-6a2b-4b79-9d33-cfb4327e65b8",
    "startedAt": "2026-08-16T10:00:00Z",
    "durationSeconds": 122.4,
    "currentCpuLoad": 73.2,
    "currentGpuLoad": 91.1,
    "currentDominantProcess": {
      "name": "game.exe",
      "cpuPercent": 28.4
    },
    "primaryProcess": {
      "name": "game.exe",
      "dominantSampleCount": 22,
      "cpuSampleCount": 24,
      "averageCpuPercent": 26.2,
      "maxCpuPercent": 31.8
    },
    "sensors": [
      {
        "id": "/intelcpu/0/temperature/0",
        "hardware": "Intel Core i7",
        "name": "CPU Package",
        "type": "Temperature",
        "current": 62.5,
        "min": 55.1,
        "max": 67.2,
        "average": 61.8,
        "sampleCount": 120,
        "unit": "°C"
      }
    ]
  }
}
```

When idle, `session` is omitted: `{ "state": "idle" }`. The primary process is the process ranked first for the greatest number of valid sample intervals, not simply the latest observation.

## `GET /api/v1/session/last`

Returns the most recently completed session retained in memory, including its ID, start/end, duration, and primary process. It does not duplicate historical sensor arrays and is cleared by a service restart. Finalized history buckets retain their session IDs.

## `GET /api/v1/alerts`

Returns retained alerts chronologically. They remain in memory for 24 hours by default and are cleared by restart.

| Parameter | Type | Meaning |
|---|---|---|
| `from` | ISO 8601 timestamp | Exclusive lower timestamp bound. |
| `severity` | `warning` or `critical` | Exact severity filter. |

```text
GET /api/v1/alerts?from=2026-08-16T10%3A00%3A00Z
GET /api/v1/alerts?severity=warning
GET /api/v1/alerts?from=2026-08-16T10%3A00%3A00Z&severity=critical
```

```json
{
  "from": "2026-08-16T10:10:00Z",
  "to": "2026-08-16T10:10:00Z",
  "alerts": [
    {
      "id": "3ff0eb75-f14b-49fa-8c4e-d7acec1ee322",
      "timestamp": "2026-08-16T10:10:00Z",
      "severity": "critical",
      "sensorId": "/intelcpu/0/temperature/0",
      "hardware": "Intel Core i7",
      "sensorName": "CPU Package",
      "sensorType": "Temperature",
      "value": 96,
      "threshold": 95,
      "unit": "°C",
      "message": "CPU Package temperature reached 96°C."
    }
  ]
}
```

Thresholds must remain exceeded for their configured minimum duration. Hysteresis prevents repeats until a reading
crosses its reset boundary; warning may escalate once to critical. The built-in evaluator monitors primary CPU/GPU
temperatures, physical-memory pressure, and temperature-gated CPU/GPU fan-speed alerts. Utilization alerts default to off
because sustained high load is normal during games and rendering.

## `GET /api/v1/alerts/status`

Returns the current alert-aware sensor view. This is the source of truth for app progress bars; clients should not
duplicate configured thresholds.

```json
{
  "timestamp": "2026-08-18T10:20:30Z",
  "sensors": [
    {
      "category": "temperature",
      "direction": "high",
      "sensorId": "/intelcpu/0/temperature/0",
      "hardware": "Intel Core i7",
      "sensorName": "CPU Package Temperature",
      "sensorType": "Temperature",
      "value": 76,
      "unit": "°C",
      "warningThreshold": 85,
      "criticalThreshold": 95,
      "state": "safe",
      "progress": 0.8,
      "distanceToCritical": 19
    }
  ]
}
```

`direction` is `high` for temperature, pressure, and utilization, and `low` for fan speed. `state` is `safe`,
`pending`, `warning`, or `critical`. Pending entries include `pendingSecondsRemaining`. Fan entries include a
`condition` explaining their hardware-temperature gate. Progress is normalized from zero to one.

## Custom alert rules

Custom rules persist under `%ProgramData%\LanPcMonitor\alerts\custom-rules.json` and use stable sensor keys from the
sensor catalog. Each rule triggers above or below one threshold, waits for a configurable duration, and remains active
until the value crosses its separate recovery threshold. `notificationsEnabled` controls push delivery without
disabling local alert history.

```http
POST /api/v1/alert-rules
Content-Type: application/json

{
  "name": "SSD running hot",
  "sensorId": "/storage/0/temperature/0",
  "direction": "above",
  "threshold": 70,
  "resetThreshold": 65,
  "minimumDurationSeconds": 30,
  "severity": "warning",
  "enabled": true,
  "notificationsEnabled": true
}
```

For `above`, recovery must be lower than the trigger. For `below`, recovery must be higher. Updating and deleting an
unknown rule returns `404`; invalid thresholds return `400`.

To prevent noisy rules and unnecessary relay traffic, the service allows at most 32 custom rules in total and two
rules per sensor. A push-enabled rule must remain beyond its threshold for at least 30 seconds (five seconds for a
local-only rule). New triggers must also be meaningfully separated from the sensor's live value: 5 °C or percentage
points, 100 RPM or MHz, 5 W, or 0.1 V. Recovery thresholds have a smaller sensor-specific minimum gap. The service
delivers at most one identical non-test push per alert source and severity per minute. Independent conditions such as
CPU and GPU overheating, and an escalation from warning to critical, are delivered immediately. Suppressed duplicates
are still retained in the PC's local alert history.

## Push notification registration

Push delivery uses the configured HTTPS notification relay. The Firebase Admin credential remains exclusively on the
relay server and is never distributed to a monitored PC or mobile client.

The app registers its FCM token directly with the relay, then gives the paired PC its installation ID and send-only
capability:

```http
POST /api/v1/notifications/devices
Content-Type: application/json

{
  "installationId": "7f53e641-5ab8-4eb8-b616-75e57a7cc485",
  "sendSecret": "relay-send-capability",
  "platform": "android",
  "deviceName": "Sam's phone"
}
```

Register again when pairing changes or the app refreshes its relay registration. The response deliberately omits the
send capability. To stop notifications, call `DELETE /api/v1/notifications/devices/{installationId}`. Registrations
are stored under the common application-data directory by default. Relay destinations that expire are removed
automatically.

`GET /api/v1/notifications/status` reports `enabled`, `configured`, `registeredDevices`, and `minimumSeverity`.
By default, only critical alerts are pushed. Alert evaluation never waits for network delivery.

## `GET /api/v1/history`

Returns finalized history buckets. The current partial bucket is not exposed. Minute data is persisted; hour/day data is generated on request.

### Query parameters

| Parameter | Type | Default | Meaning |
|---|---|---:|---|
| `from` | ISO 8601 timestamp | none | Exclusive lower bound on bucket start time. |
| `to` | ISO 8601 timestamp | none | Inclusive upper bound on bucket start time. |
| `afterSequence` | integer ≥ 0 | none | Sequences strictly greater than this cursor. |
| `beforeSequence` | integer ≥ 0 | none | Sequences strictly less than this cursor, newest first. |
| `limit` | positive integer | `500` | Page size, clamped to the configured maximum (`2000` by default). |
| `resolution` | `minute`, `hour`, `day` | `minute` | Time aggregation. |
| `sensorId` | repeatable integer | all | Only the listed catalog sensor IDs. |
| `sessionId` | GUID | all | Only buckets associated with this confirmed session. |

`from` must be earlier than `to`. Negative cursors, non-positive limits, and unknown resolutions return `400` with an `error` property.

```text
GET /api/v1/history?afterSequence=12345&limit=500
GET /api/v1/history?beforeSequence=12345&limit=60
GET /api/v1/history?from=2026-08-16T10%3A00%3A00Z&to=2026-08-16T12%3A00%3A00Z
GET /api/v1/history?resolution=hour&sensorId=17&sensorId=23
GET /api/v1/history?sessionId=6e78182d-6a2b-4b79-9d33-cfb4327e65b8
```

```json
{
  "catalogVersion": "catalog-version",
  "resolution": "minute",
  "fromSequence": 12346,
  "toSequence": 12347,
  "hasMore": true,
  "nextSequence": 12347,
  "availableToSequence": 13000,
  "remainingBuckets": 653,
  "snapshots": [
    {
      "sequence": 12346,
      "startTime": "2026-08-16T10:00:00Z",
      "endTime": "2026-08-16T10:01:00Z",
      "sensors": [
        { "sensorId": 17, "min": 61.2, "max": 64.8, "avg": 62.9, "count": 60 }
      ],
      "sessionId": "6e78182d-6a2b-4b79-9d33-cfb4327e65b8",
      "dominantProcess": {
        "name": "game.exe",
        "averageCpuPercent": 26.2,
        "maxCpuPercent": 31.8,
        "sampleCount": 12
      }
    }
  ]
}
```

Optional fields are omitted. Compact values are rounded to one decimal; persisted/internal values keep their precision.

### Cursor synchronization

For forward sync, fetch the manifest and catalog, then request `afterSequence=<last-committed-sequence>`. Commit each page before using its `nextSequence`. Continue while `hasMore` is true. Omit `sensorId` during background sync so every diagnostic sensor is retained.

For older data, request `beforeSequence=<oldest-local-sequence>`. Results are newest-first; when more exists, use `previousSequence` for the next request.

Hour buckets align to UTC hours and day buckets to UTC calendar days. Min/max are source extrema, counts are summed, and averages are weighted by sample count.

## `GET /api/v1/history/manifest`

Returns a compact inventory of retained history:

```json
{
  "streamId": "bb6e2b7d-e62d-4ca5-ae2e-53085b4dad64",
  "catalogVersion": "catalog-version",
  "oldestSequence": 9000,
  "newestSequence": 13000,
  "bucketCount": 4001,
  "oldestTimestamp": "2026-08-09T10:00:00Z",
  "newestTimestamp": "2026-08-16T10:00:00Z",
  "resolutionSeconds": 60,
  "retentionHours": 168,
  "sequenceRanges": [
    { "fromSequence": 9000, "toSequence": 13000, "bucketCount": 4001 }
  ],
  "generatedAt": "2026-08-16T10:01:00Z"
}
```

The response includes `ETag` and `Cache-Control: no-cache`. Send the exact ETag in `If-None-Match`; an unchanged inventory returns `304 Not Modified` without JSON. Keep client coverage ledgers per `streamId`; a changed value identifies a new server history stream.

## `WS /api/v1/ws/sensors`

Connect to `ws://<private-pc-address>:5005/api/v1/ws/sensors`. The service immediately sends current sensors, then publishes sensor and alert envelopes:

```json
{
  "type": "sensors",
  "data": { "timestamp": "2026-08-16T10:20:30Z", "sensors": [] }
}
```

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

The server-to-client stream is read-only. Each subscriber has a bounded 64-message buffer; a slow client loses oldest queued events instead of blocking monitoring. Treat `sensors` as transient current state and use HTTP history for durable synchronization.

```javascript
const socket = new WebSocket("ws://192.168.1.50:5005/api/v1/ws/sensors");
socket.onmessage = event => console.log(JSON.parse(event.data));
socket.onerror = event => console.error(event);
// Later: socket.close();
```

A normal HTTP request to this WebSocket route returns `400`.

## Setup routes

`GET /setup` returns locally generated HTML with status and the detected LAN address. `GET /setup/qr.svg` returns a locally generated SVG QR code pointing to that setup page. No external QR service is contacted.

## Minimal client flow

```text
GET /api/v1/status
GET /api/v1/sensors/catalog
GET /api/v1/history/manifest
GET /api/v1/history?afterSequence=<last-local-sequence>&limit=500
WS  /api/v1/ws/sensors
```

Use HTTP for durable synchronization and WebSocket for transient live state.
