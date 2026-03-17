# Task: Add live telemetry push to Telemetry Client

## Metadata
- Issue: Feature request (no GitHub Issue number provided)
- Status: In Progress
- Date: 2026-03-17

## Purpose
Add live telemetry updates to the TelemetryClient UI to provide real-time telemetry trend updates without requiring manual reload. The implementation should follow the pattern already established in AdminGateway's SignalR-based real-time updates.

## Success Criteria
- Selected telemetry trends refresh automatically without requiring manual reload
- The implementation is scoped to a clear tenant/device/point context
- Tests cover the chosen update strategy at a practical level
- Relevant docs/specs are updated
- `dotnet build` succeeds
- `dotnet test` passes (if tests are added)
- Local UI verification in TelemetryClient shows live updates

## Implementation Approach

Based on analysis of the codebase:

1. **Current State**:
   - TelemetryChart.razor already has polling implemented (2000ms interval)
   - AdminGateway has a working SignalR implementation (TelemetryHub.cs)
   - Orleans PointUpdates stream is already available

2. **Decision: Use SignalR for live push updates**:
   - Leverage existing TelemetryHub pattern from AdminGateway
   - Add SignalR client-side implementation to TelemetryClient
   - Replace polling with SignalR push mechanism
   - Keep polling as fallback for resilience

## Steps

1. Add SignalR server-side infrastructure to TelemetryClient
   - Add SignalR service registration in Program.cs
   - Create TelemetryHub similar to AdminGateway implementation
   - Configure Orleans client to access PointUpdates stream

2. Add SignalR client-side JavaScript integration
   - Create telemetry-realtime.js for SignalR connection management
   - Handle connection lifecycle (connect, disconnect, reconnect)
   - Emit events to Blazor components

3. Update TelemetryChart.razor component
   - Add SignalR integration via JS interop
   - Subscribe to point updates when chart is initialized
   - Update chart when receiving real-time data
   - Unsubscribe on component disposal

4. Add tests (if test infrastructure exists)
   - Test SignalR hub subscription/unsubscription
   - Test component integration

5. Update documentation
   - Update telemetry-client-spec.md to reflect SignalR implementation

## Progress
- [ ] Add SignalR server infrastructure
- [ ] Create TelemetryHub for TelemetryClient
- [ ] Add client-side JavaScript
- [ ] Update TelemetryChart component
- [ ] Add tests
- [ ] Update documentation
- [ ] Verify locally

## Observations
(To be filled as work progresses)

## Decisions
- **SignalR over polling**: SignalR provides true push updates with lower latency and better UX
- **Reuse AdminGateway pattern**: Proven implementation reduces risk
- **Keep minimal changes**: Only modify TelemetryClient, reuse existing Orleans infrastructure

## Verification Plan
- `dotnet build`
- `dotnet test` (if tests exist)
- Local Docker Compose deployment
- Manual UI testing: select a point, observe live updates

## Verification Results
(To be filled after verification)

## Retrospective
(To be filled after completion)
