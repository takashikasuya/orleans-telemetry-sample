# Copilot Guidance for orleans-telemetry-sample

## Project context
- Solution targets **.NET 8**; nullable + implicit usings enabled (see `Directory.Build.props`).
- Key projects: DataModel.Analyzer (RDF → building model), ApiGateway (REST/gRPC), SiloHost (Orleans host), Publisher (sample telemetry).
- Prefer existing models/services: BuildingDataModel, RdfAnalyzerService, DataModelExportService, OrleansIntegrationService.
- Orleans: use memory grain storage/streams configured in SiloHost; device contracts from DataModelExportService.

## Coding style
- Use C# async/await; prefer `Task`-based APIs.
- Respect DI: register via `ServiceCollectionExtensions.AddDataModelAnalyzer`.
- Logging with `ILogger<T>`; avoid static loggers.
- Use `JsonSerializerOptions` from DataModelExportService when exporting JSON.

## Data model hints
- Hierarchy: Site → Building → Level → Area → Equipment → Point.
- Point properties: PointId, PointType, PointSpecification, Writable, Interval, Unit, Min/MaxPresValue, Scale.
- Level uses `EffectiveLevelNumber` helper; avoid reimplementing floor parsing.
- Custom properties/tags live on Agent-derived types.

## Orleans integration
- Use `OrleansIntegrationService.GenerateDeviceGrainKey` and `CreateInitializationData`.
- Point type mapping via `MapPointType`; add new mappings there if needed.

## Testing & tooling
- Tests use xUnit + FluentAssertions; add new tests to `DataModel.Analyzer.Tests`.
- For file outputs, honor `ProcessRdfFileAsync` contract: writes JSON, summary, Orleans contracts when output directory provided.

## Documentation
- Keep comments bilingual (JA/EN) where existing.
- Update README/PROJECT_OVERVIEW when adding new features or formats.

## Avoid
- Introducing new serialization libs; keep `System.Text.Json`.
- Blocking calls in async flows.
- Coupling analyzer logic to Orleans directly; use export/integration services instead.