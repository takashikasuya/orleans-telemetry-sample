# Admin Console Design Notes

## Overview

The Blazor Server admin console (`src/AdminGateway`) ministers immediate operational visibility for the Orleans cluster alongside the RDF-based graph seed workflow. It runs independently of the API gateway and reuses the same JWT/OIDC configuration so operators can lock it down with existing tokens. The console refreshes live metrics for grain activations, silo health, storage tiers, and ingest configuration, and it exposes the Graph RDF import controls that re-use the same Orleans grains as the silo bootstrap graph seeding. The graph view now renders as a MudBlazor tree (Site → Building → Level → Area → Equipment → Point) instead of the SVG network.

## Implementation summary

- **Startup surface**: `Program.cs` wires JWT authentication/authorization, telemetry options, the `AdminMetricsService`, and an Orleans client configured via `UseStaticClustering`. Blazor Server pages are served from `/` while a set of `/admin/*` endpoints expose the same slices (`grains`, `clients`, `storage`, `ingest`, `graph/import/status`, `graph/import`) for automation.
- **Metrics service**: `AdminMetricsService` (`src/AdminGateway/Services/AdminMetricsService.cs`) aggregates data from `IManagementGrain` (grain stats + silo hosts/runtime stats), `TelemetryStorageScanner`, and configuration options via `TelemetryIngestOptions`. It shapes DTOs such as `GrainActivationSummary`, `SiloSummary`, `StorageOverview`, and `IngestSummary` and surfaces methods for triggering graph seeds via the Orleans grain `IGraphSeedGrain`.
- **Blazor page**: `Pages/Admin.razor` injects `AdminMetricsService`, refreshes data during `OnInitializedAsync`, and renders the Activation Explorer, Client Connections table, storage tiers, ingest summary, and a spatial hierarchy tree (Site/Building/Level/Area/Equipment/Point) sourced from `GraphNodeGrain`. Selecting a node shows its GraphStore metadata (attributes + edges) and point snapshots when applicable.
- **Graph import uploads**: the Graph RDF Import card includes a file picker that uploads an RDF file to the AdminGateway server and then triggers the seed using that uploaded path. Configure the upload directory with `ADMIN_GRAPH_UPLOAD_DIR` or `Admin:GraphUploadDirectory`, and note that Docker deployments must mount the same directory into both the `admin` and `silo` containers so the Orleans silo can read the uploaded file.
- **Theme toggle**: the app bar includes a light-by-default theme switch so operators can opt into a dark palette without altering the data layout.
- **Graph seed contracts**: `GraphSeedRequest`/`GraphSeedStatus` in `Grains.Abstractions/GraphSeedContracts.cs` remain the interchange objects used by the console when portal users request a new import path and tenant. The Orleans host keeps the last status inside `GraphSeedGrain` so the UI card can show the last run details without extra storage.

## Component breakdown

- **`src/AdminGateway/Program.cs`** 窶・configures authentication, telemetry scanners, Orleans client, and maps `/admin` endpoints plus Blazor pages.
- **`src/AdminGateway/Services/AdminMetricsService.cs`** 窶・orchestrates Orleans management grains, storage scanning, ingest config, and graph seed invocations; exports typed DTOs used by the UI.
- **`src/AdminGateway/Models/AdminDtos.cs`** 窶・holds `GrainActivationSummary`, `SiloSummary` (CPU %, filtered memory, max memory), `StorageOverview`, and `IngestSummary`.
- **`src/AdminGateway/Pages/Admin.razor`** 窶・renders the dashboard, data tables, storage tiers, the MudBlazor hierarchy tree, and the graph import card.
- **`src/AdminGateway/wwwroot/css/app.css`** 窶・contains styling for layout, tables, tag chips, graph import grid, and status badges to keep the console visually consistent and readable.
- **`src/SiloHost/GraphSeedGrain.cs` & `src/SiloHost/GraphSeeder.cs`** 窶・handle the actual RDF parsing and grain population that the console reuses through `GraphSeedRequest`/`GraphSeedStatus`.
- **`src/Grains.Abstractions/GraphSeedContracts.cs`** 窶・defines the serializable records shared between console, silo, and integration tests.

## Data & UI sequence

1. **Metrics refresh** 窶・`Admin.razor` calls `AdminMetricsService.RefreshAsync`, which queries `IManagementGrain.GetSimpleGrainStatistics`, `GetHosts`, and `GetRuntimeStatistics`, then merges silo addresses/run stats into `SiloSummary` rows. Storage stats come from `TelemetryStorageScanner.ScanAsync`, and ingest configuration is read from `TelemetryIngestOptions`. The page then renders tables for activations, silo clients, storage tiers, and ingest tags with formatted CPU/memory values.
2. **Graph seed status load** 窶・simultaneously `RefreshAsync` asks `IGraphSeedGrain.GetLastResultAsync` and caches the last `GraphSeedStatus` so the status card can show timestamps, node/edge counts, and messages via `GetGraphStatusLabel`/`GetGraphStatusBadgeClass`.
3. **Manual graph import** 窶・when the operator clicks 窶懊う繝ｳ繝昴・繝医ｒ螳溯｡娯・ the page builds a `GraphSeedRequest` (uploaded path + tenant) and calls `AdminMetricsService.TriggerGraphSeedAsync`, which relays the call to `GraphSeedGrain.SeedAsync`. That grain invokes `GraphSeeder`, which parses RDF, writes grains, and returns success metadata. The UI rerenders the status card with the result and optionally shows feedback text.
   - For Docker, mount the same upload directory in both `admin` and `silo` containers so the silo can access the uploaded RDF file path.

Operators extending the UI (e.g., wiring file pickers or RDF validation hints) can keep relying on the same `/admin/*` endpoints and data contracts, so automation or scripting can continue to call the REST surface even if the Blazor layout changes.
