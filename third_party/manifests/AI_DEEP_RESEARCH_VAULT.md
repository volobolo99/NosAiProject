# AI Deep Research Vault — 2026-09-04

This index tells Cursor and Claude Code which locally stored research/reference material to inspect before implementing advanced AI capabilities.

## Memory

- `sources/microsoft/Memora/reference/README.md` — memory lifecycle, abstractions, cue anchors, hybrid retrieval, consolidation.
- `sources/joslat/agent-memory-dotnet/reference/README.md` — .NET graph memory, temporal validity, reasoning traces, auditability.

## Planning

- `sources/luxkun/ReGoap/reference/IReGoapAction.cs` — GOAP action contract.
- `sources/luxkun/ReGoap/reference/IReGoapAgent.cs` — GOAP agent contract.
- `sources/luxkun/ReGoap/reference/IReGoapGoal.cs` — goal contract and priority/retry concepts.
- `sources/luxkun/ReGoap/reference/IReGoapMemory.cs` — planner memory abstraction.
- `sources/luxkun/ReGoap/reference/IReGoapSensor.cs` — sensor-to-memory pattern.
- `sources/luxkun/ReGoap/reference/ReGoapCondition.cs` — comparator-based state conditions; adapted reference, not production code.

## Navigation

- `sources/ikpil/DotRecast/reference/README.md` — Recast/Detour navmesh, pathfinding, tiled/dynamic navigation and crowd concepts.

## Existing game/network references

- `sources/opennos/reference/LoginPacketHandler.cs`
- `sources/noscore/reference/WalkPacketHandler.cs`
- `sources/chickenapi/reference/BasicEventPipelineAsync.cs`
- `sources/saltyemu/reference/WorldServer.cs`

These are reference-only and may contain server-side emulator concepts. They must never introduce privileged server authority into the NosAi client path.

## Research documents

- `docs/research/AI_CAPABILITY_RESEARCH_2026-09-04.md`
- `docs/research/AI_CAPABILITY_DEEP_RESEARCH_2026-09-04.md`

## Mandatory safety rule

Before using any item in the vault for gameplay functionality, read:
- `docs/adr/ADR-0021-unprivileged-observability-boundary.md`
- `docs/UNPRIVILEGED_DEMO_SPEC.md`
- `docs/adr/ADR-0020*` and the current executable roadmap.

Third-party material is never execution authority. Preserve provenance, licensing and attribution. Never delete vault material automatically.
