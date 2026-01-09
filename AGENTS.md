# AGENTS

This repository is a .NET 8 solution for Orleans telemetry ingestion and analysis.

## Scope
- Work in this repo only; do not access network resources unless explicitly asked.
- Favor small, focused changes that keep existing behavior intact.

## Coding conventions
- C# 12 / .NET 8.
- Keep public API changes minimal.
- Avoid broad refactors unless requested.
- Add concise comments only when behavior is non-obvious.

## Testing
- Default test command: `dotnet test`.
- If only one project is relevant, prefer testing that project first.

## Build
- Default build command: `dotnet build`.

## Notes
- Warnings from XML doc comments may appear during builds; do not change warning settings unless asked.
