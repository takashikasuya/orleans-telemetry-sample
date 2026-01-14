# plans.md: Admin UI Graph Statistics & Hierarchy Issues

## Purpose

Resolve two critical issues in the Admin UI (`AdminGateway`):

1. **Graph Statistics – Area Recognition**: After seeding from `seed-complex.ttl`, the Graph Statistics view shows 0 for Area count despite multiple Area entities existing in the RDF.
2. **Graph Hierarchy – Missing Relationships**: The Graph Hierarchy visualization does not display edge relationships between nodes, making the building topology invisible.

These issues prevent the Admin Console from correctly displaying the full building hierarchy (Site → Building → Level → Area → Equipment → Point) and their parent-child relationships.

---

## Current State Summary

### Observed Behavior

- **Admin UI Graph Statistics**:
  - URL: `http://localhost:8082/` → Graph Statistics section
  - Shows table with counts: Site, Building, Level, Area, Equipment, Point
  - After seeding `seed-complex.ttl`: Area count = 0 (expected ≥ 3)
  - Other counts appear correct (Building = 2, Level = 3, Equipment = 3, Point = 5)

- **Admin UI Graph Hierarchy**:
  - URL: `http://localhost:8082/` → Graph Hierarchy section
  - Renders nodes but no visible edges/relationships between them
  - Expected: Visual representation of parent-child links (e.g., Building → Level → Area → Equipment → Point)

- **seed-complex.ttl Structure**:
  - Contains 3 `sbco:Area` entities (area-main-f1-lobby, area-main-f2-server, area-lab-f1-exp-a)
  - Each Area has `rec:isPartOf` relationship to a Level
  - Properly formatted RDF in SBCO ontology

### Data Flow (Expected)

1. **Admin API** (`AdminGateway`):
   - `AdminMetricsService.GetGraphStatisticsAsync()` queries `IGraphIndexGrain` for nodes by type
   - `AdminMetricsService.GetGraphHierarchyAsync()` retrieves all nodes and their edges
   
2. **Orleans Grains**:
   - `IGraphIndexGrain`: Maintains an index of all nodes by `GraphNodeType`
   - `IGraphNodeGrain`: Stores node snapshots including `OutgoingEdges`

3. **RDF Seeding**:
   - `GraphSeeder.SeedAsync()` processes RDF parsed by `RdfAnalyzerService`
   - Registers all nodes in the index via `IGraphIndexGrain.AddNodeAsync()`

4. **Admin UI Rendering**:
   - Graph Statistics: Calls `GetGraphStatisticsAsync()` and renders a table
   - Graph Hierarchy: Calls `GetGraphHierarchyAsync()` and renders nodes/edges (SVG or canvas-based)

### Root Cause Hypotheses

1. **RDF Parsing Failure**:
   - `RdfAnalyzerService` may not be extracting Area entities from `seed-complex.ttl`
   - Could be missing `sbco:Area` type handling or incorrect URI matching

2. **Seeding Failure**:
   - `GraphSeeder.SeedAsync()` may not be creating `GraphNodeDefinition` for Areas
   - Filtering logic might exclude Area nodes
   - Index registration might fail silently for Area nodes

3. **Index Grain Issue**:
   - `IGraphIndexGrain.GetByTypeAsync(GraphNodeType.Area)` returns empty list
   - Nodes added but not properly indexed by type

4. **Hierarchy Serialization**:
   - `GraphNodeSnapshot.OutgoingEdges` not populated for any node type
   - Edge data lost during Orleans serialization

5. **Admin API Not Querying Correctly**:
   - `AdminMetricsService` may not be calling the right grain methods
   - Filtering or aggregation logic filters out Area nodes

---

## Target Behavior

1. **Area Recognition**:
   - After seeding `seed-complex.ttl`, Graph Statistics shows Area count = 3 (matching RDF)
   - All Areas are queryable via API
   - Area nodes appear in Graph Hierarchy visualization

2. **Edge Relationships**:
   - Graph Hierarchy displays edges between nodes (e.g., Building ← isPartOf Relationship → Level)
   - Parent-child relationships are visually evident
   - Clicking on a node shows its connected edges and neighbors

