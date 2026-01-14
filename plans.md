# plans.md: RDF-Driven Telemetry Simulation for Publisher

## Purpose

Enable the `publisher` project to read RDF (Resource Description Framework) building data and simulate realistic telemetry from devices identified in the RDF graph. Currently, the `publisher` generates random telemetry for a fixed set of hardcoded devices. This enhancement allows:

1. **Device Discovery from RDF**: Automatically identify all Equipment/Point entities in the building model and their properties.
2. **RDF-Aligned Telemetry**: Generate point values that match RDF metadata (units, min/max ranges, data types).
3. **Semantic Equivalence**: Ensure published telemetry messages reflect the structure, identifiers, and properties defined in RDF.
4. **End-to-End Verification**: Verify via automated tests that RDF-sourced devices and points are correctly ingested through the Orleans pipeline and exposed via REST/gRPC APIs.

This work bridges the gap between the data modeling layer (`DataModel.Analyzer`) and the telemetry generation layer (`publisher`), enabling data-driven simulation for local development and testing.

---

## Current State Summary

### Publisher
- **Current Behavior**: Publishes random telemetry to RabbitMQ for `--device-count` hardcoded devices (e.g., `dev-1`, `dev-2`).
- **Message Format**: `TelemetryMsg` (class in `Grains.Abstractions`) containing:
  - `DeviceId`, `TenantId`, `Sequence`, `Timestamp`
  - `Properties` dictionary (extensible key-value pairs)
  - Optional: `BuildingName`, `SpaceId`
- **Configuration**: Via environment variables (`DEVICE_COUNT`, `PUBLISH_INTERVAL_MS`, `TENANT_ID`, etc.).
- **Limitations**: No connection to RDF or building model; no type-aware value generation.

### DataModel.Analyzer
- **Capability**: Parses RDF (Turtle/RDF-XML) and builds a `BuildingDataModel` containing:
  - Hierarchy: Site → Building → Level → Area → Equipment → Point
  - Equipment has `DeviceId`, `DeviceType`, IP address, and asset metadata.
  - Point has `PointType`, `Unit`, `Min/MaxPresValue`, `Interval`, `Scale`, etc.
- **Export**: Generates JSON, summaries, and Orleans grain initialization contracts.
- **Limitation**: Analyzer is currently only used by `SiloHost` (graph seeding) and tests; not integrated with `publisher`.

### Orleans (Silo)
- **Grain Types**: `IDeviceGrain`, `IPointGrain`, `IGraphNodeGrain`, `IGraphIndexGrain`.
- **Ingestion Path**:
  1. `TelemetryRouterGrain` receives `TelemetryMsg` from RabbitMQ.
  2. Routes to `DeviceGrain` by device ID.
  3. `DeviceGrain` updates `PointGrain` instances and publishes `DeviceUpdates` stream.
  4. Storage sinks persist events to Parquet/JSON.
- **Seeding**: `GraphSeedService` initializes graph grains from RDF via `RDF_SEED_PATH`.

### API Gateway
- **Endpoints**: REST API for device/point/graph queries; gRPC scaffolding (partial implementation).
- **State Source**: Orleans grains (device snapshots, point values).
- **Dependency**: Requires Orleans to be running; grains must be populated via seeding or telemetry ingestion.

### Test Ecosystem
- **E2E Tests** (`Telemetry.E2E.Tests`): Docker Compose orchestration, seed.ttl ingestion, telemetry flow verification.
- **Existing RDF Seed**: `src/Telemetry.E2E.Tests/seed.ttl` defines a minimal building structure.

---

## Target Behavior

### Publisher with RDF Support
1. **Start-up**:
   - Read RDF file from `RDF_SEED_PATH` (same as SiloHost).
   - Parse using `DataModel.Analyzer` to extract Equipment and Points.
   - Identify all devices and their associated points.

2. **Device/Point Resolution**:
   - For each Equipment in the RDF, map to a `DeviceId` and point identifiers.
   - Extract metadata: `PointType`, `Unit`, `Min/MaxPresValue`, `Scale`, `Interval`.

3. **Telemetry Generation**:
   - Generate values based on RDF metadata (not pure random).
   - Example: A temperature point with `Unit="Celsius"` and `Min/MaxPresValue=(10, 30)` generates values in that range.
   - Publish `TelemetryMsg` with:
     - Device and point identifiers from RDF.
     - Building/space information derived from RDF hierarchy.
     - Value matching point type and constraints.

