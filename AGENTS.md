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
- `publisher`: .NET console app publishing sample telemetry. [oai_citation:1‡GitHub](https://github.com/takashikasuya/orleans-telemetry-sample)

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
2. **Integration Verification**
   - Confirm that Docker Compose stack starts without errors.
   - Validate REST endpoints via Swagger client.
   - Validate telemetry ingestion by injecting sample telemetry and confirming device grain state updates (as observable through API).

Agents must document verification steps in plans.md (see below).

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

## Do Not

- Access external networks (services, APIs) without explicit instruction.
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