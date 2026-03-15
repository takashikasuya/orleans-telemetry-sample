# Task: Port PR #78 control egress onto current src layout

## Metadata
- Issue: None (ad-hoc PR repair requested by user)
- Status: Completed

## Purpose
Repair the changes from PR #78 so they merge onto the current repository layout and correctly deliver control commands from ApiGateway to the RabbitMQ control queue.

## Success Criteria
1. The control egress changes apply cleanly to the current `src/Services` / `src/Tests` layout.
2. ApiGateway publishes RabbitMQ control messages in the format the current Publisher consumes.
3. Failed egress attempts update grain state and the immediate HTTP response consistently.
4. Targeted automated tests for ApiGateway and Telemetry.Ingest pass.
5. Repository build succeeds after the change.

## Steps
1. Inspect the old PR diff and the current repository layout.
2. Port the feature into current paths and add the missing dispatcher/grain updates.
3. Fix the RabbitMQ payload contract and response/state mismatch found in review.
4. Update related docs to reflect RabbitMQ egress support.
5. Run build and targeted tests.

## Progress
- [x] Step 1
- [x] Step 2
- [x] Step 3
- [x] Step 4
- [x] Step 5

## Observations
- The original PR targeted the pre-reorganization `src/ApiGateway` style layout and did not merge onto the current branch layout.
- The original RabbitMQ egress serialized `DesiredValue`, but the existing Publisher consumer only accepts `value`.
- The original response body would remain `Accepted` even after the grain was updated to `Failed` on egress rejection.

## Decisions
- Port the feature onto the current branch layout instead of trying to resolve a large structural merge on the old PR branch.
- Keep RabbitMQ as the only implemented egress target and document the remaining gaps for MQTT/Kafka/read-back confirmation.
- Emit a Publisher-compatible JSON payload while preserving extra metadata fields for future consumers.

## Verification Plan
- `dotnet test src/Tests/Unit/ApiGateway.Tests/ApiGateway.Tests.csproj`
- `dotnet test src/Tests/Unit/Telemetry.Ingest.Tests/Telemetry.Ingest.Tests.csproj`
- `dotnet build`

## Verification Results
- `dotnet test src/Tests/Unit/ApiGateway.Tests/ApiGateway.Tests.csproj` succeeded: 55 passed.
- `dotnet test src/Tests/Unit/Telemetry.Ingest.Tests/Telemetry.Ingest.Tests.csproj` succeeded: 21 passed.
- `dotnet build` succeeded with 3 pre-existing warnings (`ApiGateway.Client` nullability warning and 2 `AdminGateway.E2E.Tests` field warnings).

## Retrospective
- Porting the feature onto the current layout was less risky than rebasing the old PR branch across the repository reorganization.
- The key regression risk was the control queue payload contract; the new unit test now checks the concrete JSON shape expected by Publisher.