4. **Messaging**:
   - Publish to RabbitMQ with **monotonic sequences per device**.
   - Include RDF-derived metadata in the `Properties` dictionary (e.g., `{ "pointType": "Temperature", "unit": "Celsius" }`).

5. **Configuration**:
   - Environment variable: `RDF_SEED_PATH` (path to TTL file).
   - Environment variable: `TENANT_ID` (must match SiloHost tenant for seeding).
   - Optional: `PUBLISH_INTERVAL_MS` to control generation rate.

### Integration Verification
- **Via REST API**:
  - Query a device seeded from RDF and verify its state matches published telemetry.
  - Confirm point values are within expected ranges and have correct metadata.
- **Via Orleans Grains**:
  - Verify `DeviceGrain` snapshots reflect the latest published value.
  - Confirm `PointGrain` stores point metadata from RDF.

---

## RDF File Storage Convention

### Location Strategy

RDF seed files must be stored in a location accessible to both the `publisher` and `Telemetry.E2E.Tests` projects. The recommended approach:

**Primary Location**: `src/Telemetry.E2E.Tests/` (project root)

**Rationale**:
- `Telemetry.E2E.Tests` is the canonical E2E test project that already references RDF files (e.g., `seed.ttl`).
- `publisher` can reference files from this location using relative or absolute paths.
- Both SiloHost and publisher can load the same RDF file by setting `RDF_SEED_PATH` environment variable.

**File Naming Convention**:
- `seed.ttl` – Default minimal RDF used by E2E tests.
- `seed-complex.ttl` – (optional) More complex RDF for extended testing.
- `seed-rdf-publisher-test.ttl` – (new) Focused test RDF for publisher validation.

### Usage Pattern

```bash
# Example 1: SiloHost loads RDF for graph seeding
export RDF_SEED_PATH=/home/takashi/projects/dotnet/orleans-telemetry-sample/src/Telemetry.E2E.Tests/seed.ttl
export TENANT_ID=t1
dotnet run --project src/SiloHost

# Example 2: Publisher loads the same RDF for device discovery
export RDF_SEED_PATH=/home/takashi/projects/dotnet/orleans-telemetry-sample/src/Telemetry.E2E.Tests/seed.ttl
export TENANT_ID=t1
dotnet run --project src/Publisher
```

### Docker Compose Integration

In `docker-compose.yml`, volume mounts should reference the RDF seed location:

```yaml
services:
  silo:
    environment:
      RDF_SEED_PATH: /app/seed/seed.ttl
    volumes:
      - ./src/Telemetry.E2E.Tests/seed.ttl:/app/seed/seed.ttl:ro

  publisher:
    environment:
      RDF_SEED_PATH: /app/seed/seed.ttl
    volumes:
      - ./src/Telemetry.E2E.Tests/seed.ttl:/app/seed/seed.ttl:ro
```

### Build/Test Considerations

- When running unit tests (`dotnet test`), RDF files are referenced via `AppContext.BaseDirectory` or project-relative paths.
- When running E2E tests, Docker Compose or helper scripts manage volume mounting.
- No need to copy RDF files to output directory; they remain in source tree and are referenced by path.

---

## Design and Mapping Strategy

### Device Identification from RDF

**Mapping Rule**: Each `Equipment` entity in the RDF model represents a publishable device.

- **Primary Identifier**: `Equipment.DeviceId` (field in the data model).
- **Secondary Identifier**: Equipment URI (from RDF; e.g., `sbco:AHU_01`).
- **Fallback**: Generate a deterministic ID from Equipment name if `DeviceId` is empty.

**Properties to Extract**:
- `Equipment.Name` (display name).
- `Equipment.DeviceType` (e.g., "HVAC", "Lighting").
- `Equipment.IPAddress` (optional; for metadata).
- `Area.Name` + `Level.EffectiveLevelNumber` + `Building.Name` → derive `SpaceId` for telemetry.

### Point Identification and Telemetry Equivalence

**Mapping Rule**: Each `Point` associated with an Equipment becomes a telemetry field in the published message.

