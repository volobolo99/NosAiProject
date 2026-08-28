# NosAi — C# Runtime Migration

**Version:** 1.0 Beta  
**Creator:** Volodymyr Ryzhuk

## Decision

The primary NosAi runtime is being established in **C# / .NET 8 on Windows**. Existing Python modules are retained as research/prototyping and compatibility assets while equivalent production runtime components are migrated behind stable contracts.

## First migrated boundaries

- Runtime entry point.
- Candidate Action / Trust Tier contracts.
- Guard AI deterministic evaluation.
- Fail-closed Safety Gate boundary.
- Utility AI candidate selection foundation.
- Telemetry and Mastery Score foundation.

## Migration rule

Do not perform a blind language translation. Preserve the architecture and contracts, then implement each runtime boundary natively in C# with tests. Python remains available where it materially benefits experimentation, ML research or tooling.

## Current C# runtime structure

```text
src/NosAi.Runtime/
├── Contracts/
├── Guard/
├── PlayAi/
├── Safety/
├── Telemetry/
└── Program.cs
```

## Safety status

The C# Safety Gate is deliberately **fail-closed**. Until a validated game adapter and complete Guard bring-up exist, no live execution is authorized by this runtime foundation.

## Next migration targets

1. Minimal PC Play AI + Play Guard + Guard AI bring-up.
2. World Model contracts.
3. Perception contracts and production Windows capture boundary.
4. Orchestrator integration.
5. Simulation/Tactical Ranking.
6. Persistent telemetry/memory.
7. Controlled game adapter.

The project version remains **1.0 Beta** until explicitly changed by the creator.
