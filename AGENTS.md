# AGENTS

This document defines guardrails and procedures for AI agents operating on this repository.

---

## Repository Overview

**orleans-telemetry-sample** is a sample solution demonstrating a telemetry ingestion pipeline.  
It ingests messages from RabbitMQ into an Orleans cluster, routes them to device-scoped grains, and exposes state via REST and gRPC.  
Services include:
- `mq`: RabbitMQ broker for incoming telemetry.
- `silo`: Orleans host with RabbitMQ consumer and grain logic.
- `api`: ASP.NET Core application providing REST and gRPC APIs.
- `connector`: .NET console app publishing sample telemetry. [oai_citation:1‡GitHub](https://github.com/takashikasuya/orleans-telemetry-sample)

The sample is intended for **local development and experimentation**, not production. [oai_citation:2‡GitHub](https://github.com/takashikasuya/orleans-telemetry-sample)

---

## Scope of AI Agent Work

1. **Operate solely within this repository**; do not access external network resources unless explicitly instructed.  
2. **Favor incremental, focused changes** that retain existing behavior and do not introduce broad refactors.  
3. Base all actions on observable, verifiable behaviors: successful build, passing tests, and local run results.

---

## Goals & Success Criteria

Before executing any change or implementation task, an agent must establish:

1. **Purpose of change or task**: what user value or feature is delivered.
2. **Success conditions**: concrete, testable outcomes (e.g., tests pass, service starts, REST/gRPC endpoint responds).  
3. **Verification steps**: commands and checks that demonstrate correctness (see below).  

Unless success conditions and verification steps are clearly articulated, do not implement code changes.

---

## Standard Commands (Observable Behavior)

The following commands should be used to build and validate the repository:

- **Build**
  ```bash
  dotnet build
  ```

- **Test**
  ```bash
  dotnet test
  ```

- **Local Run via Docker Compose**
  ```bash
  docker compose up --build
  ```

After startup, verify:
- REST API swagger at: http://localhost:8080/swagger
- GRPC service responds to client calls
(adjust ports/config as in README).

---

## Required Reading Documents

Before starting any work, agents **must** consult the following documents:

1. **`plans.md`** (MANDATORY)
   - The primary source of truth for current task direction and progress.
   - Contains purpose, success criteria, steps, observations, decisions, and retrospective.
   - Must be read before beginning any task and updated incrementally throughout work.
   - If plans.md does not exist or is incomplete for the current task, create or update it before proceeding.

2. **`PROJECT_OVERVIEW.md`**
   - High-level architecture and workflow overview.

3. **`README.md`**
   - Sample service startup sequence and basic usage.

4. **`docs/`**
   - Technical documentation covering specific subsystems.

---

## Entry Points and Key Paths

Agents should use the following as guides to understand structure:
- `src/`: Solution source code.
- `PROJECT_OVERVIEW.md`: High-level design and workflow.
- `README.md`: Sample service startup sequence.
- `docs/`: Technical documentation covering specific subsystems and workflows.
  - `admin-console.md`: Admin console details.
  - `rdf-loading-and-grains.md`: RDF data loading and grain initialization.
  - `telemetry-connector-ingest.md`: Telemetry connector and ingestion flow.
  - `telemetry-ingest-loadtest.md`: Load testing methodology.
  - `telemetry-routing-binding.md`: Telemetry routing and binding logic.
  - `telemetry-storage.md`: Storage layer and persistence.
- Orleans grain and stream definitions in silo project (device routing, ingestion).
- API controllers in api project exposing REST and gRPC.
Refer to these for structural context before modifying code. 

---

## Testing and Verification

1. **Automated Tests**
   - Run all unit tests with the default dotnet test.
   - Document any new tests required for new functionality.
2. **Integration Verification (Local Only)**
   - Confirm that Docker Compose stack starts without errors.
   - Validate REST endpoints via Swagger client.
   - Validate telemetry ingestion by injecting sample telemetry and confirming device grain state updates (as observable through API).

Agents must clearly document which verification steps were executed by the agent and which require local execution.

---

## Consistency Checks for Changes

Before finalizing any code change, agents must verify:

1. **Build Integrity**
   - Run `dotnet build` to ensure no compilation errors.
   - All projects in the solution must build successfully.

2. **Test Integrity**
   - Run `dotnet test` to ensure all existing tests pass.
   - Add new tests for new functionality.

3. **Documentation Alignment**
   - Update `plans.md` with the actual changes made.
   - Verify that README.md, PROJECT_OVERVIEW.md, and docs/ remain accurate.
   - Update inline code comments if behavior changes.

4. **API Contract Stability**
   - Public APIs should not change unless explicitly required.
   - Breaking changes must be documented in plans.md with justification.

5. **Configuration Consistency**
   - Verify that docker-compose.yml, Dockerfile, and appsettings.json remain consistent.
   - Test with `docker compose up --build` if configuration changes are made.

6. **Dependency Integrity**
   - Ensure NuGet package versions are compatible.
   - Avoid introducing unnecessary dependencies.

---

## Definition of Done

A task is considered complete when all of the following criteria are met:

1. **Code Implementation**
   - All code changes are implemented as specified in plans.md.
   - Code follows existing conventions (C# 12, .NET 8, async/await patterns).
   - No placeholder or TODO comments remain in production code.

2. **Build & Test Success**
   - `dotnet build` completes with zero errors.
   - `dotnet test` passes all tests (or new tests are added and passing).
   - No new warnings introduced (unless documented in plans.md).

3. **Integration Verification**
   - `docker compose up --build` starts successfully (if applicable).
   - REST API endpoints respond correctly (verified via Swagger or curl).
   - gRPC services respond correctly (if applicable).
   - Telemetry ingestion works end-to-end (if applicable).

4. **Documentation Complete**
   - `plans.md` updated with final outcomes and retrospective.
   - All observations, decisions, and trade-offs documented.
   - README.md or relevant docs/ files updated if behavior changed.

5. **Cleanup & Review**
   - No debug code, console logs, or temporary files left in repository.
   - Code is idiomatic and maintainable.
   - Changes are minimal and focused on the stated purpose.

6. **Agent Confirmation**
   - Agent explicitly confirms all criteria above are met.
   - Agent identifies any manual verification steps required by user.

If any criterion cannot be met, document the blocker in plans.md under "Clarification Needed" or "Blockers" section.

---

## Coding Conventions

- Language: C# 12 / .NET 8.
- Public API changes should be minimized.
- Use concise, behavior-focused comments when logic is non-obvious.
- Avoid broad refactors unless explicitly requested.

---

## Recording and Documentation

Every non-trivial task must be accompanied by:
1. plans.md file at the repository root containing:
   - Purpose of the change.
   - Concrete Steps to implement and verify.
   - Progress checklist items.
   - Observations and surprises encountered.
   - Decision log describing alternatives and chosen solution.
   - Outcomes and retrospective notes after verification.
2. Commit messages should reference the plans.md context.
3. For multi-step work, update plans.md incrementally.

Agents must treat plans.md as the primary source of truth for task direction and progress.

---

## Allowed External Access

Agents **may** access the following external resources for standard development tasks:
- **NuGet package registry** (`nuget.org`) for restoring and updating .NET dependencies via `dotnet restore`, `dotnet build`, or `dotnet add package`.
- **Docker image registries** (Docker Hub, `mcr.microsoft.com`, etc.) for pulling base images and dependencies required by `docker compose` or `Dockerfile`.

## Do Not

- Access other external networks (web services, APIs, third-party data sources) without explicit instruction.
- Make environment or platform configuration changes unless required to achieve clear success conditions.
- Modify CI workflows without explicit permission.

---

## Contact for Clarification

If requirements or success criteria are unclear, introduce a plans.md section titled “Clarification Needed” and explain what information is needed before proceeding.

---

## Example plans.md Skeleton

# plans.md

## Purpose
Describe what this change achieves and why.

## Success Criteria
List tests and behaviors that prove task is complete.

## Steps
1. Step by step actions.
2. …

## Progress
- [ ] Step 1
- [ ] Step 2

## Observations
Describe runtime results, failures, odd behavior.

## Decisions
Explain trade-offs and decisions.

## Retrospective
What was learned, next actions.