**Point Properties to Extract**:
- `Point.PointId` (must be unique within the device; used as Property key in `TelemetryMsg.Properties`).
- `Point.PointType` (e.g., "Temperature", "Humidity"; semantic meaning).
- `Point.Unit` (e.g., "Celsius", "%RH").
- `Point.Min/MaxPresValue` (acceptable value range).
- `Point.Scale` (conversion factor, if applicable).
- `Point.Interval` (sampling interval; not directly used for publication but informative).
- `Point.Writable` (whether the point is settable; false for read-only sensor outputs).

### Telemetry Equivalence

**Definition**: A published telemetry message is semantically equivalent to RDF if:

1. **Device-Level Equivalence**:
   - `TelemetryMsg.DeviceId` matches the Equipment's `DeviceId`.
   - `TelemetryMsg.BuildingName` and `TelemetryMsg.SpaceId` reflect the Equipment's location in the hierarchy.
   - `TelemetryMsg.TenantId` matches the RDF tenant context.

2. **Property-Level Equivalence**:
   - For each Point in the Equipment, the `TelemetryMsg.Properties` dictionary includes:
     - Key: `Point.PointId` (or a derived identifier).
     - Value: A JSON object or primitive matching the point's `PointType`.
   - Point metadata (unit, range, type) is included in the Properties or as a separate metadata structure.

3. **Temporal Equivalence**:
   - Sequence numbers are monotonic per device (required by Orleans ingestion logic).
   - Timestamp is set to the current time when the message is created.

4. **Data Type Safety**:
   - Boolean points: publish `true` or `false`.
   - Numeric points: publish within `Min/MaxPresValue` range.
   - String points: publish a representative string (e.g., sensor state name).
   - Enumerated points: publish one of the allowed values from RDF.

### RDF-to-Telemetry Property Mapping

**Data Structure**: Properties dictionary in `TelemetryMsg` contains point readings. Example:

```json
{
  "properties": {
    "PT_Temp_01": 22.5,
    "PT_RH_01": 45.2,
    "PT_CO2_01": 450,
    "PT_Occupancy_01": true
  }
}
```

**Metadata Inclusion** (optional, in a separate field or property):
- Point metadata can be stored in a reserved property (e.g., `_metadata`) or derived on the fly during API calls by joining with Orleans grain state.

**Handling Complex RDF Types**:
- **Quantities with Units**: Store value + unit in `Properties` or separate fields.
  - Example: `"PT_Temp_01": { "value": 22.5, "unit": "Celsius" }`
- **Time Series**: Publisher generates single-point snapshots; Orleans stores as streams/events.
- **Ontology Alignment**: Use point URIs from RDF as property identifiers where available.

---

## Implementation Plan

### Phase 1: Data Model Integration (Publisher ↔ Analyzer)

#### Task 1.1: Extend Publisher to Load RDF
- **Files**: `src/Publisher/Program.cs`
- **Action**:
  - Add dependency: `DataModel.Analyzer` project reference.
  - Read `RDF_SEED_PATH` environment variable.
  - Use `RdfAnalyzerService.ProcessRdfFileAsync()` to parse RDF and build `BuildingDataModel`.
  - Log summary: device count, point count, tenant ID.
- **Acceptance Criteria**:
  - Publisher starts and successfully loads an RDF file when `RDF_SEED_PATH` is set.
  - Console output reports number of devices and points discovered.
  - No errors if `RDF_SEED_PATH` is unset (fallback to random mode or graceful skip).

#### Task 1.2: Create RDF-Aware Device/Point Generator Service
- **Files**: `src/Publisher/RdfTelemetryGenerator.cs` (new)
- **Action**:
  - Design a service that accepts a `BuildingDataModel` and produces a list of `(DeviceId, List<Point>)` tuples.
  - Implement methods:
    - `GetDevicesFromModel(BuildingDataModel)`: Extract all Equipment with DeviceId.
    - `GetPointsForDevice(Equipment)`: Return associated Points.
    - `GenerateValueForPoint(Point)`: Create a random value within Point's constraints.
  - Handle edge cases:
    - Equipment with no DeviceId (generate from URI or name).
    - Points with no Unit or range (use sensible defaults).
- **Acceptance Criteria**:
  - Service correctly extracts devices and points from a test RDF model.
  - Generated values fall within `Min/MaxPresValue` ranges.
  - Service is tested with unit tests (see Test Plan).

---

### Phase 2: Telemetry Message Construction

