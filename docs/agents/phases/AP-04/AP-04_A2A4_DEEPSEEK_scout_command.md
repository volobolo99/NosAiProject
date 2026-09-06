# AP-04 / A2+A4 — DeepSeek — The `--scout` operator command

## Why this shape, and not a Gate3Runtime bridge

A read-only investigation (see `docs/agents/phases/AP-04/AP-04_A1_STATUS.md`
§"Indagine su Gate3Runtime conclusa") confirmed that wiring AP-04's
`NavigationPlan` into `Gate3Runtime`'s existing `ActionCandidate`/Guard/
Trust/Safety pipeline is **not** a small, additive task today: candidate
generation there is closed/hardcoded, the movement prediction is a fixed
placeholder that never reads the target, and the live effector for
`MoveToPosition` is a single teleport-click, not a real walk. Building
those three missing pieces is out of scope for AP-04 — that work is
`docs/ROADMAP_ESECUTIVA.md`'s own **AP-08 "Strategic Autonomy + HTN"**,
a later phase. Do not touch `Gate3Runtime.cs`, `ActionPlanner`,
`SimulationEngine` or `InputActionEffector` in this task.

What **is** real, tested and reusable today is
`NosAi.Runtime.Navigation.WalkCommand.Execute` — the exact per-cell walk,
guard, verify and replan loop already Gate-1-verified for the `--walk`
operator command. `ActuationAuthority` has exactly two legitimate kinds by
design (`ActuationAuthority.cs`, ADR-0020): `Planned` (needs a
`SafetyToken` from `Gate3Runtime`'s pipeline — not available for the
reason above) or `Commanded` (a human typed a named command — legitimate,
and exactly what `--walk`/`--screen-autocalibrate` already use for
automated multi-step routines a person starts on purpose). So: a new
operator command, `--scout`, that computes a `NavigationPlan` from the
real World Model and executes it by calling `WalkCommand.Execute`
**unchanged** with `ActuationAuthority.Commanded("--scout")` — same
authority family as `--walk`, same guard chain, same verifier, zero
bypass, zero duplication of the walk logic.

