# AP-06 / A4 — DeepSeek — The `--collect` operator command

## The decision this task executes

`docs/agents/phases/AP-06/AP-06_A1_STATUS.md` §"AP-06/A2+A4 — indagine
mirata, decisione, algoritmo e specifica DeepSeek" found, by direct
inspection rather than assumption, that `docs/ROADMAP_ESECUTIVA.md`
S:AP-06's "OCR/UI/**network** evidence" has a real, already-decoded
network path for `QuestObjectiveKind.Collect` specifically — independent
of the OCR/ML gap that blocks every other objective kind. `Travel` needs
no new code at all (`WalkCommand.Execute`/`--walk` already does it).
`Kill`/`Dialogue`/`Interact`/`Deliver` stay blocked (no entity
classification for Kill, no interaction primitive or verification channel
for the rest) — do not attempt them here.

**What this buys, and what it deliberately does not:** it confirms *"the
player's own count of this item increased after walking to this
position"* — a real, wire-confirmed inventory count, not OCR, not a
guess. It does **not** discover ground items automatically, does **not**
pick the best one to fetch, and does **not** handle any quest semantics
beyond the one objective the operator names directly.

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/Quests/QuestGraphPlanner.cs`,
  `AssessCollectProgress(QuestObjectiveTarget target, EquatableArray<InventoryItem> inventory, DateTime observedAtUtc) -> WorldFact<int>`
  (already written and tested this task, in `NosAi.Core` — read it in
  full, including its own remarks about the empty-inventory ambiguity).
  This is the one piece of comparison logic you call; do not reimplement
  its reasoning.
- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`:
  `InventorySlotReading`/`ItemPickup`/`GroundItem` — real, decoded from
  the wire's own `ivn`/`get`/`drop` opcodes
  (`NosTaleWorldProtocolDecoder.cs`), not synthetic.
- `src/NosAi.LiveIntegration/GameplayProvider.cs`: `GameplayObservation`
  carries `Inventory`/`GroundItems`/`LastPickup` as
  `ClassifiedValue<IReadOnlyList<...>>` — per-field provenance, `Unknown`
  with a reason when the bound provider never set them.
- `src/NosAi.Runtime/LiveIntegration/LiveObservationGateway.cs`:
  `LiveObservationGateway.Capture() -> LiveObservationSnapshot` — a single,
  non-polling read of `ClientBaselineSnapshot` + `GameplayObservation`.
  Same role as `ClientMemorySession.TryReadPlayerVitals` had for AP-05's
  `--engage`: call it once before the walk, once after.
- `src/NosAi.Runtime/WorldModel/Fusion/GameplayObservationProjector.cs`:
  `GameplayObservationProjector.Project(GameplayObservation, EntityId playerId, long version, DateTime nowUtc) -> WorldModelSnapshot`
  (AP-01/A2, **already `Integrated`**) — call this on each
  `LiveObservationSnapshot.Gameplay` you capture and read
  `.Player.Inventory` off the result. Do not hand-roll a second
  `InventorySlotReading -> InventoryItem` mapping; this one is real,
  tested, and already the project's single source of truth for that
  conversion.