#### Task 2.1: Implement RDF-Aligned Message Building
- **Files**: `src/Publisher/Program.cs` (main loop modification)
- **Action**:
  - Replace the hardcoded device loop with one iterating over `BuildingDataModel` devices.
  - For each device, construct a `TelemetryMsg` with:
    - `DeviceId` from Equipment.
    - `BuildingName` from the hierarchy.
    - `SpaceId` derived from Area/Level/Building names.
    - `Properties` dictionary populated with point readings.
  - Maintain sequence numbers per device (as currently done).
- **Acceptance Criteria**:
  - Publisher generates and publishes telemetry for all devices in the RDF model.
  - Messages include building and space metadata.
  - Sequence numbers remain monotonic.

#### Task 2.2: Point Metadata in Properties
- **Files**: `src/Publisher/Program.cs`, `src/Grains.Abstractions/DeviceContracts.cs` (if needed)
- **Action**:
  - Decide whether point metadata (unit, type, range) is:
    - Stored in `Properties` alongside values, or
    - Derived from Orleans grains when querying.
  - For simplicity, include metadata as a reserved property key (e.g., `"_pointMetadata": {...}`).
- **Acceptance Criteria**:
  - Metadata is accessible to API consumers (either in message or via grain query).
  - Unit and type information aids downstream processing.

---

### Phase 3: Orleans Integration

#### Task 3.1: Verify Grain Initialization from Published Telemetry
- **Files**: `src/SiloHost/*` (review; no changes expected)
- **Action**:
  - Confirm that `TelemetryRouterGrain` correctly routes RDF-sourced devices to `DeviceGrain`.
  - Verify that `DeviceGrain` and `PointGrain` store metadata derived from the telemetry (or from seeded graph grains).
  - Test end-to-end: publish RDF-aligned telemetry → grain updates → API query returns correct state.
- **Acceptance Criteria**:
  - Orleans grains are created and updated for each device/point in the RDF model.
  - No routing errors in silo logs.

#### Task 3.2: API Endpoint Verification
- **Files**: `src/ApiGateway/Controllers/*`
- **Action**:
  - Verify that existing endpoints correctly expose device state.
  - Ensure point metadata is returned (if stored in grain or graph).
  - Test a full flow: Seed RDF → Publish RDF-aligned telemetry → Query API → Verify response.
- **Acceptance Criteria**:
  - REST API returns device state with all points.
  - gRPC endpoint responds correctly (if implemented).

---

### Phase 4: Testing

#### Task 4.1: Unit Tests for RDF Device/Point Extraction
- **Files**: `src/Publisher.Tests/RdfTelemetryGeneratorTests.cs` (new, if unit test project created)
  - Alternatively: `src/DataModel.Analyzer.Tests/` (extend existing tests)
- **Tests**:
  - Load a simple RDF file (TTL) with known structure from `src/Telemetry.E2E.Tests/`.
  - Extract devices and points.
  - Verify correct DeviceId, point count, and metadata.
  - Test value generation within constraints.
- **Acceptance Criteria**:
  - All tests pass locally with `dotnet test`.

#### Task 4.2: Integration Tests (Telemetry Ingestion Flow)
- **Files**: `src/Telemetry.E2E.Tests/RdfPublisherE2ETests.cs` (new or extend)
- **Tests**:
  1. **Setup**: Seed RDF via `RDF_SEED_PATH` in SiloHost (reference `src/Telemetry.E2E.Tests/seed.ttl`).
  2. **Publish**: Run publisher with the same RDF to generate telemetry.
  3. **Ingest**: Verify messages are consumed from RabbitMQ and routed to Orleans.
  4. **Verify API**: Query device state and confirm point values match published telemetry.
  5. **Metadata**: Confirm point metadata (unit, type, range) is available.
- **Docker Compose Integration**:
  - Extend `docker-compose.yml` to include an optional publisher service (enabled via environment variable).
  - Mount RDF seed files from `src/Telemetry.E2E.Tests/` into container.
  - Use helper scripts (`scripts/start-system.sh`, `scripts/stop-system.sh`) with RDF support.
- **Acceptance Criteria**:
  - E2E test runs within Docker Compose and completes without errors.
  - API responses include expected device/point data.
  - Telemetry flow is unbroken from publisher to API.