**Naming note:** the command is `--scout`, not `--explore` — kept
deliberately distinct from the `Exploration` namespace/domain concept
(`ExplorationFootprint`, `ExplorationPlanner`, AP-04's own phase name)
so the operator-facing verb never gets confused with the World Model
contracts it is built on. Do not name anything you write `Explore*`.

## Already built (read, do not modify)

- `src/NosAi.Core/WorldModel/Exploration/ExplorationContracts.cs` +
  `ExplorationPlanner.cs` (AP-04/A1+A3): `ExplorationFootprint`,
  `FrontierCandidate`, `NavigationWaypoint`, `NavigationPlan`,
  `ExplorationPlanner.UpdateFootprint/BuildFrontierCandidates/
  SelectNextFrontier/BuildNavigationPlan`, `ExplorationPlanner.ToTileCoordinate`.
- `src/NosAi.Core/WorldModel/Exploration/MovementExecutionContracts.cs`
  (AP-04/A1): `MovementExecutionResult`, `MovementExecutionEvidence`
  (`.NotAttempted` factory).
- `src/NosAi.Runtime/Navigation/WalkCommand.cs`: `WalkCommand.Execute(...)`
  (signature below, you are adding one optional parameter to it — see
  MODIFY), `WalkCommand.RunWindows` (the live-composition pattern to
  mirror exactly: `RuntimeComposition.CreateSafe()`, window lookup,
  `ClientMemorySession.TryAttach`/`TryReadPlayer`/`TryReadMapId`,
  `StepGuardChain`, `SingleStepExecutor`, `GatedInputBackend`).
- `src/NosAi.Runtime/Navigation/MovementVerifier.cs`: `MovementOutcome`,
  `MovementVerification` (`Outcome`, `Detail`, `Observed: MapPoint?`,
  `Elapsed`, `ReadingsAccepted`).
- `src/NosAi.Runtime/WorldModel/Fusion/MapReconstructionSource.cs`
  (AP-03/A4, real, already persists/caches per map id):
  `MapReconstructionSource.Resolve(WorldModelSnapshot networkSnapshot, DateTime nowUtc) -> MapModel`.
  Reuse this to get a real, persisted `MapModel` — do not re-open
  `MapGridExtractor`/`MapModelStore` yourself.
- `src/NosAi.Runtime/LiveIntegration/NosTaleClientLayout.cs`:
  `PlayerObjectReading(int CharacterId, int EntityId, ushort X, ushort Y, ...)`.
- `src/NosAi.Runtime/Contracts/MapPoint.cs`: `MapPoint(int X, int Y)`.
- `tests/NosAi.Runtime.Tests/WalkCommandTests.cs`: the test rig you must
  reuse (`WalkRig`, `ChainRig`, `RecordingInputBackend`, `Fast`
  `MovementVerifier`, `StatedGeometry`) — do not invent a second one.

## The map id / coordinate conventions you must use

Same convention `MapReconstructionSource` already documents and parses:
`MapId.Value` is `"map-{numericId}"`. `MapPoint(int X, int Y)` and
`TileCoordinate(int Column, int Row)` are both already integer grid
cells in the same space — converting between them is a direct
`new TileCoordinate(point.X, point.Y)` / `new MapPoint(coordinate.Column, coordinate.Row)`,
never a float round-trip through `WorldPosition`.

## OWN (new files only)

- `src/NosAi.Runtime/WorldModel/Fusion/MovementVerificationProjector.cs` (A2)
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs` (A4)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/MovementVerificationProjectorTests.cs`
- `tests/NosAi.Runtime.Tests/ScoutCommandTests.cs`

## MODIFY, additive only

- `src/NosAi.Runtime/Navigation/WalkCommand.cs`: add one optional
  trailing parameter to `Execute`,
  `Action<MapPoint, MovementVerification>? onStepVerified = null`
  (after the existing `timestampUtc` parameter). Invoke it immediately
  after the existing line
  `MovementVerification verification = report.Verification;` /
  `controller.NoteStepOutcome(in verification);` inside the
  `WalkOutcome.Stepping` case, as
  `onStepVerified?.Invoke(to, verification);` (`to` is the cell that was
  requested — `MovementVerification` itself only carries what was
  *observed*, not what was asked for, so the caller needs `to` to build
  a complete `MovementExecutionEvidence`). Default `null` -> zero
  behavior change for `WalkCommand.RunWindows` and every existing test
  in `WalkCommandTests.cs`. Do not change anything else in this file.
- `src/NosAi.Runtime/Program.cs`: register the new flag (see §4).

## 1. `MovementVerificationProjector` (A2)

Pure, mechanical bridge — same role as AP-03/A2's
`MapGridObservationProjector`, mirroring `MovementOutcome` into
`MovementExecutionResult` without `NosAi.Core` ever referencing
`NosAi.Runtime`.

```csharp
namespace NosAi.Runtime.WorldModel.Fusion;

public static class MovementVerificationProjector
{
    public static TileCoordinate ToTileCoordinate(MapPoint point) =>
        new(point.X, point.Y);

    /// <param name="requested">The cell WalkCommand.Execute asked the character to step onto (WalkCommand's own `to`, not `verification.Observed`).</param>
    public static MovementExecutionEvidence Project(
        MapId mapId,
        MapPoint requested,
        in MovementVerification verification,
        DateTime observedAtUtc)
    {
        WorldFact<TileCoordinate> observed = verification.Observed is { } at
            ? WorldFact<TileCoordinate>.Live(ToTileCoordinate(at), confidence: 1d, observedAtUtc)
            : WorldFact<TileCoordinate>.Unknown(verification.Detail ?? "movement_not_observed", observedAtUtc);

        return new MovementExecutionEvidence(
            mapId,
            ToTileCoordinate(requested),
            observed,
            ToResult(verification.Outcome),
            verification.Detail,
            observedAtUtc);
    }

    private static MovementExecutionResult ToResult(MovementOutcome outcome) => outcome switch
    {
        MovementOutcome.Succeeded => MovementExecutionResult.Succeeded,
        MovementOutcome.Stalled => MovementExecutionResult.Stalled,
        MovementOutcome.Displaced => MovementExecutionResult.Displaced,
        MovementOutcome.Unobserved => MovementExecutionResult.Unobserved,
        MovementOutcome.Aborted => MovementExecutionResult.Aborted,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
            "Unknown MovementOutcome; MovementExecutionResult must be extended to match before this can be projected.")
    };
}
```

Test every `MovementOutcome` value maps to the matching
`MovementExecutionResult` (5 cases), that a `Succeeded`/`Stalled`
verification with `Observed` set produces a `Live` `WorldFact` at the
observed cell, that an `Unobserved`/`Aborted` verification with
`Observed == null` produces `Unknown` carrying `verification.Detail` as
its reason (fall back to `"movement_not_observed"` only when `Detail` is
itself null), and that `Requested` always reflects the `requested`
parameter, never `verification.Observed`.

## 2. `ScoutCommand` (A4 — the heavy lot)

Two layers, same split `WalkCommand` already uses so your composition
logic is testable without Windows/hardware and your live wiring stays a
thin, untested-by-design shell (same precedent as `WalkCommand.RunWindows`,
which also has no unit test — only `WalkCommand.Execute` does).

### 2a. `ScoutCommand.ExecuteOneRound` — testable, no I/O

```csharp
public static class ScoutCommand
{
    public const string Flag = "--scout";
    public const string SourceModule = "Navigation";
    public const string OperatorSessionId = "operator-scout";

    /// <summary>Nothing left to scout this cycle: FullyExplored, or no walkable tile has ever been reconstructed.</summary>
    public const int ExitNothingToScout = 6;

    /// <param name="onEvidence">Called once per emitted step, in order, via <see cref="MovementVerificationProjector"/>. Never called for a step that was never emitted (a guard refusal ends the round before any step).</param>
    /// <returns>
    /// The result of walking toward the chosen frontier, or <see langword="null"/>
    /// when <paramref name="plan"/> (out) has no reachable waypoint -- caller must
    /// check <paramref name="plan"/> in that case, not assume "null run" means failure.
    /// </returns>
    public static WalkRun? ExecuteOneRound(
        MapModel map,
        ExplorationFootprint footprint,
        WorldPosition playerPosition,
        EquatableArray<Mob> mobs,
        MapPoint origin,
        in MapGrid grid,
        OccupancyView view,
        PathWalkController controller,
        StepGuardChain chain,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        Action<MovementExecutionEvidence>? onEvidence,
        out ExplorationFootprint updatedFootprint,
        out NavigationPlan plan,
        DateTime nowUtc)
    {
        updatedFootprint = ExplorationPlanner.UpdateFootprint(
            footprint, map, WorldFact<WorldPosition>.Live(playerPosition, confidence: 1d, nowUtc), nowUtc);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, updatedFootprint, playerPosition, mobs);
        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(candidates);
        plan = ExplorationPlanner.BuildNavigationPlan(map.Id, selected, nowUtc);

        if (!plan.IsReachable.HasValue || !plan.IsReachable.Value || plan.Waypoints.Count == 0)
            return null;

        NavigationWaypoint waypoint = plan.Waypoints[0];
        var destination = new MapPoint((int)waypoint.Position.X, (int)waypoint.Position.Y);

        void OnStepVerified(MapPoint requested, MovementVerification verification)
        {
            if (onEvidence is null) return;
            MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
                map.Id, requested, in verification, nowUtc);
            onEvidence(evidence);
        }

        return WalkCommand.Execute(
            destination,
            origin,
            in grid,
            view,
            controller,
            chain,
            executor,
            in authority,
            readPosition,
            dryRun: false,
            sessionId: OperatorSessionId,
            timestampUtc: nowUtc,
            onStepVerified: OnStepVerified);
    }
```

Notes:

- `waypoint.Position` (`WorldPosition`, `float`) truncates to `int` here
  because `ExplorationPlanner.BuildNavigationPlan` always constructs it
  from a `TileCoordinate`'s own integer `Column`/`Row`
  (`new WorldPosition(candidate.Coordinate.Column, candidate.Coordinate.Row)`)
  -- there is no fractional part to lose. Do not round; truncate, to make
  that equivalence explicit rather than accidental.
- `mobs` may legitimately be `EquatableArray<Mob>.Empty` when the caller
  has no live mob feed available (see §2b) -- `FrontierCandidate.Risk`
  is then always `0` for every candidate. This is an honest, documented
  limitation, not a bug: do not fabricate mob positions to avoid it.
- This method never touches the network, memory, or an input backend
  itself -- everything I/O-shaped is a parameter, exactly like
  `WalkCommand.Execute`. This is what makes it unit-testable with the
  existing `WalkCommandTests.cs` rig.

### 2b. `ScoutCommand.Run`/`RunWindows` — live composition

Console entry point, mirroring `WalkCommand.Run`/`RunWindows` structure
exactly (same `RuntimeComposition.CreateSafe()`, window lookup,
`ClientMemorySession.TryAttach`, `StepGuardChain`, `SingleStepExecutor`,
`GatedInputBackend`, `ScreenProjectionCalibration`/`CalibratedScreenProjection`
construction -- copy that block, do not reinvent it). Differences from
`--walk`:

- No destination arguments. `Run(int rounds = 1)`: attempts up to
  `rounds` frontier walks in this one process invocation (mirrors the
  `--watch <n>` convention other probes already use, e.g.
  `--screen-watch`). Default `1`.
- Build one `MapReconstructionSource` (`using`, disposed on the way out)
  and reuse it across rounds -- it already caches per map id, so
  repeated rounds on the same map do not re-touch the grid/store (its own
  documented behavior; verify this by not disposing/recreating it between
  rounds).
- Keep one `ExplorationFootprint` in memory across rounds, keyed to the
  current map id: start it as `ExplorationFootprint.Empty(currentMapId, "scout_command_session_start", now)` before the loop; if a round observes a
  different map id than the footprint's own, reset to a fresh `Empty` for
  the new map id rather than silently reusing stale visited-tile data
  from a different map (this session's memory does not need to survive a
  process restart -- cross-session footprint persistence is explicitly
  out of scope for this task, same honesty pattern as
  `MapReconstructionSource`'s own documented limitations).
- Each round: read `player`/`mapId` the same way `WalkCommand.RunWindows`
  does; build the minimal `WorldModelSnapshot` needed to call
  `MapReconstructionSource.Resolve` --
  `WorldModelSnapshot.Unknown("scout_command_local_read", now) with { Map = MapModel.Unknown(new MapId($"map-{mapId}"), "scout_command_local_read", now) }`
  -- then `MapModel map = mapReconstruction.Resolve(snapshotForResolve, now);`.
  Pass `mobs: EquatableArray<Mob>.Empty` (no live mob feed is wired into
  this command's context; document this exact limitation in your
  completion report, do not go build one -- out of scope here).
- Call `ExecuteOneRound` with `authority: ActuationAuthority.Commanded(Flag)`,
  the same `readPosition` closure pattern `WalkCommand.RunWindows` already
  uses, and `onEvidence` printing each evidence's `Result`/`Detail` to
  the console (one line per step, same spirit as `SingleStepCommand.Format`)
  -- do not persist evidence to a store in this task; printing is enough
  for this round.
- If `ExecuteOneRound` returns `null` (nothing reachable this round):
  print the plan's `IsReachable` reason and stop the loop early (return
  `ExitNothingToScout`) rather than looping `rounds` times uselessly.
- Otherwise print the `WalkRun.Text` (same as `--walk` already does) and,
  if its `ExitCode != WalkCommand.ExitArrived`, stop the loop early and
  return that exit code unchanged -- do not keep scouting after an
  abandoned/refused walk. Only on `ExitArrived` continue to the next
  round.
- After all rounds finish (or the loop ends early with `ExitArrived` on
  the last round), return `WalkCommand.ExitArrived`.

## 3. Wiring `Program.cs`

Additive, alongside the existing `--walk`/`--step` block:

```csharp
if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.ScoutCommand.Flag, StringComparison.OrdinalIgnoreCase)))
{
    int watchFlag = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
    int rounds = watchFlag >= 0 && watchFlag + 1 < args.Length
                 && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRounds)
                 && parsedRounds > 0
        ? parsedRounds
        : 1;

    return NosAi.Runtime.Navigation.ScoutCommand.Run(rounds);
}
```

Place it next to the existing `walkFlag`/`stepFlag` block (around
`Program.cs`'s existing navigation-command section), not inside any
other conditional.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~MovementVerificationProjectorTests|FullyQualifiedName~ScoutCommandTests|FullyQualifiedName~WalkCommandTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

`WalkCommandTests` must stay 100% green and unchanged in count/behavior
-- your one added parameter on `Execute` is optional and defaults to
`null`, so nothing in that file should need editing. `ScoutCommandTests`
must cover `ExecuteOneRound` using the `WalkCommandTests` rig style
(`WalkRig`/`ChainRig`/`RecordingInputBackend`): a reachable frontier walks
and reports evidence matching each emitted step in order; no walkable
unvisited tile (fully explored map) returns `null` and a `plan` with
`IsReachable` false/Unknown and does not call `WalkCommand.Execute` at
all (assert via the recorder seeing zero emitted input); a map with no
tiles at all behaves the same way, honestly (nothing fabricated). All
must be green, 0 warnings/0 errors on the build, no regression in the two
full test suites (record exact pass counts, same as every prior phase's
status doc).

## Report back

Files created/modified; the exact diff to `WalkCommand.Execute`'s
signature (confirm `WalkCommandTests.cs` needed zero changes); build/test
evidence with exact pass counts; the mob-feed limitation stated plainly
(`Risk` always `0` until a live mob source is wired in -- name this as a
known gap, not a silent omission); anything found in AP-04/A1/A3 or in
`WalkCommand.cs` that looks wrong (do not fix it yourself -- report it
for AP-04/A5's audit and AP-04/A6's integration); verification level
(`Present`/`Integrated`) and why.