- `src/NosAi.Runtime/Gate1/Gate1ObservationChannel.cs` (read-only
  reference, line ~165): shows the real composition
  `IGameplayProvider provider = new NetworkGameplayProvider(feed);` and
  the decorator chain built on top of it
  (`MemoryTargetGameplayProvider`/`TargetAwareGameplayProvider`). Mirror
  this composition shape for `EngageCommand`'s live wiring — do not
  invent a second way to construct a live `IGameplayProvider`. If the
  exact feed/decorator wiring needs adjusting for a one-shot command
  context (rather than Gate 1's own long-running host), keep the change
  local to your own command's composition method, not to
  `Gate1ObservationChannel.cs` itself.
- `src/NosAi.Runtime/Navigation/WalkCommand.cs`: `WalkCommand.Execute`
  (same signature already used by `--walk`/`--scout`/unchanged by AP-05).
- `src/NosAi.Runtime/LowLevel/ActuationAuthority.cs`:
  `ActuationAuthority.Commanded(string operatorCommand)`.

Do not touch `Gate3Runtime.cs`, `ActionPlanner`, `InputActionEffector`,
`PostConditions.cs` (`CollectGroundItemPostCondition` is Gate3-owned and
explicitly declares no gesture implemented yet — this task does not
extend or fix it, it builds an independent path, same precedent as
`--scout`/`--engage`).

## OWN (new files only)

- `src/NosAi.Runtime/Navigation/CollectCommand.cs` (A4)
- `tests/NosAi.Runtime.Tests/CollectCommandTests.cs`

## MODIFY

- `src/NosAi.Runtime/Program.cs`: register the new flag (see §3).
  Additive only, same pattern as `--scout`/`--engage`'s wiring.

Do not modify any other existing file — in particular, do not add fields
to `GameplayObservation`, `InventorySlotReading`, or
`GameplayObservationProjector`: everything this task needs is already
there.

## Scope for this task, stated explicitly (read before writing code)

`--collect <x> <y> <vnum> [<requiredCount>]` verifies **one Collect
attempt at one operator-named position**. It does **not**:

- enumerate `GameplayObservation.GroundItems`/`WorldModelSnapshot.Drops`
  to find the nearest matching item automatically — that is a real,
  buildable future extension (a small, pure "nearest matching GroundItem"
  selection function), but it is not this task. Name it as a known gap in
  your completion report, do not build it now.
- walk to the item more than once, retry on failure, or attempt to pick
  a different item if the named one is not there. One walk, one
  before/after comparison, one process invocation. `--watch <n>` (same
  convention as `--scout`/`--engage`) may repeat the whole round `n`
  times against the same `<x> <y> <vnum>`, each round independent.
- build a `QuestObjectiveTarget`/`Quest` from scratch — the operator's
  `<vnum>` argument becomes the `ItemId` directly
  (`new ItemId(vnum)`, same string-of-the-number convention
  `GameplayObservationProjector` already uses); `requiredCount` is
  optional and, when given, is only used to decide whether to print
  "objective satisfied" — it does not gate whether the round runs.

This mirrors exactly how narrow `--scout`'s and `--engage`'s first slices
were — a small, honest, reviewable step, not the whole of AP-06/A2+A4's
eventual scope.

## 1. `CollectCommand.ExecuteOneRound` — testable, no I/O

```csharp
namespace NosAi.Runtime.Navigation;

public static class CollectCommand
{
    public const string Flag = "--collect";
    public const string OperatorSessionId = "operator-collect";

    /// <summary>
    /// Walks to <paramref name="destination"/> and reports whether the
    /// player's observed count of <paramref name="item"/> increased.
    /// </summary>
    /// <param name="before">The live snapshot taken immediately before the walk starts.</param>
    /// <param name="after">The live snapshot taken immediately after the walk finishes (whatever its outcome).</param>
    public static (WalkRun? Walk, WorldFact<int> Before, WorldFact<int> After) ExecuteOneRound(
        MapPoint destination,
        MapPoint origin,
        ItemId item,
        in MapGrid grid,
        OccupancyView view,
        PathWalkController controller,
        StepGuardChain chain,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        GameplayObservation before,
        GameplayObservation after,
        EntityId playerId,
        DateTime nowUtc)
    {
        var target = new QuestObjectiveTarget(QuestObjectiveKind.Collect, item: item);

        WorldModelSnapshot beforeSnapshot = GameplayObservationProjector.Project(before, playerId, version: 0, nowUtc);
        WorldFact<int> beforeCount = QuestGraphPlanner.AssessCollectProgress(target, beforeSnapshot.Player.Inventory, nowUtc);

        WalkRun run = WalkCommand.Execute(
            destination, origin, in grid, view, controller, chain, executor, in authority,
            readPosition, dryRun: false, sessionId: OperatorSessionId, timestampUtc: nowUtc);

        WorldModelSnapshot afterSnapshot = GameplayObservationProjector.Project(after, playerId, version: 0, nowUtc);
        WorldFact<int> afterCount = QuestGraphPlanner.AssessCollectProgress(target, afterSnapshot.Player.Inventory, nowUtc);

        return (run, beforeCount, afterCount);
    }
}
```

Notes:

- This signature is a starting point, not a contract to copy verbatim:
  match `WalkCommand.Execute`'s actual current parameter list exactly
  (read the file; do not guess at parameter names/order) and pass
  `before`/`after` in as already-captured `GameplayObservation` values —
  `ExecuteOneRound` itself performs no I/O and reads no clock, same
  testability discipline as `ScoutCommand.ExecuteOneRound`/
  `EngageCommand.ExecuteOneRound`.
- `version: 0` — `GameplayObservationProjector.Project`'s version
  parameter is for the World Model's own replay/versioning concern, not
  used by `AssessCollectProgress`; a fixed `0` here is honest (this
  command does not participate in that versioning) and must be documented
  as such in a one-line comment, not left unexplained.
- Interpreting the result (increased / unchanged / still unknown) is the
  caller's job (2b below) — this method returns both raw `WorldFact<int>`
  readings, it does not itself decide "success".

## 2. `CollectCommand.Run`/`RunWindows` — live composition

Console entry point, mirroring `EngageCommand.Run`/`RunWindows`'s
structure (`ClientMemorySession.TryAttach` for player position/map,
`GatedInputBackend`/`RuntimeComposition.CreateSafe()` for the walk itself
— copy that shape from `WalkCommand.RunWindows`; build the live
`IGameplayProvider` the same way `Gate1ObservationChannel.cs` does, per
§"Already built and real" above).

- `Run(int x, int y, string vnum, int? requiredCount, int rounds = 1)`:
  parses `<vnum>` into `new ItemId(vnum)`, builds `destination = new MapPoint(x, y)`.
- Each round: call `LiveObservationGateway.Capture()` for `before`,
  run `ExecuteOneRound`, call `LiveObservationGateway.Capture()` again
  for `after` (the "after" read happens once the walk attempt has ended,
  whatever its outcome — a stalled/aborted walk still gets a real
  after-reading, it is simply expected to show no change).
- Print, per round: the `WalkRun.Text` (same as `--walk`/`--scout`
  already do), then `before`/`after` counts and whether the count
  increased. If both `before`/`after` have a value and `after > before`,
  print `COLLECTED`; if both have a value and `after == before`, print
  `NO_CHANGE_OBSERVED`; if either is `Unknown`, print `UNOBSERVED` with
  the carried reason — never guess a direction from a value that was
  never confirmed.
- If `requiredCount` was given and the `after` count has a value
  `>= requiredCount`, print `OBJECTIVE_SATISFIED` in addition to the
  above — this is reporting only, it does not stop the loop or change
  the exit code.
- `authority: ActuationAuthority.Commanded(Flag)` for every round.
- If `ClientMemorySession.TryAttach` fails, or the live gameplay provider
  cannot be constructed: print `[REFUSED] {reason}` and return a non-zero
  exit code, same convention as `PlayerVitalsProbe`/`ScoutCommand`/
  `EngageCommand`.
- Do not persist evidence to a store in this task — printing is enough,
  same stated scope as `--scout`/`--engage`.

## 3. Wiring `Program.cs`

Additive, alongside the existing `--scout`/`--engage` block:

```csharp
if (args.Any(a => string.Equals(a, NosAi.Runtime.Navigation.CollectCommand.Flag, StringComparison.OrdinalIgnoreCase)))
{
    int collectIndex = Array.FindIndex(args, a => string.Equals(a, NosAi.Runtime.Navigation.CollectCommand.Flag, StringComparison.OrdinalIgnoreCase));
    if (collectIndex + 3 >= args.Length
        || !int.TryParse(args[collectIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
        || !int.TryParse(args[collectIndex + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
    {
        Console.WriteLine("[REFUSED] --collect requires <x> <y> <vnum> [<requiredCount>]");
        return 1;
    }

    string vnum = args[collectIndex + 3];
    int? requiredCount = collectIndex + 4 < args.Length
        && int.TryParse(args[collectIndex + 4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRequired)
        ? parsedRequired
        : null;

    int watchFlag = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
    int rounds = watchFlag >= 0 && watchFlag + 1 < args.Length
                 && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRounds)
                 && parsedRounds > 0
        ? parsedRounds
        : 1;

    return NosAi.Runtime.Navigation.CollectCommand.Run(x, y, vnum, requiredCount, rounds);
}
```

Place it next to the existing `--engage` block, not inside any other
conditional.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~CollectCommandTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

`CollectCommandTests` must cover, at minimum, using the `WalkCommandTests`
rig style (`WalkRig`/`ChainRig`/`RecordingInputBackend`): a `before`
`GameplayObservation` with inventory count 2 and an `after` with count 5
for the same vnum → `ExecuteOneRound` returns `Before.Value == 2`,
`After.Value == 5`; a `before`/`after` pair where the vnum is absent from
both → both counts `Live` and `0` (per `AssessCollectProgress`'s own
documented reasoning — inventory non-empty in the fixture, item simply
absent); a `before`/`after` pair built from an empty `Inventory`
`ClassifiedValue` (never observed) → both counts `Unknown`. All green, 0
warnings/0 errors, no regression in either full test suite (record exact
pass counts, same as every prior phase's status doc).

## Report back

Files created/modified; the exact `IGameplayProvider` composition you
used (cite the file/lines you mirrored it from); build/test evidence with
exact pass counts; the stated scope limitation (no automatic ground-item
discovery) named plainly as a known gap, not a silent omission; anything
found in `AP-06_A1_STATUS.md`/`QuestGraphPlanner.cs`/
`GameplayObservationProjector.cs` that looks wrong (do not fix it
yourself — report it for AP-06/A5's audit and AP-06/A6's integration, if
one is scheduled); verification level (`Present`/`Integrated`) and why.