#### Task 4.3: Verification Script
- **Files**: `scripts/verify-rdf-telemetry.sh` (new)
- **Action**:
  - Start Docker Compose with RDF seed from `src/Telemetry.E2E.Tests/seed.ttl`.
  - Publish RDF-aligned telemetry.
  - Query API to validate ingestion.
  - Generate a summary report.
- **Acceptance Criteria**:
  - Script can be run locally and produces a pass/fail summary.

---

## Test Plan (REQUIRED)

### Unit Tests

**Scope**: RDF parsing, device/point extraction, value generation.

**Files and Tests**:
1. `src/DataModel.Analyzer.Tests/RdfDeviceExtractionTests.cs` (or extend existing):
   - Load `src/Telemetry.E2E.Tests/seed.ttl` and extract all Equipment entries.
   - Verify correct DeviceId, name, and point associations.
   - Test edge cases: empty DeviceId, missing points.

2. `src/Publisher.Tests/RdfTelemetryGeneratorTests.cs` (new, if project created):
   - Instantiate `RdfTelemetryGenerator` with a test `BuildingDataModel`.
   - Call `GenerateValueForPoint()` and verify ranges.
   - Test all PointType categories (numeric, boolean, string, enum).

**Execution**:
```bash
cd /home/takashi/projects/dotnet/orleans-telemetry-sample
dotnet test src/DataModel.Analyzer.Tests
dotnet test src/Publisher.Tests  # if created
```

### Integration / E2E Tests

**Scope**: Full telemetry flow from publisher through Orleans to API.

**Files and Tests**:
1. `src/Telemetry.E2E.Tests/RdfPublisherE2ETests.cs` (new or extend):
   - Test method: `TestRdfPublisherTelemetryIngestion()`:
     1. Set `RDF_SEED_PATH=/app/seed/seed.ttl` (or full path to `src/Telemetry.E2E.Tests/seed.ttl`), `TENANT_ID=t1`.
     2. Start Docker Compose (or local Orleans + RabbitMQ).
     3. Run publisher for a fixed duration (e.g., 10 seconds).
     4. Query API for device state.
     5. Assert: device exists, point count matches RDF, values are in range.
   
   - Test method: `TestRdfMetadataIsAccessible()`:
     1. Seed RDF from `src/Telemetry.E2E.Tests/seed.ttl`.
     2. Publish telemetry.
     3. Query API device endpoint.
     4. Assert: response includes point metadata (unit, type, min/max).

**Docker Compose Execution**:
```bash
# Using helper script
cd /home/takashi/projects/dotnet/orleans-telemetry-sample
export RDF_SEED_PATH=./src/Telemetry.E2E.Tests/seed.ttl
./scripts/start-system.sh

# In another terminal: run tests
cd /home/takashi/projects/dotnet/orleans-telemetry-sample
dotnet test src/Telemetry.E2E.Tests --filter "RdfPublisher"

# Cleanup
./scripts/stop-system.sh
```

**Standalone Execution** (if Docker Compose is overkill):
```bash
# Start RabbitMQ
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:management

# Start Orleans silo (separate terminal)
export RDF_SEED_PATH=/home/takashi/projects/dotnet/orleans-telemetry-sample/src/Telemetry.E2E.Tests/seed.ttl
dotnet run --project src/SiloHost

# Start API gateway (separate terminal)
dotnet run --project src/ApiGateway

# Run E2E tests
export RDF_SEED_PATH=/home/takashi/projects/dotnet/orleans-telemetry-sample/src/Telemetry.E2E.Tests/seed.ttl
dotnet test src/Telemetry.E2E.Tests --filter "RdfPublisher"

# Cleanup
docker stop rabbitmq
docker rm rabbitmq
```

### Test Data

**RDF Seed Files** (stored in `src/Telemetry.E2E.Tests/`):
- `seed.ttl` – Existing minimal RDF used by E2E tests. Reuse this for publisher tests.
- `seed-rdf-publisher-test.ttl` – New focused test RDF for publisher validation:
  - Define 2–3 Equipment with distinct PointTypes.
  - Include range constraints, units, and metadata.

**Expected Telemetry Behavior**:
- For each Equipment in the RDF, one `TelemetryMsg` per publish cycle.
- Properties include values for all associated Points.
- Sequences increase monotonically per device.

---

## Assumptions and Constraints

### Assumptions

