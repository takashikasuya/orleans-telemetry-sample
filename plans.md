# plans.md

## Purpose
Document how the recent changes under `src/DataModel.Analyzer/Models/BuildingDataModel.cs` ripple through the analyzer, exporter, and Orleans integration layers so we can reproduce, understand, and fix the regressions before altering the implementation. This plan relies on the canonical definitions in `src/DataModel.Analyzer/Schema` and `src/DataModel.Analyzer/Models` and follows the workflow described in `docs/rdf-loading-and-grains.md` and `docs/telemetry-routing-binding.md`.

## Success Criteria
- `dotnet build` of the solution succeeds with the updated model definitions.
- `dotnet test` (at least `DataModel.Analyzer.Tests` and any impacted projects) pass to prove extraction, SHACL validation, and integration logic align.
- `docker compose up --build` completes, and we can hit the REST Swagger endpoint plus at least one Graph/Tenant read to confirm graph seeding still works (per `docs/rdf-loading-and-grains.md` and `README.md`).
- Any adjustments keep `src/DataModel.Analyzer/Schema` and `/Models` as the ground truth for the data model, and the docs (`docs/…`, `README.md`) stay in sync with the observed behavior.

## Steps
1. **Baseline the data model** – examine `src/DataModel.Analyzer/Models/BuildingDataModel.cs` along with `src/DataModel.Analyzer/Schema/building_model.shacl.ttl` so we understand the new shape (classes, properties, enums) that downstream code and docs must honor. Reference `docs/rdf-loading-and-grains.md` for the intended extraction order (Site → Building → Level → Area → Equipment → Point) and the canonization of point attributes.
2. **Audit RDF extraction/hierarchy** – trace each `Extract*` helper and `BuildHierarchy` inside `src/DataModel.Analyzer/Services/RdfAnalyzerService.cs` to confirm properties like `AreaUri`, `Point.EquipmentUri`, and the secondary collections/identifiers still line up with the adjusted model. Spot mismatches introduced by the model change (e.g., new fields not populated, renames, or changed defaults) and document where fixes are needed.
3. **Review downstream consumers** – verify `src/DataModel.Analyzer/Services/DataModelExportService.cs` and `src/DataModel.Analyzer/Integration/OrleansIntegrationService.cs` still build device contracts/graph seeds correctly with the modified data. Pay attention to location-path logic (`BuildLocationPath`), attribute attachment (`AddPointBindingAttributes`), and node attribute serialization so that graph seeding (observe `src/SiloHost/GraphSeeder.cs`) remains consistent with `docs/telemetry-routing-binding.md`.
4. **Align tests and documentation** – inspect `src/DataModel.Analyzer.Tests/RdfAnalyzerServiceTests.cs` and `src/DataModel.Analyzer.Tests/RdfAnalyzerServiceShaclTests.cs` to see which expectations failed. Update or extend TSV/SHACL fixtures to match the new model and add assertions if the change introduced new failure modes stated in `docs/rdf-loading-and-grains.md`. Cross-check `README.md` (RDF seeding sections around Graph & RDF Seeding) to keep examples consistent.
5. **Verify end-to-end behavior** – after touching code/tests, run `dotnet build`, run the relevant `dotnet test` suite, and exercise `docker compose up --build` to ensure the analyzer, exporter, and Orleans host initialize without errors. Validate the Swagger endpoint at `http://localhost:8080/swagger` and one Graph/point query (per `docs/telemetry-routing-binding.md`) to confirm the newer model still drives the ingestion/graph layers.

## Progress
- [ ] Baseline the updated data model versus schema/docs.
- [ ] Audit `RdfAnalyzerService` extraction hierarchies.
- [ ] Review export/integration logic and graph seeding.
- [ ] Update failing tests and documentation.
- [ ] Run build/test/compose verifications.

## Observations
- TBD after running inspections and tests.

## Decisions
- Treat `src/DataModel.Analyzer/Schema` and `src/DataModel.Analyzer/Models` as the definitive structure when resolving discrepancies rather than external docs.

## Retrospective
- TODO: capture what we learned and how the verification turned out once the plan executes.
