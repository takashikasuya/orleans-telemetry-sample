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

---

# plans.md: ApiGateway Test Coverage Expansion

## Purpose

Expand the test coverage of `ApiGateway` to achieve comprehensive validation of:
1. **All major REST endpoints** (currently only partial coverage)
2. **Error paths and boundary conditions** (404/410 responses, missing attributes, query limits)
3. **Authentication and authorization** (JWT validation, tenant isolation, 401/403 responses)
4. **gRPC DeviceService** (currently untested)

Current state: E2E tests cover basic telemetry flow but do not systematically exercise all API paths or error handling branches.

---

## Current State Summary

### Covered Endpoints (from E2E Tests)

- `GET /api/nodes/{nodeId}` – Retrieves graph node metadata
- `GET /api/nodes/{nodeId}/value` – Retrieves point value (happy path only)
- `GET /api/devices/{deviceId}` – Retrieves device snapshot
- `GET /api/telemetry/{deviceId}` – Queries telemetry with limit/pagination
- `GET /api/registry/exports/{exportId}` – Downloads registry export (basic case)
- `GET /api/telemetry/exports/{exportId}` – Downloads telemetry export (basic case)

### Uncovered/Partially Covered Endpoints

| Endpoint | Issue | Impact |
|----------|-------|--------|
| `GET /api/graph/traverse/{nodeId}` | No test coverage | Graph traversal logic untested |
| `GET /api/registry/devices` | No error/boundary tests | limit, pagination behavior unknown |
| `GET /api/registry/spaces` | No error/boundary tests | Returns Area nodes; limit not validated |
| `GET /api/registry/points` | No error/boundary tests | Point enumeration untested |
| `GET /api/registry/buildings` | No error/boundary tests | Building enumeration untested |
| `GET /api/registry/sites` | No error/boundary tests | Site enumeration untested |
| `GET /api/registry/exports/{exportId}` | Only 200 case | Missing 404/410 branches |
| `GET /api/telemetry/exports/{exportId}` | Only 200 case | Missing 404/410 branches |
| **gRPC DeviceService** | No test | Bidirectional streaming untested |

### Uncovered Error Paths

| Scenario | Current Status | Gap |
|----------|----------------|-----|
| `/api/nodes/{nodeId}` with missing PointId | Code has 404 branch | Test missing |
| `/api/nodes/{nodeId}` with missing DeviceId | Code has 404 branch | Test missing |
| `/api/nodes/{nodeId}/value` with invalid nodeId | 404 expected | Test missing |
| `/api/registry/exports/{exportId}` – NotFound (404) | Code handles | Test missing |
| `/api/registry/exports/{exportId}` – Expired (410) | Code handles | Test missing |
| `/api/telemetry/exports/{exportId}` – NotFound (404) | Code handles | Test missing |
| `/api/telemetry/exports/{exportId}` – Expired (410) | Code handles | Test missing |
| Telemetry query with limit=0 | Boundary untested | Edge case unknown |
| Telemetry query with very large limit | MaxInlineRecords threshold | Behavior unclear |
| Unauthorized request (missing auth) | Middleware should reject | Not explicitly tested |
| Wrong tenant in token | TenantResolver.ResolveTenant | Isolation not validated |

### Authentication/Authorization Gaps

- **Current approach**: `TestAuthHandler` mocks authentication; real JWT validation untested
- **Missing validation**:
  - JWT signature verification
  - Token expiration handling
  - Tenant claim extraction and validation
  - Cross-tenant data isolation (ensure tenant-a cannot access tenant-b data)
  - Missing/invalid Authorization header (401)
  - Insufficient permissions scenarios (403)

### Test Infrastructure

**Unit Tests** (`src/ApiGateway.Tests/`):
- `GraphRegistryServiceTests.cs` – Tests export creation and limit logic
- No tests for error paths, auth, or other endpoints