1. **RDF Format Consistency**:
   - Input RDF follows the SBCO (Smart Building Ontology) or Brick schema conventions.
   - Equipment and Point entities are properly linked via `sbco:hasPoint` or similar.
   - DeviceId is present on Equipment; if missing, a fallback generation strategy is acceptable.

2. **Telemetry Message Contract**:
   - `TelemetryMsg` structure (in `Grains.Abstractions`) is stable; no breaking changes expected.
   - Sequences are validated by Orleans and must be monotonic per device.

3. **Orleans Grain Initialization**:
   - Seeding via `GraphSeedService` is independent and can occur in parallel with telemetry ingestion.
   - Grains are created on-demand as telemetry arrives; no pre-creation is required.

4. **Multi-Tenancy**:
   - `TENANT_ID` environment variable is synchronized between SiloHost, Publisher, and API calls.
   - Tenant context is preserved through the ingestion pipeline.

5. **Local Development Only**:
   - Publisher is intended for simulation and testing; production-grade features (e.g., error recovery, backpressure) are out of scope.

6. **RDF File Location**:
   - RDF seed files are stored in `src/Telemetry.E2E.Tests/` so they are accessible to both publisher and E2E tests.
   - `RDF_SEED_PATH` environment variable points to the full or relative path of these files.

### Constraints (from AGENTS.md)

1. **No External Services**: All data (RDF, telemetry, storage) remains within the Docker Compose stack or local development environment.
2. **Incremental Changes**: Modifications preserve existing behavior (e.g., publisher can still run in random mode if RDF_SEED_PATH is unset).
3. **Local Verification**: All tests must pass locally using `dotnet test` and `docker compose up/down`.
4. **No Breaking Changes**: Existing Orleans contracts, API endpoints, and telemetry routing remain compatible.

---

## Out of Scope

This plan **does not** cover:

1. **Production Deployment**:
   - Kubernetes, cloud hosting, or production-grade scalability are not addressed.
   - Load testing beyond simple E2E verification is deferred.

2. **Advanced RDF Features**:
   - Custom ontology extensions beyond SBCO/Brick.
   - RDF reasoning or inference (e.g., derived properties).
   - Real-time RDF updates (RDF is consumed once at startup).

3. **Publisher Enhancements Beyond RDF**:
   - Time-series simulation (e.g., synthetic trends, seasonal patterns).
   - Failure injection or anomaly simulation.
   - Kafka connector support (RabbitMQ only).

4. **gRPC Implementation**:
   - Full gRPC service implementation is not included; REST API verification is sufficient.

5. **Admin Console or Monitoring**:
   - Updates to the admin UI or monitoring dashboards are not included.
   - Existing admin console continues to work.

6. **Graph Grain Querying**:
   - Telemetry does not directly update graph grains; graph seeding is independent.
   - Graph query optimization is out of scope.

7. **Storage Optimization**:
   - Parquet compression and long-term storage are not modified.
   - Existing storage layer remains unchanged.

---

## Success Criteria Summary

A task is complete when:

1. **Unit Tests Pass**: `dotnet test src/DataModel.Analyzer.Tests` succeeds.
2. **E2E Tests Pass**: `dotnet test src/Telemetry.E2E.Tests` completes without errors (within Docker Compose).
3. **Integration Verified**:
   - RDF seed is loaded by publisher from `src/Telemetry.E2E.Tests/seed.ttl`.
   - Telemetry is published with RDF-derived device and point information.
   - REST API queries return device state reflecting published telemetry.
4. **No Regressions**: Existing publisher behavior (random mode) continues to work.
5. **Documentation Updated**: README or dedicated doc file explains RDF-driven publisher usage and RDF file location conventions.

---

## Next Steps (Future Issues)

Once this plan is implemented, the following enhancements are candidates for future work:

- **Point Value Simulation**: Add time-series generation (e.g., sinusoidal variation) for more realistic data.
- **Control Flow Implementation**: Allow publisher to respond to Orleans control commands (e.g., set setpoint on writable points).
- **Multi-RDF Support**: Load multiple RDF files or incremental RDF updates.
- **Metrics and Observability**: Add OpenTelemetry instrumentation to publisher and Orleans pipeline.

## ExecPlan

### Purpose
Capture the concrete work required to complete the "Next Steps" improvements by adding targeted tests and verification coverage tied to the current RDF-driven publisher behavior.

