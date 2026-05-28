# Planscape.Edge

Thin .NET worker that sits between field devices and Planscape Server
(Pillar B, decisions D2/D3). It does two jobs:

1. **Protocol adapters** normalise device telemetry into the shared
   `ITelemetryAdapter` contract (MQTT today; BACnet/IP + Modbus are stubbed
   seams). Adapters live here — not as server background services — so
   background ingest needs no per-request user context: the edge holds a
   project API token and posts to the server's authenticated HTTP ingest.

2. **Store-and-forward** (`StoreAndForwardQueue`) buffers every batch to disk
   (one atomic file per batch) and a forward loop drains it to
   `POST /api/projects/{id}/telemetry/ingest`, deleting a batch only on a
   confirmed 2xx. A WAN cut just accumulates files; they replay in order on
   reconnect. **No telemetry is lost across a network outage** — the
   non-negotiable v1 guarantee.

```
field devices ─▶ [adapter] ─▶ StoreAndForwardQueue (disk) ─▶ ServerForwarder ─▶ Planscape Server
```

## Run

```bash
dotnet run --project Planscape.Edge -- \
  --Edge:ServerUrl=https://api.planscape.example \
  --Edge:ProjectId=<guid> \
  --Edge:ApiToken=<project-token>
```

Or edit `appsettings.json`. The Simulator adapter is on by default so you can
exercise the full edge → server → twin → K3 overlay pipeline without a broker
or real devices.

## Config (`Edge` section)

| Key | Meaning |
|---|---|
| `ServerUrl` / `ProjectId` / `ApiToken` | Where + as whom to forward |
| `QueuePath` | Disk buffer directory (survives restarts) |
| `ForwardIntervalMs` / `ForwardBatchSize` | Drain cadence + max readings per POST |
| `Adapters.Simulator` | Synthetic devices/metrics for testing |
| `Adapters.Mqtt` | Broker host/port/topic/credentials (MQTTnet) |
| `Adapters.Modbus` / `Adapters.Bacnet` | Enable flags (clients are TODO seams) |

MQTT payload convention (single object or array):

```json
{ "deviceId": "AHU-01", "metric": "supply_air_temp_c", "value": 23.4, "unit": "°C" }
```

`deviceId` may instead come from the topic `planscape/<deviceId>/telemetry`.

## Status

Committed without `dotnet build` (Linux sandbox). Verify package versions
(MQTTnet 4.x) and build before deploy. BACnet/Modbus adapters are documented
stubs — enabling one logs a notice and reads nothing until its client is wired.
Standalone project (not in `Planscape.sln`); build with
`dotnet build Planscape.Edge/Planscape.Edge.csproj`.