**E2E Tests** (`src/Telemetry.E2E.Tests/`):
- `TelemetryE2ETests.cs` – Full pipeline from RDF seed to telemetry query
- `ApiGatewayFactory.cs` – In-process API host with `TestAuthHandler`
- `TestAuthHandler.cs` – Mock JWT validation (does not exercise real logic)

---

## Target Behavior

1. **Complete endpoint coverage**: Every route in `Program.cs` has at least one passing test
2. **Error handling**: 404, 410, 400, 401, 403 responses are explicitly tested
3. **Boundary conditions**: Query limits, pagination, empty results validated
4. **Tenant isolation**: Token tenant claim is correctly resolved and enforced
5. **gRPC support**: DeviceService contract validation (connection, message exchange, error cases)
6. **No regressions**: All existing tests pass; backward compatibility maintained

---

## Success Criteria

1. **New test files/classes created**:
   - `ApiGateway.Tests/GraphTraversalTests.cs` – `/api/graph/traverse` endpoint
   - `ApiGateway.Tests/RegistryEndpointsTests.cs` – `/api/registry/*` endpoints with limits, pagination, errors
   - `ApiGateway.Tests/TelemetryExportTests.cs` – `/api/telemetry/exports/{exportId}` 404/410 branches
   - `ApiGateway.Tests/RegistryExportTests.cs` – `/api/registry/exports/{exportId}` 404/410 branches
   - `ApiGateway.Tests/AuthenticationTests.cs` – Auth/authz, tenant isolation, 401/403 scenarios
   - `ApiGateway.Tests/GrpcDeviceServiceTests.cs` – gRPC DeviceService contract, streaming, errors

2. **Test counts**:
   - Total: ≥20 new tests covering error paths, boundaries, and gRPC
   - Each endpoint should have ≥1 happy path + ≥1 error case

3. **Build & Test Pass**:
   - `dotnet build` succeeds
   - `dotnet test src/ApiGateway.Tests/` passes all new tests
   - No regressions in existing tests

4. **Coverage metrics** (aspirational):
   - All routes in `Program.cs` (lines 110–280) have at least one test
   - All error branches (`Results.NotFound()`, `Results.StatusCode()`) have at least one test

---

## Constraints (from AGENTS.md)

1. **Local testing only**: Tests use xUnit + FluentAssertions; no external services
2. **Mock gRPC**: For gRPC tests, use `Moq` to mock `IClusterClient` grain calls or in-process testing
3. **No breaking changes**: Preserve existing API contracts; only add tests
4. **Incremental approach**: Tests can be implemented in multiple PRs; this plan defines the roadmap

---

## Test Plan Breakdown

### 1. Graph Traversal Tests (`GraphTraversalTests.cs`)

**Endpoint**: `GET /api/graph/traverse/{nodeId}?depth=N&predicate=P`

**Test Cases**:
- Happy path: Traverse with depth 1, 2, 3 (should respect depth cap of 5)
- Empty result: Valid nodeId with no outgoing edges
- Invalid nodeId: 404 response
- Out-of-range depth: depth > 5 capped to 5; depth < 0 treated as 0
- Invalid predicate: Filtered edge type (e.g., "isPartOf") limits results
- Null predicate: All edges returned
- Tenant isolation: Different tenants see different graphs

**Mocking Strategy**:
- Mock `IClusterClient.GetGrain<IGraphNodeGrain>()` to return node snapshots with populated `OutgoingEdges`
- Use `GraphTraversal` service directly; test traversal logic in isolation

---

### 2. Registry Endpoints Tests (`RegistryEndpointsTests.cs`)

**Endpoints**: `/api/registry/devices`, `/api/registry/spaces`, `/api/registry/points`, `/api/registry/buildings`, `/api/registry/sites`

**Test Cases per Endpoint**:
- **Happy path**: Returns paginated list of nodes (inline mode when count ≤ maxInlineRecords)
- **With limit**: `?limit=5` returns top 5 nodes (inline)
- **Exceeds limit**: Node count > maxInlineRecords → export mode with URL
- **Empty result**: No nodes of given type → empty inline response
- **Negative limit**: Treated as 0 or error (boundary)
- **Very large limit**: Behavior when limit > total count
- **Tenant isolation**: Different tenants see only their own nodes

