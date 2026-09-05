# AP-01 / A1 — Claude — World Model Contracts

## MODE
Implementation agent. Work only on the files named below.

## GOAL
Define the versioned, immutable Unified World Model contracts consumed by every later AP-01+ agent (A2 sensor fusion, A3 temporal belief/prediction, A4 runtime wiring): Player, Map, Tile/Polygon, Portal, Mob, NPC, Drop, Quest, InventoryItem, EquipmentItem, Skill, Buff, Debuff, Cooldown, Resource, Action, Goal (docs/ROADMAP_ESECUTIVA.md S:AP-01).

## READ
`docs/ROADMAP_ESECUTIVA.md` (S:AP-01); `docs/NOSAI_ARCHITECTURE_BASELINE.md` (S:3 "World Model"/"Domain Models"); `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` (S:4.2); `src/NosAi.Core/Navigation/NavigationObservation.cs` and `NavigationEvidenceEvaluator.cs` (existing provenance/freshness style); `src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs` (existing classified-value/Unknown-factory style, and its "no collections in a record" pitfall — WorldModel needs collections, so this phase adds `EquatableArray<T>` instead of repeating that constraint); `src/NosAi.Core/WorldState.cs` (the pre-existing, unrelated low-level reflex-loop snapshot — do not reuse or rename its types, AP-01's World Model is a separate, richer semantic layer that coexists with it per ADR-0015/ADR-0025).

## OWNED FILES
Only `src/NosAi.Core/WorldModel/*.cs` (new folder) and their direct unit tests under `tests/NosAi.Core.Tests/WorldModel/*.cs`. Do not edit `src/NosAi.Core/Hardware/`, `src/NosAi.Core/Scheduling/`, `src/NosAi.Core/WorldState.cs`, any `Gate1/2/3` file, any `.csproj`, or any other agent's files.

## REQUIREMENTS
- Every important fact carries provenance (`DataSourceKind`: Live/Derived/Cached/Simulated/Unknown), a continuous confidence score and an observation timestamp — never collapsed into a silent default. Follow the existing repo convention of a self-contained classification primitive per bounded context (see `NosAi.Core.Hardware.HardwareClassification.cs`'s documented rationale) rather than reaching into `NosAi.Core.Hardware` from gameplay contracts; note the eventual cross-context unification as A6/integration follow-up, do not attempt it here.
- Records containing collections must still support true structural/value equality (needed for the "replay deterministico" DoD) — do not put a bare `List<T>`/array/`ImmutableArray<T>` straight into a record; add and use one small equatable collection wrapper.
- No hardcoded gameplay content (no fixed map list, no fixed item/skill catalog) — every identity is a typed id, every attribute is either a real classified fact or an explicit `Unknown` factory result.
- Contracts only: no fusion logic, no planner, no I/O. That is A2/A3/A4's job in later tasks.

## DELIVERY
Complete files, zero TODO/stub. Build `src/NosAi.Core/NosAi.Core.csproj` (0 warnings, `TreatWarningsAsErrors=true`) and run `tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj` (all green, including pre-existing tests). Update `docs/agents/EXECUTION_QUEUE.md` (Q-007 → DONE) and `docs/agents/phases/AP-00/AP-00_STATUS.md`-style evidence in a new `docs/agents/phases/AP-01/AP-01_STATUS.md`. Handoff to A2 (DeepSeek, sensor fusion) and A3/A4: exact type/namespace list, the equatable-collection helper's contract, and the unresolved `DataSourceKind` duplication note.