3. **Backward Compatibility**:
   - Existing tests continue to pass
   - Seeding via `seed.ttl` (minimal RDF) works unchanged
   - Telemetry ingestion is unaffected

---

## Success Criteria

1. **Area Extraction**:
   - RDF parsing test: Load `seed-complex.ttl`, extract 3 Area nodes
   - Graph Statistics: Area count matches extracted count
   - Admin API: `/admin/graph/statistics?tenantId=default` includes Area with count > 0

2. **Hierarchy Visualization**:
   - Graph Hierarchy renders with at least Site → Building → Level → Area → Equipment → Point edges
   - `GetGraphHierarchyAsync()` returns nodes with populated `OutgoingEdges`
   - UI properly renders edges (SVG, canvas, or DOM structure)

3. **No Regressions**:
   - All existing tests pass: `dotnet test`
   - Docker Compose stack starts without errors
   - Telemetry ingestion still works

---

## Constraints (from AGENTS.md)

1. **Local Verification Only**: Tests use `dotnet test`, `docker compose`, and local REST calls
2. **No External Services**: All data remains within the local stack
3. **Incremental Changes**: Preserve backward compatibility
4. **Observable Behavior**: Use `dotnet build`, `dotnet test`, Swagger to verify

---

## Steps

### Step 1: Diagnose RDF Parsing for Area

**Action**:
- Inspect `RdfAnalyzerService.ProcessRdfFileAsync()` to verify Area extraction
- Create or extend a unit test in `src/DataModel.Analyzer.Tests/` that loads `seed-complex.ttl`
- Verify extracted Area count and compare against manual count in RDF
- Expected outcome: At least 3 Area entities extracted

**Files to investigate**:
- `src/DataModel.Analyzer/RdfAnalyzerService.cs`
- `src/DataModel.Analyzer/Models/BuildingDataModel.cs`
- `src/Telemetry.E2E.Tests/seed-complex.ttl`

### Step 2: Diagnose Graph Seeding

**Action**:
- Add debug logging to `GraphSeeder.SeedAsync()` to track node counts by type
- Run seeding with `seed-complex.ttl` and capture logs
- Verify Area nodes are passed to `IGraphIndexGrain.AddNodeAsync()`
- Expected outcome: Log shows Area nodes being added

**Files to investigate**:
- `src/SiloHost/Services/GraphSeeder.cs` or equivalent
- `src/SiloHost/Grains/GraphIndexGrain.cs`

### Step 3: Verify Index Grain Behavior

**Action**:
- Inspect `IGraphIndexGrain` implementation
- Verify `GetByTypeAsync(GraphNodeType.Area)` returns all registered Area node IDs
- Write a test to query the index post-seeding
- Expected outcome: Index returns 3+ Area node IDs

**Files to investigate**:
- `src/SiloHost/Grains/GraphIndexGrain.cs`
- `src/Grains.Abstractions/IGraphIndexGrain.cs`

### Step 4: Diagnose Hierarchy Visualization

**Action**:
- Inspect `AdminMetricsService.GetGraphHierarchyAsync()` to confirm edges are loaded
- Check `Admin.razor` graph rendering logic
- Verify CSS/SVG rendering of edges
- Expected outcome: Edges are populated and rendered visually

**Files to investigate**:
- `src/AdminGateway/Services/AdminMetricsService.cs`
- `src/AdminGateway/Pages/Admin.razor`
- `src/AdminGateway/wwwroot/app.css`

### Step 5: Root Cause Fix

**Action** (based on Steps 1–4 findings):
- **If parsing fails**: Update `RdfAnalyzerService` to handle Area extraction
- **If seeding fails**: Update `GraphSeeder` to map Area entities correctly
- **If index fails**: Verify `IGraphIndexGrain` implementation and initialization
- **If rendering fails**: Update `Admin.razor` to properly iterate over edges

### Step 6: Verification

**Action**:
- Run `dotnet build` to ensure no compilation errors
- Run `dotnet test` to verify existing tests still pass
- Seed `seed-complex.ttl` via Admin UI
- Verify Area count > 0 in Graph Statistics
- Verify edges are visually rendered in Graph Hierarchy
- Document findings in plans.md
- Manual verification of the Admin UI graph hierarchy (via `scripts/start-system.sh`, Swagger, etc.) remains pending to confirm edges render as expected.

