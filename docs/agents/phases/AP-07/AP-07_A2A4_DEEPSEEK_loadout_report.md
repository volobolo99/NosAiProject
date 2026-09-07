# AP-07 / A2+A4 — DeepSeek — `--loadout-report`: real Equip candidates from the item catalog

## Read this section before anything else

`REGOLA ASSOLUTA`, `REGOLA ASSOLUTA #2` and `REGOLA ASSOLUTA #3` in
`docs/agents/DEEPSEEK_TASKS.md` apply unchanged: complete files only, code
and tests only, and **every claim below that ends up in a comment or test
must be re-verified against the real files cited, not copied on trust**.
REGOLA ASSOLUTA #3 exists because of a real defect in the previous task
(AP-08/A2A4) that shipped a false comment copied verbatim from that spec —
do not repeat it here.

This task creates one **new, read-only diagnostic command**,
`--loadout-report`. It does **not** equip or unequip anything: no mouse
drag, no inventory click, no new keybind. `--equip`/`--unequip` stay
explicitly out of scope (see `docs/agents/DEEPSEEK_TASKS.md`, T-12's
second half is still open) — this command only *reports* what
`LoadoutPlanner` would propose right now and whether each proposal passes
hard constraints, the same read-only spirit as `--route`
(`RouteProbe`)/`--reference-info` (`ReferenceInfoCommand`).

## Why this task exists

`src/NosAi.Core/WorldModel/Loadout/LoadoutPlanner.cs`'s class remarks used
to say "nothing in this repository decodes an item's equipment category
yet" — false since `src/NosAi.Runtime/GameData/ItemReferenceDecoder.cs`
(commit `e597c0d`) started decoding `Item.dat`'s real `EquipmentSlot`
field. Claude has closed the `NosAi.Core`-side half of this gap (pure
contract + algorithm, already built and tested — see below). What remains
is wiring a **real** slot lookup from the on-disk catalog into it and
reading a **real** live inventory/equipment to report against — both
mechanical, both inside `NosAi.Runtime`.

## Already built and real — read before writing code

**`NosAi.Core.WorldModel.Loadout.LoadoutPlanner`** (already merged to
`main`, already tested):

- `GenerateEquipCandidates(Player player, Func<ItemId, EquipmentSlot?> resolveSlot)`
  — new. One `LoadoutActionCandidate` per inventory stack with known
  quantity `> 0` that `resolveSlot` maps to a real slot; a stack
  `resolveSlot` has no answer for produces **no candidate**, never a
  guessed one. Pure: it does not know or care where `resolveSlot`'s answer
  came from.
- `GenerateUnequipCandidates(Player)`, `GenerateUpgradeCandidates(Player)`,
  `CheckHardConstraints(LoadoutActionCandidate, Player)` — pre-existing,
  unchanged, already what this command needs for the other two action
  kinds and for judging every candidate.
- Tests: `tests/NosAi.Core.Tests/WorldModel/Loadout/LoadoutPlannerTests.cs`
  (`GenerateEquipCandidates_*` cases prove the null-propagation and
  zero/unknown-quantity behaviour above — read them, they are the
  executable spec for what `resolveSlot` is allowed to assume).

**Already real in `NosAi.Runtime`, nothing here needs to be invented**:

- `NosAi.Runtime.GameData.GameReferenceLocator.TryOpen(out GameReferenceDatabase? database, out string? failureReason): bool`
  (`src/NosAi.Runtime/GameData/GameReferenceLocator.cs`) — opens the
  catalogue on the `NOSAI-SSD` volume without creating one. Absence is a
  named, honest refusal (`GameReferenceLocator.DatabaseNotFound` etc.),
  not an exception. Mirror `ReferenceInfoCommand.Format` (same file's
  neighbour, `src/NosAi.Runtime/Observability/ReferenceInfoCommand.cs`)
  for the pattern of treating a missing catalogue as a reported condition,
  not a crash.
- `GameReferenceDatabase.Lookup(string kind, int vnum): IReadOnlyList<NosField>?`
  (`src/NosAi.Runtime/GameData/GameReferenceDatabase.cs`, around line 619)
  — the item table's `kind` string is exactly `"item"`
  (`src/NosAi.Runtime/GameData/ReferenceImporter.cs:69`). Returns `null`
  when nothing in the catalogue carries that vnum.
- `NosRecord(int? Vnum, IReadOnlyList<NosField> Fields)` and
  `NosAi.Runtime.GameData.ItemReferenceDecoder.Decode(NosRecord): ItemReference?`
  — wrap a `Lookup` result as `new NosRecord(vnum, fields)` and decode it;
  `ItemReference.Slot: EquipmentSlot?` is the field this task needs
  (`src/NosAi.Runtime/GameData/ItemReferenceDecoder.cs`).
