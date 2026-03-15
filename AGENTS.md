# AGENTS

This document defines guardrails and procedures for AI agents operating on this repository.

---

## Repository Overview

**orleans-telemetry-sample** is a sample solution demonstrating a telemetry ingestion pipeline.
It ingests messages from RabbitMQ, MQTT and Kafka into an Orleans cluster, routes them to device-scoped grains, and exposes state via REST and gRPC.
Services include:
- `mq`: RabbitMQ broker for incoming telemetry.
- `silo`: Orleans host with RabbitMQ consumer and grain logic.
- `api`: ASP.NET Core application providing REST and gRPC APIs.
- `publisher`: .NET console app publishing sample telemetry.

The sample is intended for local development and experimentation, not production.

---

## Scope of AI Agent Work

1. Operate solely within this repository; do not access external network resources unless explicitly instructed or required by allowed package/image restore flows.
2. Favor incremental, focused changes that retain existing behavior and do not introduce broad refactors.
3. Base all actions on observable, verifiable behaviors: successful build, passing tests, and local run results.

---

## Work Intake Policy

1. Treat a GitHub Issue as the default unit of work for any non-trivial task.
2. If the user references an Issue number, title, or URL, use that Issue as the primary source of scope and record it in planning artifacts.
3. If work starts without an Issue, create a temporary ad-hoc planning note and clearly mark that the work was not Issue-backed.
4. Prefer one task note per Issue. Do not mix unrelated work into the same task note.
5. If ad-hoc work grows beyond a quick one-off change, spans multiple turns, or is likely to be committed/shared, convert it into an Issue-backed task as early as practical.

Success conditions for a task must still be written locally even when the Issue already contains acceptance criteria.

Use the following intake rule of thumb:

- Issue-first: bug fixes, features, refactors, behavior changes, test additions, config changes, process changes meant to persist.
- Ad-hoc allowed: narrow exploratory reads, one-off documentation touch-ups, or user-directed local experiments that are unlikely to survive as tracked work.

---

## Goals & Success Criteria

Before executing any change or implementation task, an agent must establish:

1. Purpose of change or task: what user value or feature is delivered.
2. Success conditions: concrete, testable outcomes such as passing tests, successful startup, or verified endpoint behavior.
3. Verification steps: commands and checks that demonstrate correctness.

Unless success conditions and verification steps are clearly articulated, do not implement code changes.

---

## Standard Commands (Observable Behavior)

The following commands should be used to build and validate the repository:

- Build
  ```bash
  dotnet build
  ```

- Test
  ```bash
  dotnet test
  ```

- Local Run via Docker Compose
  ```bash
  docker compose up --build
  ```

After startup, verify:
- REST API swagger at: http://localhost:8080/swagger
- GRPC service responds to client calls

Adjust ports/config as described in `README.md`.

---

## Required Reading Documents

Before starting any work, agents must consult the following documents in this order:

1. The relevant GitHub Issue, if one exists for the task.
2. `plans.md`
   - This is the active planning index and operating guide.
   - It should stay concise and point to the detailed task note.
3. The detailed task note under `plans/YYYY-MM/`
   - Use `issue-<number>-<slug>.md` for Issue-backed work.
   - Use `adhoc-YYYY-MM-DD-<slug>.md` only when no Issue exists.
   - Follow `docs/github-issue-workflow.md` for naming and workflow conventions.
4. `PROJECT_OVERVIEW.md`
   - High-level architecture and workflow overview.
5. `README.md`
   - Service startup sequence and basic usage.
6. Relevant files in `docs/`
   - Read only the subsystem documents needed for the task.
   - Include `docs/github-issue-workflow.md` when the task changes process or Issue operations.

If the required planning file does not exist for the current task, create it before proceeding.

---

## Planning Files and Rotation

Use the following structure:

- `plans.md`
  - Active workboard only.
  - Contains current task links, archive links, and planning rules.
  - Do not append long retrospectives or full task logs here.
- `plans/YYYY-MM/issue-<number>-<slug>.md`
  - Detailed execution log for one GitHub Issue.
- `plans/YYYY-MM/adhoc-YYYY-MM-DD-<slug>.md`
  - Temporary detailed log when no Issue exists.
- `plans/archive/YYYY-MM.md`
  - Monthly archive of superseded root-level planning history or rolled-up summaries when needed.

Rotation rules:

1. Keep `plans.md` short; it should function as an index, not a running journal.
2. Store detailed notes in monthly files under `plans/YYYY-MM/`.
3. When a month changes, start writing new task notes in the new month directory.
4. Rotate root-level history into `plans/archive/YYYY-MM.md` as soon as `plans.md` starts accumulating narrative history, long retrospectives, or completed-task detail beyond summary links.
5. Do not rewrite or delete old task history unless the user explicitly requests cleanup.

In practice, `plans.md` should usually contain only:
- active task links
- short status lines
- archive links
- lightweight operating rules

Anything longer belongs in the monthly task note or archive file.

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
- Orleans grain and stream definitions in silo project.
- API controllers in gateway projects exposing REST and gRPC.

Refer to these for structural context before modifying code.

---

## Testing and Verification

1. Automated Tests
   - Run relevant `dotnet test` commands for the changed scope.
   - Run the full `dotnet test` for behavior-affecting changes unless the user has constrained the task to documentation/process updates.
   - Document any new tests required for new functionality.