---

## Progress

- [x] Step 1 – Diagnose RDF parsing for Area
- [x] Step 2 – Diagnose Graph Seeding
- [x] Step 3 – Verify Index Grain Behavior
- [x] Step 4 – Diagnose Hierarchy Visualization
- [x] Step 5 – Root Cause Fix
- [ ] Step 6 – Verification

---

## Observations

- Observed `RdfAnalyzerService.ExtractAreas` filters only for `sbco:Space`/`rec:Space`/`rec:Area`, so `sbco:Area` subjects were skipped and never seeded, explaining the zero-count in statistics.
- Added node-type logging in `GraphSeeder.SeedAsync` to confirm Area nodes are produced by the analyzer before Orleans grains run.
- Added `GraphIndexGrainTests` covering add/remove behavior so we can reason about type-specific indexing without starting Orleans infrastructure.
- RDF data relies on `rec:isPartOf`/`sbco:isPartOf` rather than `hasPart`, so building, level, area, equipment, and point parents were missing. Storing those parent URIs and applying them in `BuildHierarchy` now rebuilds every relationship so edges appear.
- Ran `dotnet test src/DataModel.Analyzer.Tests/DataModel.Analyzer.Tests.csproj` to verify the analyzer suite accepts the new parent handling (required elevated permissions to create temp directories).
- Ran `dotnet build` (the solution compiles cleanly, with pre-existing Moq 4.20.0 warnings) to satisfy the build validation step.

---

## Decisions

- Adding `sbco:Area` to the `ExtractAreas` subject types provides coverage for the Area entities defined in `seed-complex.ttl` without touching downstream grains or UI logic.
- Logging node-type counts at seeding ensures we can verify Area node volume early and aids diagnosing index/edge issues.
- Verified indexing logic via `GraphIndexGrain` unit tests backed by a `TestPersistentState` harness rather than hitting a live Orleans silo.
- Chose to preserve edges for Site→Building→Level→Area→Equipment→Point by storing `rec:isPartOf` relationships during extraction and rehydrating them in `BuildHierarchy` so GraphSeeder can emit the appropriate edges.

---

## ExecPlan

### Purpose
Provide concrete, verifiable steps to identify and resolve the Area recognition and edge relationship issues in the Admin UI.

### Tasks

1. **Diagnose RDF Parsing**: Review `RdfAnalyzerService` to confirm Area extraction from `seed-complex.ttl`.
   - Load the RDF file and verify BuildingDataModel includes Area entities.
   - Add unit test to validate extraction count.

2. **Diagnose Graph Seeding**: Review `GraphSeeder.SeedAsync()` to confirm Area nodes are registered in the index.
   - Add debug logging for node type counts.
   - Run with `seed-complex.ttl` and capture logs.

3. **Verify Index Grain**: Query `IGraphIndexGrain` to confirm Area nodes are retrievable.
   - Write Orleans client test or manual verification script.

4. **Diagnose Hierarchy Rendering**: Review `AdminMetricsService` and `Admin.razor` for edge handling.
   - Confirm `OutgoingEdges` are populated and rendered.

5. **Implement Fix**: Based on findings, fix parsing, seeding, indexing, or rendering.
   - Update relevant files.
   - Run `dotnet build` and `dotnet test`.

6. **E2E Verification**: Seed `seed-complex.ttl` and verify Admin UI displays Area and edges.
   - Use Docker Compose or local services.
   - Document results.

### Verification Steps

**Build & Unit Tests**:
```bash
cd /home/takashi/projects/dotnet/orleans-telemetry-sample
dotnet build
dotnet test
```

**Docker Compose Verification**:
```bash
cd /home/takashi/projects/dotnet/orleans-telemetry-sample/scripts
./start-system.sh

# Open Admin UI in browser: http://localhost:8082/
# Upload seed-complex.ttl via /admin/graph/import
# Check Graph Statistics for Area count > 0
# Check Graph Hierarchy for visible edges

./stop-system.sh
```

---

## Retrospective

*To be updated after completion.*