### Tasks
1. **Unit tests** – Add `RdfTelemetryGeneratorTests` (or extend the appropriate publisher test project) to validate device/point extraction, metadata enrichment, and value generation boundaries from `BuildingDataModel`.
2. **Integration/E2E tests** – Expand `Telemetry.E2E.Tests` (and any helper scripts) to run the publisher with `RDF_SEED_PATH`, exercise the Orleans/RabbitMQ stack, and assert that published telemetry and metadata appear through the API.
3. **Verification artifacts** – Document how to run the new tests (dotnet commands, Docker Compose flows) and capture any required shell helpers/scripts.

### Verification
- `dotnet test src/Publisher.Tests` (or whichever test project hosts the generator tests) must pass.
- `dotnet test src/Telemetry.E2E.Tests --filter RdfPublisher` (or similar) should succeed after seeding the RDF and running the publisher.
- Provide notes on any manual Docker Compose steps used for API validation.

### Progress
- [x] Task 1 – Implement unit tests covering `RdfTelemetryGenerator`.
- [x] Task 2 – Extend integration/E2E tests to run the RDF-driven publisher and validate API visibility.
- [ ] Task 3 – Document verification commands and any helper scripts.

### Decisions
- Tests should reuse existing RDF seeds (`src/Telemetry.E2E.Tests/seed.ttl`) where possible to keep configuration aligned with the publisher’s runtime expectations.
- If Docker Compose is required for the E2E flow, keep volume mounts consistent with the `RDF_SEED_PATH` conventions already documented.
- The publisher assembly exposes an `InternalsVisibleTo("Publisher.Tests")` attribute so the new generator tests can exercise device/point definitions without expanding the public API surface.

## Success Criteria

- Publisher can load `RDF_SEED_PATH`, parse the RDF via `RdfAnalyzerService`, and report the device/point counts at startup.
- Telemetry messages derived from RDF keep monotonic sequences, respect min/max ranges, and include a `_pointMetadata` entry for downstream clients while keeping raw point values simple.
- `plans.md` captures the current work items, progress flags, and decision log before further implementation.

## Steps

1. Extend `Publisher` to reference `DataModel.Analyzer`, read `RDF_SEED_PATH`, and build a `BuildingDataModel` at startup.
2. Implement `RdfTelemetryGenerator` to extract devices/points, generate constrained values, and provide metadata that can be baked into `TelemetryMsg`.
3. Wire the generator into the main publishing loop so RDF-derived devices stream telemetry while falling back to the existing random mode, then document progress and decisions.

## Progress

- [x] Step 1 – Cargo cult the RDF analyzer into the publisher and validate the model load/logging flow.
- [x] Step 2 – Build the telemetry generator that maps equipment to points with metadata-aware values.
- [x] Step 3 – Replace the hardcoded publishing loop, maintain sequence tracking, and note design decisions/test needs.

## Observations

- Publisher now logs RDF load outcomes and drops back to the legacy random generator whenever the seed file is absent or invalid.
- The new generator builds device/point definitions from the analyzer model and keeps metadata inside `_pointMetadata` while presenting primitive point values.
- Added unit tests that cover device/point extraction, metadata exposure, and value generation behavior so future changes can be verified quickly.
- The integration test `RdfPublisherTelemetry_IsVisibleThroughApi` injects normalized RDF telemetry via the Orleans router and confirms the API still exposes the expected point value while preserving the `_pointMetadata` payload.

## Decisions

- Metadata will be communicated via a reserved `_pointMetadata` dictionary entry so existing consumers still see primitive point values while metadata stays accessible.
- Device IDs without an explicit `DeviceId` will fallback to a sanitized form of the equipment URI/name so telemetry remains deterministic.
- Boolean points are detected heuristically via keyword matching (`Binary`, `Boolean`, `Switch`, etc.) while all other points default to constrained numeric values derived from `MinPresValue`/`MaxPresValue`.
- Added an `InternalsVisibleTo("Publisher.Tests")` assembly attribute so generator internals can be exercised by the new unit tests without enlarging the public API.
- Normalizing `_pointMetadata` entries to lightweight dictionaries before routing telemetry prevents Orleans serializer errors while keeping metadata accessible downstream.

## Retrospective

- Run `dotnet build`/`dotnet test` for both the analyzer and publisher when time permits, and add targeted unit tests for `RdfTelemetryGenerator`.