2. Integration Verification (Local Only)
   - Confirm that the Docker Compose stack starts without errors when configuration or runtime behavior changed.
   - Validate REST endpoints via Swagger or curl when APIs changed.
   - Validate telemetry ingestion by injecting sample telemetry and confirming device grain state updates when ingest behavior changed.

Agents must clearly document which verification steps were executed by the agent and which require local execution.

---

## Consistency Checks for Changes

Before finalizing any code change, agents must verify:

1. Build Integrity
   - Run `dotnet build` to ensure no compilation errors.
   - All affected projects must build successfully.
2. Test Integrity
   - Run `dotnet test` for affected scope.
   - Add new tests for new functionality where appropriate.
3. Documentation Alignment
   - Update the detailed task note with the actual changes made.
   - Update `plans.md` so it still points to the current source of truth.
   - Verify that `README.md`, `PROJECT_OVERVIEW.md`, and `docs/` remain accurate.
4. API Contract Stability
   - Public APIs should not change unless explicitly required.
   - Breaking changes must be documented in the task note with justification.
5. Configuration Consistency
   - Verify that `docker-compose.yml`, `Dockerfile`, and `appsettings.json` remain consistent.
   - Test with `docker compose up --build` if configuration changes are made.
6. Dependency Integrity
   - Ensure NuGet package versions are compatible.
   - Avoid introducing unnecessary dependencies.

---

## Definition of Done

A task is considered complete when all of the following criteria are met:

1. Code Implementation
   - All code changes are implemented as specified in the detailed task note.
   - Code follows existing conventions (C# 12, .NET 8, async/await patterns).
   - No placeholder or TODO comments remain in production code.
2. Build & Test Success
   - Relevant build/test commands complete with zero errors.
   - No new warnings are introduced unless documented in the task note.
3. Integration Verification
   - `docker compose up --build` starts successfully when applicable.
   - REST API endpoints respond correctly when applicable.
   - gRPC services respond correctly when applicable.
   - Telemetry ingestion works end-to-end when applicable.
4. Documentation Complete
   - The detailed task note is updated with outcomes and retrospective.
   - `plans.md` is updated with the active entry and archive references.
   - `README.md` or relevant docs are updated if behavior changed.
5. Cleanup & Review
   - No debug code, console logs, or temporary files are left in the repository.
   - Changes are minimal and focused on the stated purpose.
6. Agent Confirmation
   - The agent explicitly confirms which Definition of Done items were satisfied.
   - The agent identifies any manual verification steps still required.

If any criterion cannot be met, document the blocker under `Clarification Needed` or `Blockers` in the detailed task note.

---

## Coding Conventions

- Language: C# 12 / .NET 8.
- Public API changes should be minimized.
- Use concise, behavior-focused comments when logic is non-obvious.
- Avoid broad refactors unless explicitly requested.

---

## Recording and Documentation

Every non-trivial task must be accompanied by:

1. A detailed task note in `plans/YYYY-MM/`
   - Include purpose, success criteria, implementation steps, progress, observations, decisions, verification, and retrospective.
   - Include the related GitHub Issue number and title when available.
2. A concise entry in `plans.md`
   - Point to the active task note.
   - Keep only summary status at the root.
3. Commit messages that reference the Issue number or task note identifier when available.
4. Incremental updates to the detailed task note for multi-step work.

Agents must treat the detailed task note as the primary execution record and `plans.md` as the active index.

When work begins ad-hoc and later becomes durable, the agent should:
1. Open or reference the GitHub Issue.
2. Create or rename the task note to the `issue-<number>-<slug>.md` convention when practical.
3. Update `plans.md` so the active index points at the Issue-backed note.

---

## Allowed External Access

Agents may access the following external resources for standard development tasks:
- NuGet package registry (`nuget.org`) for restoring and updating .NET dependencies via `dotnet restore`, `dotnet build`, or `dotnet add package`.
- Docker image registries (Docker Hub, `mcr.microsoft.com`, and similar) for pulling base images and dependencies required by `docker compose` or `Dockerfile`.
- GitHub Issue metadata only when the user explicitly requests Issue-driven synchronization or references a specific Issue.

## Do Not

- Access other external networks, web services, or third-party APIs without explicit instruction.
- Make environment or platform configuration changes unless required to achieve clear success conditions.
- Modify CI workflows without explicit permission.

---

## Contact for Clarification

If requirements or success criteria are unclear, add a `Clarification Needed` section to the detailed task note and explain what information is needed before proceeding.

---

## Task Note Skeleton

```md
# Task: <Issue number or ad-hoc title>

## Metadata
- Issue: #123 Example title
- Status: In Progress

## Purpose
Describe what this change achieves and why.

## Success Criteria
List tests and behaviors that prove task is complete.

## Steps
1. Step by step actions.
2. ...

## Progress
- [ ] Step 1
- [ ] Step 2

## Observations
Describe runtime results, failures, and surprises encountered.

## Decisions
Explain trade-offs and decisions.

## Verification Plan
- `dotnet build`
- `dotnet test`

## Verification Results
Record what was actually executed and what happened.

## Retrospective
What was learned, follow-ups, and remaining manual checks.
```
