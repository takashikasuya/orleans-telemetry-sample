# Admin Console Design Notes

## Step 1 — Grain + Client Visibility

- **Data plane**: `AdminMetricsService` (`src/AdminGateway/Services/AdminMetricsService.cs`) calls `IManagementGrain` to fetch `GetSimpleGrainStatistics`, `GetHosts`, and `GetRuntimeStatistics`, then projects the results into `GrainActivationSummary` and the reshaped `SiloSummary` (CPU percentage, filtered memory usage, maximum available memory, activation/client counts).
- **Endpoints**: `AdminGateway/Program.cs` exposes `GET /admin/grains` and `GET /admin/clients` so the Blazor front‑end (and any automation) can replay the same data. Each silo row reports its humanized CPU %, used/maximum memory (byte formatting moved to `Pages/Admin.razor`), and a numeric breakdown of activation/client counts to support troubleshooting.
- **UI layout**: The Activation Explorer and Client Connections tables on `/` are the first panels operators see (`Step1`), surfacing the grain-type ranking plus per-silo resource usage after the latest refresh.

## Step 3 — Graph RDF Import

- **Contract**: The admin console delegates to `GraphSeedGrain` (`src/SiloHost/GraphSeedGrain.cs`) via `GraphSeedRequest`/`GraphSeedStatus` (`src/Grains.Abstractions/GraphSeedContracts.cs`). `GraphSeeder` wraps `DataModel.Analyzer.Integration.OrleansIntegrationService` to rehydrate RDF, populate `GraphNode`/`GraphIndex` grains, and return node/edge counts plus success metadata.
- **Endpoints**: `Program.cs` wires `GET /admin/graph/import/status` and `POST /admin/graph/import` so the UI can show the most recent run and trigger new seeds without touching silo environment variables. The Graph Seed status survives grain state so repeated reports include the last tenant/path pair, timestamps, counts, and any error message.
- **UI design**: The Graph RDF Import card pairs the path/tenant inputs with the status card introduced in this update. The card now shows a badge (success/failure/idle), the last execution times, node/edge counts, and any message, helping operators understand whether re-seeding succeeded without digging through silo logs.

Operators can extend this flow (e.g., mounting RDF files) without modifying the API surface, since the request still supplies a file path that the silo must be able to open.
