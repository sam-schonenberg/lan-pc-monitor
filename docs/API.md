# LAN PC Monitor API

[Back to project overview](../README.md) · [Security](SECURITY.md)

The service exposes a read-only JSON API and WebSocket event stream on the local network. The default base URL is:

```text
http://<private-pc-address>:5005
```

The API has no authentication, authorization, or TLS. Use it only across a trusted private LAN and read the [security model](SECURITY.md) before deployment.

## Conventions

- JSON properties use `camelCase`.
- Enums are strings such as `warning`, `critical`, and `active`.
- Timestamps are ISO 8601 values with offsets; examples use UTC (`Z`).
- Nullable properties are omitted from JSON.
- All HTTP API routes are `GET` and do not mutate service state.
- Clients may request Brotli or gzip with `Accept-Encoding: br, gzip`.
- Invalid bound values or rejected combinations return `400`; unknown routes return `404`.

## Endpoint summary

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/status` | Service health and machine identity. |
| `GET` | `/api/sensors` | Latest complete sensor snapshot. |
| `GET` | `/api/sensors/catalog` | Numeric-to-stable sensor identifier catalog. |
| `GET` | `/api/session` | Current idle/candidate/active load session. |
| `GET` | `/api/session/last` | Most recently completed in-memory session. |
| `GET` | `/api/alerts` | Retained temperature alerts. |
| `GET` | `/api/history` | Paged and filtered historical readings. |
| `GET` | `/api/history/manifest` | Retained-history inventory with ETag support. |
| `GET` | `/setup` | Human-readable local setup page. |
| `GET` | `/setup/qr.svg` | Locally generated setup QR code as SVG. |
| `WS` | `/ws/sensors` | Live sensor and alert event stream. |

## `GET /status`

Recommended connectivity check. It returns service health, the Windows machine name, and server time.

```json
{
  "status": "ok",
  "service": "PCMonitor",
  "machineName": "DESKTOP-PC",
  "timestamp": "2026-08-16T10:20:30+00:00"
}
```

## `GET /api/sensors`

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

## `GET /api/sensors/catalog`

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

## `GET /api/session`

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

## `GET /api/session/last`

Returns the most recently completed session retained in memory, including its ID, start/end, duration, and primary process. It does not duplicate historical sensor arrays and is cleared by a service restart. Finalized history buckets retain their session IDs.

## `GET /api/alerts`

Returns retained alerts chronologically. They remain in memory for 24 hours by default and are cleared by restart.

| Parameter | Type | Meaning |
|---|---|---|
| `from` | ISO 8601 timestamp | Exclusive lower timestamp bound. |
| `severity` | `warning` or `critical` | Exact severity filter. |

```text
GET /api/alerts?from=2026-08-16T10%3A00%3A00Z
GET /api/alerts?severity=warning
GET /api/alerts?from=2026-08-16T10%3A00%3A00Z&severity=critical
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

Temperature thresholds must remain exceeded for the configured minimum duration (five seconds by default). Hysteresis prevents repeats until the reading falls below the reset threshold; warning may escalate once to critical.

## `GET /api/history`

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
GET /api/history?afterSequence=12345&limit=500
GET /api/history?beforeSequence=12345&limit=60
GET /api/history?from=2026-08-16T10%3A00%3A00Z&to=2026-08-16T12%3A00%3A00Z
GET /api/history?resolution=hour&sensorId=17&sensorId=23
GET /api/history?sessionId=6e78182d-6a2b-4b79-9d33-cfb4327e65b8
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

## `GET /api/history/manifest`

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

## `WS /ws/sensors`

Connect to `ws://<private-pc-address>:5005/ws/sensors`. The service immediately sends current sensors, then publishes sensor and alert envelopes:

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
const socket = new WebSocket("ws://192.168.1.50:5005/ws/sensors");
socket.onmessage = event => console.log(JSON.parse(event.data));
socket.onerror = event => console.error(event);
// Later: socket.close();
```

A normal HTTP request to this WebSocket route returns `400`.

## Setup routes

`GET /setup` returns locally generated HTML with status and the detected LAN address. `GET /setup/qr.svg` returns a locally generated SVG QR code pointing to that setup page. No external QR service is contacted.

## Minimal client flow

```text
GET /status
GET /api/sensors/catalog
GET /api/history/manifest
GET /api/history?afterSequence=<last-local-sequence>&limit=500
WS  /ws/sensors
```

Use HTTP for durable synchronization and WebSocket for transient live state.