**Mocking Strategy**:
- Mock `IGraphIndexGrain.GetByTypeAsync()` to return node IDs
- Mock `IGraphNodeGrain.GetAsync()` for snapshots
- Use `RegistryExportService` to validate export creation

---

### 3. Telemetry Export Tests (`TelemetryExportTests.cs`)

**Endpoint**: `GET /api/telemetry/exports/{exportId}`

**Test Cases**:
- **Happy path (200)**: Export ready → returns file stream with correct content-type
- **NotFound (404)**: Non-existent exportId
- **Expired (410)**: Export TTL exceeded
- **Wrong tenant**: Export created by tenant-a; tenant-b tries to access → 404 or isolation check
- **Malformed exportId**: Invalid format (security check)

**Mocking Strategy**:
- Mock `TelemetryExportService.TryOpenExportAsync()` to return different statuses
- Create temporary export files or use in-memory streams

---

### 4. Registry Export Tests (`RegistryExportTests.cs`)

**Endpoint**: `GET /api/registry/exports/{exportId}`

**Test Cases**:
- **Happy path (200)**: Export ready → returns file stream
- **NotFound (404)**: Non-existent exportId
- **Expired (410)**: Export TTL exceeded
- **Wrong tenant**: Isolation validation
- **Concurrent access**: Multiple requests to same exportId

**Mocking Strategy**:
- Similar to telemetry export tests
- Mock `RegistryExportService.TryOpenExportAsync()`

---

### 5. Authentication & Authorization Tests (`AuthenticationTests.cs`)

**Scenarios**:
- **No Authorization header**: 401 Unauthorized
- **Invalid JWT token**: 401 Unauthorized
- **Expired token**: 401 Unauthorized (if validation implemented)
- **Missing tenant claim**: Tenant resolver should handle gracefully
- **Tenant isolation**: Token with `tenant=t1` cannot access data from `tenant=t2`
  - Create nodes for t1 and t2
  - Request as t1 should only see t1 nodes
  - Request as t2 should only see t2 nodes
- **Valid token, authorized**: Happy path with proper tenant claim
- **Custom predicate validation**: If additional claims required (future)

**Mocking Strategy**:
- Use real JWT validation (not just `TestAuthHandler`)
- Create signed tokens with different tenant claims
- Or: Extend `TestAuthHandler` to support failing cases (token expiration, missing claim, etc.)

**Note**: If real JWT setup is complex, initially test tenant isolation with `TestAuthHandler` setting different `TenantId` values; add real JWT tests later.

---

### 6. gRPC DeviceService Tests (`GrpcDeviceServiceTests.cs`)

**Service**: `DeviceService` (implements `Device.DeviceServiceBase`)

**Test Cases**:
- **GetDevice (unary)**: Valid deviceId → returns device snapshot
- **GetDevice (error)**: Invalid deviceId → gRPC error (NOT_FOUND)
- **SubscribeToDeviceUpdates (server-side streaming)**: Subscribe to device; receive updates when device state changes
- **Channel lifecycle**: Connect, receive messages, disconnect gracefully
- **Tenant isolation**: gRPC calls respect tenant context
- **Authentication**: gRPC metadata includes valid auth token

**Mocking Strategy**:
- Use `Grpc.Testing.GrpcTestFixture` or in-process gRPC testing
- Mock `IClusterClient` to return device snapshots
- For streaming, use Orleans memory streams if available, or mock stream subscriptions

**Alternative (Simpler)**:
- Test `DeviceService` methods directly without gRPC transport
- Verify that `GetAsync()` calls are made correctly
- Defer full gRPC transport testing to E2E (Docker Compose)

---

## Implementation Steps (Planning Only, Not Executed)

1. **Create test files** in `src/ApiGateway.Tests/`:
   - `GraphTraversalTests.cs`
   - `RegistryEndpointsTests.cs`
   - `TelemetryExportTests.cs`
   - `RegistryExportTests.cs`
   - `AuthenticationTests.cs`
   - `GrpcDeviceServiceTests.cs`

