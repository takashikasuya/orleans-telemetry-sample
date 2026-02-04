# plans.md

## Purpose
Add tests that verify RDF-derived spatial nodes and relationships are exported into GraphSeedData (space grains and edges) so we can confirm where space grains and their relationships are generated.

## Success Criteria
- New unit test(s) assert that `OrleansIntegrationService.CreateGraphSeedData` emits Site/Building/Level/Area nodes and `hasBuilding`/`hasLevel`/`hasArea` edges based on model hierarchy.
- `dotnet test` can run (not executed by agent unless requested).

## Steps
1. Extend `OrleansIntegrationServiceBindingTests` with spatial node/edge assertions.
2. Ensure the test model includes URIs and hierarchy references.
3. Update this plan with progress, decisions, and observations.

## Progress
- [x] Extend tests for spatial nodes/edges
- [x] Review assertions for correctness

## Observations
- Tests for point binding existed; spatial node/edge assertions were missing.

## Decisions
- Reused the existing `BuildModel` helper to keep test data consistent across binding checks.

## Retrospective
- Added coverage for spatial grain/edge generation without needing new fixtures.
