# AP-08 / A2+A4 — DeepSeek — The `--autoplay` operator command

## Read this section before anything else

This is a different class of task from every operator command built so
far. `--scout`/`--engage`/`--collect`/`--recover` each execute **one act
the operator names**. `--autoplay` is the first component in this project
that **chooses** which act to attempt, cycle by cycle, from
`StrategyPlanner.SelectStrategicPlan`'s output. The user explicitly
approved building this ("anche un orchestratore minimo... va delimitato
con cura") — the boundaries below are not optional simplifications, they
are the terms of that approval. Do not widen this command's scope beyond
what this document specifies, even if it would be easy to add.

**What `--autoplay` is allowed to decide:** which of two already-built,
already-audited commands to invoke this cycle — `ScoutCommand.ExecuteOneRound`
or `RecoverCommand.ExecuteOneRound` — based on which `StrategicGoalKind`
`StrategyPlanner.SelectStrategicPlan` selects. **What it is never allowed
to do:** construct an `ActuationScope`, press a key, or move the mouse
itself. Every actual input event still goes through the exact same
`StepGuardChain`/`GatedInputBackend`/keybind-confirmation path those two
commands already use — `--autoplay` only decides *which* pre-built,
pre-gated round to run. If both commands' own Guard/Safety refuse
everything on a given cycle (as `--engage`/`--recover` currently do on the
armed production gate — see `AP-05_A5_AUDIT.md` §5), `--autoplay`
faithfully reports that refusal; it does not work around it.

## Scope, stated as hard limits

1. **Only two `StrategicGoalKind` values are dispatched:
   `Survival` → `RecoverCommand.ExecuteOneRound`, `Exploration` →
   `ScoutCommand.ExecuteOneRound`.** `QuestUrgency` is deliberately **not**
   dispatched: `WorldModelSnapshot.Quests` is empty in every live
   composition today (`GameplayObservationProjector.Project` always
   returns `EquatableArray<Quest>.Empty` — no network/OCR channel
   populates real `Quest`/`QuestObjective` instances yet, the same AP-06
   gap named in `AP-06_A1_STATUS.md`). `StrategyPlanner.AssessQuestUrgency`
   will therefore return `null` on every real cycle, and
   `SelectStrategicPlan` will never select it while that gap stands — do
   not write dispatch code for a signal that cannot fire honestly today.
   `Recovery`/`Progression`/`Farming`/`Optimization` have no assessor at
   all (`AP-08_A1_STATUS.md`) and are out of scope for the same reason.
2. **A hard cycle cap.** `--autoplay [--cycles <n>] [--recover-slot <slot>]`,
   default `n = 1`. `MaxCycles = 20` (a named constant). A requested
   `--cycles` above `MaxCycles` is refused cleanly
   (`[REFUSED] autoplay_cycles_exceeds_max:20`), never silently clamped —
   the operator must ask again with a smaller number, not be surprised by
   a quietly shortened run.
3. **Stops immediately when nothing is urgent.** `StrategicPlan.SelectedKind == null`
   (`Unselected`) ends the loop early and returns a distinct exit code —
   do not keep looping through the remaining cycles once there is nothing
   to do.
4. **Survival without a configured recovery slot is a skip, not a crash
   or a silent Exploration fallback.** If `SelectedKind == Survival` and
   `--recover-slot` was not given, print
   `[WARN] survival urgent but no --recover-slot configured, skipping this cycle`
   and continue to the next cycle (do not substitute a different action
   for the one the plan actually selected).
5. **Authority.** Every dispatched round uses
   `ActuationAuthority.Commanded(Flag)` (`Flag = "--autoplay"`), passed as
   the `authority` parameter straight into
   `ScoutCommand.ExecuteOneRound`/`RecoverCommand.ExecuteOneRound` —
   **not** `ActuationAuthority.Commanded("--scout")`/`"--recover"`. The
   audit trail must show these acts were chosen by `--autoplay`, not typed
   directly by an operator.
6. **No candidate generation, no new planning logic.** `--autoplay` calls
   `StrategyPlanner.AssessSurvivalUrgency`/`AssessExplorationUrgency`/
   `SelectStrategicPlan` exactly as they exist today (read
   `src/NosAi.Core/WorldModel/Strategy/StrategyPlanner.cs` in full — do
   not modify it, do not add a new assessor, do not touch
   `AssessQuestUrgency`).

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/Strategy/StrategyContracts.cs`/`StrategyPlanner.cs`
  (AP-08/A1+A3, already `Present`): `StrategicGoalKind`, `StrategicSignal`,
  `StrategicPlan`, `AssessSurvivalUrgency(Player)`,
  `AssessExplorationUrgency(ExplorationFootprint)`,
  `SelectStrategicPlan(IReadOnlyList<StrategicSignal>, DateTime)`.
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs` (delivered, audited,
  integrated — `AP-04_STATUS.md`): read `ExecuteOneRound` and
  `RunWindows` in full. `RunWindows` (lines ~209-389) is the exact
  composition to mirror for the movement half of this command's live
  wiring: `RuntimeComposition.CreateSafe()`, window lookup,
  `ClientMemorySession.TryAttach`, `MapReconstructionSource`,
  `MapGridExtractor`, `StepGuardChain`/`SingleStepExecutor`/
  `PathWalkController` construction, the `OccupancyView(null, now)`
  convention (shared, documented limitation — see
  `AP-06_A5_AUDIT.md`'s note on this, do not try to fix it here).
- `src/NosAi.Runtime/Tactical/RecoverCommand.cs` (this session, sibling
  task Q-050/Q-051 — if not yet delivered when you start this task, stop
  and report the blocker per `CLAUDE.md`'s dependency rule; do not
  duplicate its logic here). Read `ExecuteOneRound`/`RunWindows` in full
  for the consumable-recovery half of this command's live wiring
  (`KeybindMap`, `GatedInputBackend`).
- `src/NosAi.Core/WorldModel/Combat/CombatContracts.cs`:
  `CombatActionCandidate(CombatActionKind.UseConsumable, item: ItemId)`.
- `docs/agents/phases/AP-08/AP-08_A1_STATUS.md` §"AP-08/A2+A4" — the
  investigation this task executes.

## OWN (new files only)

- `src/NosAi.Runtime/Tactical/AutoplayCommand.cs` (A4)
- `tests/NosAi.Runtime.Tests/AutoplayCommandTests.cs`

## MODIFY

- `src/NosAi.Runtime/Program.cs`: register the new flag (see §3).

Do not touch `ScoutCommand.cs`, `RecoverCommand.cs`, `EngageCommand.cs`,
`CollectCommand.cs`, `StrategyPlanner.cs`, `WalkCommand.cs`.

## 1. `AutoplayCommand.ExecuteOneCycle` — testable, no I/O

A pure dispatcher: given an already-computed `StrategicPlan` and the
already-assembled context each candidate action needs, calls at most one
of the two existing `ExecuteOneRound` methods and returns what happened.
No I/O, no clock reads, no `Console.Write` — exactly the same testability
discipline `ScoutCommand.ExecuteOneRound`/`RecoverCommand.ExecuteOneRound`
already follow.

```csharp
public enum AutoplayDispatch
{
    Idle = 0,           // StrategicPlan.SelectedKind was null
    Explored = 1,       // dispatched to ScoutCommand
    Recovered = 2,      // dispatched to RecoverCommand
    SurvivalSkippedNoSlot = 3, // Survival selected, no --recover-slot configured
    NotDispatchable = 4       // any other selected Kind (QuestUrgency etc.) -- named, not silently ignored
}

public sealed record AutoplayCycleResult(
    AutoplayDispatch Dispatch,
    StrategicPlan Plan,
    WalkRun? ScoutRun,
    CombatExecutionEvidence? RecoverEvidence);
```

`ExecuteOneCycle` takes: the `StrategicPlan` (already computed by the
caller from live `Player`/`ExplorationFootprint`), an `int? recoverSlot`,
and everything `ScoutCommand.ExecuteOneRound`/`RecoverCommand.ExecuteOneRound`
each need as parameters (map/footprint/grid/view/controller/chain/executor
for Scout; keybinds/input/readVitals/verificationDelay for Recover), plus
the shared `authority`/`nowUtc`. Switches on `plan.SelectedKind`:
`null` → `Idle`; `Exploration` → call `ScoutCommand.ExecuteOneRound` with
the given context, wrap its result; `Survival` → if `recoverSlot` is
`null`, return `SurvivalSkippedNoSlot`; else build the `UseConsumable`
candidate and call `RecoverCommand.ExecuteOneRound`, wrap its result;
anything else → `NotDispatchable`.

Test every branch of this switch in isolation with the injected rig style
already established (`WalkCommandTests`' `WalkRig`/`ChainRig`, a
recording `IInputBackend` for the Recover branch) — this is the bulk of
this task's real test coverage, since `RunWindows` (like every other
command's) is a thin, untested-by-design live shell.

## 2. `AutoplayCommand.Run`/`RunWindows` — live composition

```csharp
public const string Flag = "--autoplay";
public const int MaxCycles = 20;
public const string CyclesExceedsMaxReason = "autoplay_cycles_exceeds_max";
public const string SurvivalNoSlotWarning = "survival urgent but no --recover-slot configured, skipping this cycle";
```

`Run(int cycles = 1, int? recoverSlot = null)`: refuse
(`[REFUSED] {CyclesExceedsMaxReason}:{MaxCycles}`) if `cycles > MaxCycles`
or `cycles < 1`, same `[REFUSED]`-not-throw discipline as every other
command's argument validation (`AP-05_A5_AUDIT.md`/`AP-06_A5_AUDIT.md`).

`RunWindows`: build **one** shared live composition for the whole
invocation — attach the client session once, build the gated backend and
`StepGuardChain`/`MapReconstructionSource` once (mirroring
`ScoutCommand.RunWindows`), load the keybind map once (mirroring
`RecoverCommand.RunWindows`) — then loop cycles:

1. Read live player position/map/vitals the same way
   `ScoutCommand.RunWindows`/`RecoverCommand.RunWindows` each already do.
2. Build a minimal `Player` (AP-01) carrying only what
   `AssessSurvivalUrgency` reads: `Status.Resources` with one
   `Resource(ResourceKind.Health, current, max)` from the live vitals
   reading; every other `Player` field `Unknown`/empty — same "minimal
   object with only the one real fact" pattern `ScoutCommand.RunWindows`
   already uses for `MapModel.Unknown`/`WorldModelSnapshot.Unknown`.
3. Compute both signals (`AssessSurvivalUrgency`, `AssessExplorationUrgency`
   against the session's `ExplorationFootprint`), collect the non-null
   ones, call `SelectStrategicPlan`.
4. Call `ExecuteOneCycle`, print a one-line verdict naming the
   `Dispatch` outcome and, when dispatched, the underlying command's own
   result text/evidence (reuse `ScoutCommand`'s/`RecoverCommand`'s own
   print formatting where practical, do not invent a third format).
5. If `Dispatch == Idle`, print `nothing urgent, stopping` and return a
   distinct exit code (do not run the remaining cycles).
6. Otherwise continue to the next cycle up to the requested count.

## 3. Wiring `Program.cs`

Additive, alongside the existing `--recover` block:

```csharp
if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.AutoplayCommand.Flag, StringComparison.OrdinalIgnoreCase)))
{
    int cyclesFlag = Array.FindIndex(args, a => string.Equals(a, "--cycles", StringComparison.OrdinalIgnoreCase));
    int cycles = cyclesFlag >= 0 && cyclesFlag + 1 < args.Length
                 && int.TryParse(args[cyclesFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCycles)
        ? parsedCycles
        : 1;

    int slotFlag = Array.FindIndex(args, a => string.Equals(a, "--recover-slot", StringComparison.OrdinalIgnoreCase));
    int? recoverSlot = slotFlag >= 0 && slotFlag + 1 < args.Length
                        && int.TryParse(args[slotFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedSlot)
        ? parsedSlot
        : null;

    return NosAi.Runtime.Tactical.AutoplayCommand.Run(cycles, recoverSlot);
}
```

Note this does **not** reuse the `--watch` flag other commands use —
`--cycles` is its own name, because `--autoplay` choosing what to do each
cycle is a different concept from `--watch` repeating the *same*
operator-named act.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~AutoplayCommandTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

`AutoplayCommandTests` must cover every `AutoplayDispatch` branch of
`ExecuteOneCycle` (Idle/Explored/Recovered/SurvivalSkippedNoSlot/
NotDispatchable), `Run` refusing `cycles > MaxCycles` and `cycles < 1`
without throwing, and that a `NotDispatchable` or `Idle` cycle never
touches the input backend or the walk controller (assert via a recording
fake, same style as `RecordingInputBackend`). All green, 0 warnings/0
errors, no regression in either full test suite.

## Report back

Files created/modified; build/test evidence with exact pass counts;
confirm explicitly that `QuestUrgency`/`Recovery`/`Progression`/
`Farming`/`Optimization` are not dispatched anywhere in this delivery;
verification level (`Present`/`Integrated`) and why; anything found in
`StrategyPlanner.cs`/`ScoutCommand.cs`/`RecoverCommand.cs` that looks
wrong (do not fix it yourself — report it).
