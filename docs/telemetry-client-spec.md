# Telemetry Tree Client Spec

## Purpose & Personas

- **Objective**: Provide operators a live, tenant-aware browsing experience for the building telemetry hierarchy from Site down to Device, surface device points as actionable properties, and enable remote control through the ApiGateway contracts.
- **Primary persona**: Facilities or automation engineers who need to trace telemetry from a spatial context (Site → Building → → Device) and react quickly by visualizing trends and issuing control commands.
- **Secondary persona**: Operators monitoring telemetry for SLA compliance who benefit from a tree view that reduces navigation friction compared to graph visualizations.

## UX Flow

### Tree Navigation

- Left pane powered by a MudBlazor `MudTreeView` showing Site → Building → Level → Area → Equipment → Device nodes.
- Nodes load lazily via `/api/graph/traverse/{nodeId}`; the root list comes from `/api/registry/sites`.
- Each node label includes a friendly name, optional status badge (online/offline), and tenant-aware metadata. Search/filter input above the tree narrows matches (tenant scope + text).
- Selecting a Device stops the tree drill-down and fetches its point list from `/api/nodes/{nodeId}` or `/api/devices/{deviceId}`.

### Trend & Control Panel

- Right pane shows the selected Device’s points (displayed as property rows) and the currently highlighted point’s latest telemetry value.
- A chart area renders historic data from `/api/telemetry/{deviceId}` (windowed query) and refreshes via tenant-scoped polling within ~2s.
- Writable points surface context-sensitive controls (slider/input/toggle) that post to `/api/devices/{deviceId}/control` with `{ pointId, value }`.
- Every control response shows a toast or inline validation message referencing `PointControlResponse.Status`.

### Tenant Awareness & Polling

- Tenant selector at the top honors the JWT tenant claim; the UI should disable invalid tenants and show tooltips explaining isolation when necessary.
- Polling for telemetry updates re-uses the ApiGateway telemetry endpoint and respects tenant-scoped cursors to limit load.

## API Contract Mapping

| Feature | API |
|---|---|
| Initial tree root | `GET /api/registry/sites?tenantId={tenant}` |
| Child lookup | `GET /api/graph/traverse/{nodeId}?tenantId={tenant}&depth=1` |
| Device details & point list | `GET /api/devices/{deviceId}?tenantId={tenant}` and `GET /api/nodes/{nodeId}?tenantId={tenant}` |
| Point value history | `GET /api/telemetry/{deviceId}?limit={n}&fallBackTo={timestamp}` |
| Remote control | `POST /api/devices/{deviceId}/control` (new contract accepting `PointControlRequest`, returning `PointControlResponse`) |
| Live telemetry push | Polling via repeated `GET /api/telemetry/{deviceId}` requests and optional WebSocket upgrade in future |

## Real-time & Control Contracts

- **PointControlRequest/Response**: Reuse ApiGateway contracts (`ApiGateway.Contracts.PointControlRequest`/`PointControlResponse`). The client will extend the ApiGateway surface with these payloads.

## Tech Stack & Integration Notes

- **Blazor Server** for hosting the MudBlazor layout and typed `HttpClient` pipelines.
- **MudBlazor** for components (`MudTreeView`, cards, loading shards, toast).
- **ECharts** (via JS interop) for telemetry trends; initial version may use `Plotly.js` if ECharts setup proves heavier (decision pending).
- **Authentication**: Blazor Server will inherit API JWT tokens (same middleware) or use `HttpClient` configured with `Authorization` header per tenant selection.
- **Service layer**: `TelemetryTreeService`, `TelemetryChartService`, `DeviceControlService` abstractions wrapping typed `HttpClient` requests.
- **Navigation**: Use `NavMenu` to expose the telemetry client entry point from ApiGateway docs or README; eventually include tenant filter in layout.

## Initial Implementation Plan

1. Document the UX contract (this spec) and the API surface used by the client (per Step 1 and 2).
2. Scaffold `src/TelemetryClient/` as a Blazor Server project with MudBlazor, HttpClient factories, and placeholder tree/trend layout.
3. Add new `ApiGateway.Contracts` library so the client can consume `PointControlRequest`/`Response` without pulling in the entire ApiGateway host.
4. Wire up placeholder pages/components (`TelemetryTree.razor`, `TelemetryCanvas.razor`, `PointControlForm.razor`) with TODO markers for future data bindings.