2. **Implement test cases** according to breakdown above:
   - Use `xUnit` for test structure
   - Use `FluentAssertions` for assertions
   - Use `Moq` for mocking `IClusterClient`, services, etc.
   - Leverage `ApiGatewayFactory` and `TestAuthHandler` from E2E tests

3. **Verify builds and tests pass**:
   - `dotnet build src/ApiGateway.Tests/ApiGateway.Tests.csproj`
   - `dotnet test src/ApiGateway.Tests/ApiGateway.Tests.csproj`

4. **Document test organization** in a new section of README or `docs/` if needed

5. **Future**: Integrate new tests into CI pipeline (if applicable)

---

## Progress

- [x] Create `GraphTraversalTests.cs` with ≥5 test cases
- [ ] Create `RegistryEndpointsTests.cs` with ≥10 test cases (2 per endpoint)
- [x] Create `TelemetryExportTests.cs` with ≥5 test cases
- [ ] Create `RegistryExportTests.cs` with ≥5 test cases
- [ ] Create `AuthenticationTests.cs` with ≥5 test cases
- [ ] Create `GrpcDeviceServiceTests.cs` with ≥3 test cases
- [x] Run `dotnet test` to verify all new tests pass
- [x] Verify no regressions in existing tests

Registry endpoint coverage: added `RegistryEndpointsTests.cs` that exercises each registry node type plus limit/export behaviors, leaving room for more cases to reach the planned test count.

---

## Observations

- `GraphTraversal` performs breadth-first traversal, honoring the requested depth and optional predicate filter. The new tests verify depth bounds, predicate filtering, zero-depth behavior, cycle handling, and that deeply nested nodes are included when the depth allows.
- `GraphRegistryTestHelper` consolidates the cluster/registry mocks. `RegistryEndpointsTests.cs` now ensures each registry endpoint’s node type is handled, along with limit boundaries and export branching.
- `TelemetryExportEndpoint` wraps `/api/telemetry/exports/{exportId}` logic, and `TelemetryExportEndpointTests.cs` covers 404/410/200 response branches with a real export file flow.
- Authentication coverage now uses `ApiGatewayTestFactory` with `TestAuthHandler` and an `Orleans__DisableClient` toggle so the in-process server exercises 401 responses and tenant-based grain resolution without connecting to an Orleans silo.

---

## Decisions

**Scope Definition**:
- Focus on unit/integration tests in `ApiGateway.Tests/`; defer full gRPC transport testing to E2E if needed
- Use mocked dependencies to avoid starting a full Orleans silo in unit tests
- Test tenant isolation at the API layer (request context); Orleans grain isolation tested separately

- **Test Infrastructure**:
- Leverage existing `TestAuthHandler` and `ApiGatewayFactory` for consistency
- Create helper methods for common setup (e.g., mock cluster, create test requests)
- Introduce `ApiGatewayTestFactory` and `TestAuthHandler` within `ApiGateway.Tests` so authentication behavior can be exercised without hitting RabbitMQ/Orleans dependencies.
- Use `Orleans__DisableClient` environment variable (and config overrides) to skip `UseOrleansClient` during HTTP-based tests.
- Introduce `GraphRegistryTestHelper` so GraphRegistryService and registry endpoint tests share cluster/export wiring without duplication
- Add `TelemetryExportEndpoint` to isolate HTTP result creation so the new endpoint tests can call it directly without wiring the entire Program.

**Design Notes**:
- Start coverage by exercising `GraphTraversal` directly so tests remain deterministic and do not require Orleans/HTTP plumbing before covering the higher-level endpoints.

**Priority**:
- High: Graph traversal, registry endpoints, export error paths (404/410)
- Medium: Authentication/authorization (tenant isolation)
- Low: Full gRPC streaming (defer to E2E)

---

## Retrospective

*To be filled after implementation.*
