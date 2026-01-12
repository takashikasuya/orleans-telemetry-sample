# Admin Console Design Notes

## Overview

The Blazor Server admin console (`src/AdminGateway`) ministers immediate operational visibility for the Orleans cluster alongside the RDF-based graph seed workflow. It runs independently of the API gateway and reuses the same JWT/OIDC configuration so operators can lock it down with existing tokens. The console refreshes live metrics for grain activations, silo health, storage tiers, and ingest configuration, and it exposes the Graph RDF import controls that re-use the same Orleans grains as the silo bootstrap graph seeding.

## Implementation summary

- **Startup surface**: `Program.cs` wires JWT authentication/authorization, telemetry options, the `AdminMetricsService`, and an Orleans client configured via `UseStaticClustering`. Blazor Server pages are served from `/` while a set of `/admin/*` endpoints expose the same slices (`grains`, `clients`, `storage`, `ingest`, `graph/import/status`, `graph/import`) for automation.
- **Metrics service**: `AdminMetricsService` (`src/AdminGateway/Services/AdminMetricsService.cs`) aggregates data from `IManagementGrain` (grain stats + silo hosts/runtime stats), `TelemetryStorageScanner`, and configuration options via `TelemetryIngestOptions`. It shapes DTOs such as `GrainActivationSummary`, `SiloSummary`, `StorageOverview`, and `IngestSummary` and surfaces methods for triggering graph seeds via the Orleans grain `IGraphSeedGrain`.
- **Blazor page**: `Pages/Admin.razor` injects `AdminMetricsService`, refreshes data during `OnInitializedAsync`, and renders the Activation Explorer, Client Connections table, storage tiers, ingest summary, and Graph RDF Import card. Helpers such as `FormatMemoryUsage`, `GetGraphStatusLabel`, and the CSS grid update keep the UI readable.
- **Graph seed contracts**: `GraphSeedRequest`/`GraphSeedStatus` in `Grains.Abstractions/GraphSeedContracts.cs` remain the interchange objects used by the console when portal users request a new import path and tenant. The Orleans host keeps the last status inside `GraphSeedGrain` so the UI card can show the last run details without extra storage.

## Component breakdown

- **`src/AdminGateway/Program.cs`** – configures authentication, telemetry scanners, Orleans client, and maps `/admin` endpoints plus Blazor pages.
- **`src/AdminGateway/Services/AdminMetricsService.cs`** – orchestrates Orleans management grains, storage scanning, ingest config, and graph seed invocations; exports typed DTOs used by the UI.
- **`src/AdminGateway/Models/AdminDtos.cs`** – holds `GrainActivationSummary`, `SiloSummary` (CPU %, filtered memory, max memory), `StorageOverview`, and `IngestSummary`.
- **`src/AdminGateway/Pages/Admin.razor`** – renders the dashboard, data tables, storage tiers, and graph import card with helpers for formatting and status badges.
- **`src/AdminGateway/wwwroot/css/app.css`** – contains styling for layout, tables, tag chips, graph import grid, and status badges to keep the console visually consistent and readable.
- **`src/SiloHost/GraphSeedGrain.cs` & `src/SiloHost/GraphSeeder.cs`** – handle the actual RDF parsing and grain population that the console reuses through `GraphSeedRequest`/`GraphSeedStatus`.
- **`src/Grains.Abstractions/GraphSeedContracts.cs`** – defines the serializable records shared between console, silo, and integration tests.

## Data & UI sequence

1. **Metrics refresh** – `Admin.razor` calls `AdminMetricsService.RefreshAsync`, which queries `IManagementGrain.GetSimpleGrainStatistics`, `GetHosts`, and `GetRuntimeStatistics`, then merges silo addresses/run stats into `SiloSummary` rows. Storage stats come from `TelemetryStorageScanner.ScanAsync`, and ingest configuration is read from `TelemetryIngestOptions`. The page then renders tables for activations, silo clients, storage tiers, and ingest tags with formatted CPU/memory values.
2. **Graph seed status load** – simultaneously `RefreshAsync` asks `IGraphSeedGrain.GetLastResultAsync` and caches the last `GraphSeedStatus` so the status card can show timestamps, node/edge counts, and messages via `GetGraphStatusLabel`/`GetGraphStatusBadgeClass`.
3. **Manual graph import** – when the operator clicks “インポートを実行”, the page builds a `GraphSeedRequest` (path + tenant) and calls `AdminMetricsService.TriggerGraphSeedAsync`, which relays the call to `GraphSeedGrain.SeedAsync`. That grain invokes `GraphSeeder`, which parses RDF, writes grains, and returns success metadata. The UI rerenders the status card with the result and optionally shows feedback text.

Operators extending the UI (e.g., wiring file pickers or RDF validation hints) can keep relying on the same `/admin/*` endpoints and data contracts, so automation or scripting can continue to call the REST surface even if the Blazor layout changes.