- `NosAi.Runtime.WorldModel.Fusion.GameplayObservationProjector.Project(GameplayObservation observation, EntityId playerId, long version, DateTime nowUtc): WorldModelSnapshot`
  (`src/NosAi.Runtime/WorldModel/Fusion/GameplayObservationProjector.cs:39`)
  — turns a live `GameplayObservation` into a full `WorldModelSnapshot`
  whose `.Player` carries real `Inventory`/`Equipment`. `version: 0` is
  correct here (a one-shot report does not participate in World Model
  replay/versioning) — `CollectCommand.ExecuteOneRound`
  (`src/NosAi.Runtime/Navigation/CollectCommand.cs:138`) already does
  exactly this with the same `version: 0` and the same honest comment
  explaining why; copy that comment's reasoning, not just its code.
- The live-attach boilerplate this command needs (find the client window,
  attach `ClientMemorySession`, open the packet-based gameplay gateway,
  capture one `GameplayObservation`) already exists, real and tested, as
  `CollectCommand`'s own `Run`/`RunWindowsCore`/private `LiveScope`
  (`src/NosAi.Runtime/Navigation/CollectCommand.cs`, `Run` starts around
  line 180, `LiveScope` around line 375 — search for `private sealed class
  LiveScope` if the line moved). **Mirror this
  composition field-for-field** (`RuntimeComponents`, `TryFindWindow`,
  `ClientMemorySession.TryAttach`, `LiveScope.TryOpen`,
  `gateway.Capture().Gameplay`) the same way `ScoutCommand`/`WalkCommand`/
  `SingleStepCommand` each already carry their own private copy of
  `TryFindWindow` — this project's established (if repetitive) way of
  keeping each command's live-attach path self-contained, not a shortcut
  to invent around. Do **not** build a new capture/decoder composition:
  the one `CollectCommand` already has is real, already exercised by its
  own tests, and this command needs nothing more from it than one
  `Capture().Gameplay` call.

## OWN (new files only)

- `src/NosAi.Runtime/Tactical/LoadoutReportCommand.cs`
- `tests/NosAi.Runtime.Tests/LoadoutReportCommandTests.cs`

## MODIFY

- `src/NosAi.Runtime/Program.cs` — add the flag dispatch (see below) and
  add `"--loadout-report"` to `KnownProbeFlags`
  (`src/NosAi.Runtime/Program.cs`, around line 1047). Nothing else in this
  file.

## 1. `LoadoutReportCommand` — pure core (testable, no I/O)

```csharp
public readonly record struct LoadoutReport(
    IReadOnlyList<LoadoutConstraintCheck> EquipChecks,
    IReadOnlyList<LoadoutConstraintCheck> UnequipChecks,
    IReadOnlyList<LoadoutConstraintCheck> UpgradeChecks);

public static LoadoutReport Build(Player player, Func<ItemId, EquipmentSlot?> resolveSlot)
```

Generates all three candidate kinds via the three `LoadoutPlanner`
generators above, runs `CheckHardConstraints` on every one of them against
`player`, and returns the three result lists. No `Console`, no clock read,
no field access outside `player`/`resolveSlot` — same discipline
`AutoplayCommand.ExecuteOneCycle` already follows, so this is testable
with the exact hand-built `Player`/fake `resolveSlot` pattern
`LoadoutPlannerTests.cs` already uses.

## 2. `BuildResolveSlot` — the real catalog lookup (Runtime-only, not pure)

```csharp
public static Func<ItemId, EquipmentSlot?> BuildResolveSlot(GameReferenceDatabase database)
```

For a given `ItemId`, parse `.Value` as `int` (`int.TryParse` with
`NumberStyles.Integer`/`CultureInfo.InvariantCulture` — the same
convention `GameplayObservationProjector.cs` uses to build an `ItemId`
from a vnum in the first place, see its line ~63). On parse failure,
return `null`. Otherwise `database.Lookup("item", vnum)`; on `null`,
return `null`; otherwise wrap as `new NosRecord(vnum, fields)`, decode
with `ItemReferenceDecoder.Decode`, and return the result's `?.Slot`
(propagating `null` all the way through when the record itself has no
`VNUM` field, which `Decode` already refuses honestly).

## 3. Console entry — `Run()`

`Flag = "--loadout-report"`, no required arguments. Attach exactly as
`CollectCommand.Run` does (see "Already built and real" above) down to
capturing one `GameplayObservation` from the gateway. Then:

1. `EntityId playerId` — build the same honest per-process id
   `CollectCommand.RunWindowsCore` builds (`player-{processId}`), with
   the same comment explaining why it is not a real wire character id.
2. `WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, playerId, version: 0, TimeProvider.System.GetUtcNow().UtcDateTime);`
3. Open the catalogue: `GameReferenceLocator.TryOpen(out database, out failureReason)`.
   - If it fails: print `[WARN] loadout-report: catalog unavailable ({failureReason}) -- Equip candidates cannot be resolved, Unequip/Upgrade still reported` and use `resolveSlot = _ => null` (never abort the whole report over a missing catalogue — Unequip/Upgrade need no catalog at all).
   - If it opens: `using` it, build `resolveSlot` via `BuildResolveSlot`.
4. `LoadoutReport report = Build(snapshot.Player, resolveSlot);`
5. Print one line per check in each of the three lists, e.g.:
   `equip candidate: item=<id> slot=<slot> allowed=<bool> violations=<comma-joined ViolatedConstraints, or "none">`
   (and the Unequip/Upgrade equivalents, `item=`/`slot=` populated from whichever the candidate's `Kind` actually carries). Print a one-line count summary at the end (`N equip, M unequip, K upgrade candidates`) mirroring `LiveWireMonitor`'s final summary line style.
6. Return `0` always (a report with zero candidates in every list is a
   valid, honest result, not a failure) — `[REFUSED]`/non-zero exit is
   reserved for the attach-boilerplate failures already established by
   `CollectCommand`'s own pattern (window not found, session attach
   failed, gateway open failed).

## Tests

`LoadoutReportCommandTests.cs` (new):

- `Build`: at least one test per candidate kind proving it appears in the
  right list and `CheckHardConstraints` actually ran (an item in
  inventory `resolveSlot` maps to a free slot -> allowed Equip check; a
  slot already occupied -> a violated Equip check with
  `"slot_already_occupied"`; an equipped item -> both an Unequip and an
  Upgrade check). Build `Player`/`resolveSlot` by hand, the same pattern
  `LoadoutPlannerTests.cs` already uses — do not attach anything live for
  these.
- `BuildResolveSlot`: against `GameReferenceDatabase.OpenInMemory()`,
  seeded with `db.Import("item", "test.NOS", "item.dat", "C:/test", records, payloadBytes)`
  — the exact signature `GameReferenceDatabaseTests.cs`'s own `Import`
  helper wraps (`tests/NosAi.Runtime.Tests/GameReferenceDatabaseTests.cs`,
  around line 33 — that helper hardcodes `kind: "monster"`, so write your
  own call with `"item"` instead of reusing it as-is). Build each seed
  record as `new NosRecord(vnum, new[] { new NosField("INDEX", new[] { "0", "0", "0", "<slotCode>", "0", "0" }) })`
  — `ItemReferenceDecoder` reads the slot from the `INDEX` field's 4th
  value (index 3), exactly as `ItemReferenceDecoderTests.cs` already
  proves (`tests/NosAi.Runtime.Tests/GameData/ItemReferenceDecoderTests.cs`,
  `ADecodedRecord_ProducesEveryNamedField_FromItsOwnTagPositions` reads
  `INDEX` position 3 into `item.Slot`) — do not invent a different tag
  layout. Cover: one real vnum whose seeded `INDEX` decodes to a slot, one
  vnum absent from the catalog (`Lookup` returns null) that resolves to
  `null`, one `ItemId` that is not a valid integer at all that resolves to
  `null` without throwing.
- No test is expected or wanted for `Run()` itself (Windows-only,
  real-client-attached, same limitation every other command's console
  entry already accepts — see any existing `*CommandTests.cs`'s own scope
  note).

## Build/test

```
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

Expect 0 new errors/warnings, **every existing test in both projects
still green** (report the exact before/after numbers, not "all green" —
REGOLA ASSOLUTA #3 above exists specifically because a prior delivery
skipped this and shipped a regression in a file it never touched
directly), plus the new file's tests.

## Report back

Task id (`AP-07/A2A4 — --loadout-report`), files created/modified, exact
build/test commands and numeric results, verification level (`Present` —
this closes the wiring gap but is not `Verified`/`Integrated` until an
operator runs `--loadout-report` against a real client with a real
catalog on `NOSAI-SSD` and confirms at least one real Equip candidate
against an item actually in their inventory; say this explicitly), any
blocker, handoff notes. Report in Italian per `CLAUDE.md`'s "Economia dei
token e lingua"; code/identifiers/comments in English as everywhere else
in this repository.
