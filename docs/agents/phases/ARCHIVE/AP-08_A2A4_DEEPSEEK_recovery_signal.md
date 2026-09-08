# AP-08 / A2+A4 — DeepSeek — Wire `StrategicGoalKind.Recovery` into `--autoplay`

## Read this section before anything else

`REGOLA ASSOLUTA` and `REGOLA ASSOLUTA #2` in `docs/agents/DEEPSEEK_TASKS.md`
apply unchanged: complete files only, no `TODO`, no stub, code and tests
only — no doc file in this repository is yours to edit except the report
`CLAUDE.md` asks for at the end.

This is a **small, self-contained increment** on an already-`Integrated`
command (`AutoplayCommand`, AP-08). It does not touch the CLI surface, does
not add a new observation channel, does not open any new driver or need
elevation. Read that as a hard constraint, not a suggestion: if your
implementation needs a new CLI flag, a new endpoint, or a new live source to
finish this task, stop and report the blocker — it means you have drifted
from this spec, not found a missing piece of it.

## Why this task exists

`docs/agents/phases/AP-10/AP-10_A1_STATUS.md` (stage 12, "Recovery") names
the exact gap this closes: `StrategyPlanner` assesses Survival, QuestUrgency
and Exploration, but not Recovery, because nothing in this repository
established a real "currently in combat" fact to gate it on — a
threshold-only guess would have double-counted Survival's own signal under
a different name. Claude has now closed that gap on the `NosAi.Core` side
(pure contracts/algorithm, no I/O, already built — see "Already built and
real" below). What is missing is exactly two things, both mechanical
wiring, both inside `AutoplayCommand`:

1. Feed `AutoplayCommand.RunWindows`'s own live HP readings (already read
   every cycle for Survival) through the new pure combat-recency tracker to
   get a real `WorldFact<bool>` for "in combat", and
2. Add the resulting `Recovery` signal to the list `SelectStrategicPlan`
   already picks from, and dispatch it when selected.

## Already built and real — read before writing code

All in `NosAi.Core.WorldModel.Strategy`, already merged to `main`, already
tested (build/test commands at the end of this file reproduce the numbers):

- `CombatRecencyTracker` (`src/NosAi.Core/WorldModel/Strategy/CombatRecencyTracker.cs`)
  — pure, stateless per call. `State.Initial` is the starting state (before
  any poll). `Update(State previous, double currentHp, DateTime nowUtc,
  TimeSpan? decayWindow = null)` returns `(WorldFact<bool> InCombat, State
  Next)`: feed `Next` back in as `previous` on the following poll, the same
  way `AutoplayCommand.RunWindows`'s existing `previousReading`/`footprint`
  locals already carry state across its `for` loop. `InCombat` is
  `WorldFact<bool>.Unknown("insufficient_history", ...)` on the very first
  poll (nothing to compare against yet) — never treated as "not in combat".
  From the second poll on it is `WorldFact<bool>.Derived(...)`: true when
  the most recent observed HP *drop* was within `DefaultDecayWindow` (15s,
  or whatever `decayWindow` you pass) of `nowUtc`, false otherwise. An HP
  *rise* is never counted as damage. Read the type's own doc comments for
  the honestly-stated limits (a DoT effect also counts as "in combat"; a
  fully-absorbed hit with no net HP loss does not) — do not try to improve
  on this heuristic, it is a deliberate, declared trade-off, not an
  oversight.
- `StrategyPlanner.AssessRecoveryUrgency(Player player, WorldFact<bool>
  inCombat)` (`src/NosAi.Core/WorldModel/Strategy/StrategyPlanner.cs`) —
  pure. Returns `null` when `inCombat` is Unknown, when
  `inCombat.Value == true`, or when the player's HP fraction is unknown
  (same `Resources`/`ResourceKind.Health` shape `AssessSurvivalUrgency`
  already reads — you are not adding a new way to read HP, `AutoplayCommand`
  already builds this exact `Player` shape for `AssessSurvivalUrgency`, one
  call site above where you will add this one). Otherwise returns a
  `StrategicSignal(StrategicGoalKind.Recovery, urgency, reason)` with
  `urgency = 1 - fraction` (reason `"safe_to_recover"`), or urgency `0`
  (reason `"health_full"`) at full health.
- Tests proving both, already green:
  `tests/NosAi.Core.Tests/WorldModel/Strategy/CombatRecencyTrackerTests.cs`,
  `tests/NosAi.Core.Tests/WorldModel/Strategy/StrategyPlannerTests.cs`
  (`AssessRecoveryUrgency_*` cases).

You are not asked to touch any of the files above. Read
`CombatRecencyTracker.cs` and the `AssessRecoveryUrgency` doc comments in
full before writing the wiring below — every design decision they encode
(why the gate exists, why it returns null rather than false, why urgency
mirrors Survival's own formula) is intentional and is not yours to revisit.

## OWN (new files only)

- `tests/NosAi.Runtime.Tests/AutoplayRecoveryDispatchTests.cs` (new file —
  keep the existing `AutoplayCommandTests.cs` untouched rather than growing
  an unrelated section into it; this class covers only the new dispatch
  branch, same pattern as the existing file).

## MODIFY

- `src/NosAi.Runtime/Tactical/AutoplayCommand.cs` — the two sections below,
  nothing else in this file. In particular: do not touch `Run`'s public
  signature, do not add a CLI parameter, do not touch `Program.cs` (no
  wiring change is needed there — `--autoplay`'s existing flag parsing
  already reaches `Run(cycles, recoverSlot)` unchanged).

## 1. `ExecuteOneCycle` — add the `Recovery` dispatch branch (testable, no I/O)

`ExecuteOneCycle` (around line 166) already switches on `plan.SelectedKind`
without knowing or caring how the plan was assessed — this is why it is
independently testable with a hand-built `StrategicPlan`, and why this part
of the task needs no live client at all.

Add `AutoplayDispatch.RecoverySkippedNoSlot = 5` to the `enum
AutoplayDispatch` (after `NotDispatchable = 4`), doc-commented the same way
its Survival sibling is: `"Recovery was selected, but no recovery slot was
configured."` Do not reuse `SurvivalSkippedNoSlot` for this — the two must
stay distinguishable in the printed/audited outcome, the same reason
`AutoplayDispatch` already names every branch instead of collapsing them.

In the `switch (kind)` block, add a `case StrategicGoalKind.Recovery:`
immediately after the existing `case StrategicGoalKind.Survival:` block.
Its body is **the same action** `Survival` already dispatches — both
represent "use the configured consumable slot", they differ only in *why*
they fired (an unconditional HP-fraction read vs. the same read gated on
being confirmed safe) — so do not duplicate the candidate-construction/
`RecoverCommand.ExecuteOneRound` call inline a second time. Extract the
existing `Survival` case body into a private static method (something like
`DispatchRecovery(int? recoverSlot, KeybindMap keybinds, IInputBackend
input, Func<PlayerVitalsReading?> readVitals, Action verificationDelay, in
ActuationAuthority authority, DateTime nowUtc, ExplorationFootprint
footprint, StrategicPlan plan, AutoplayDispatch skippedNoSlotDispatch)` that
returns the `AutoplayCycleResult` for both cases — call it from
`case StrategicGoalKind.Survival:` with `skippedNoSlotDispatch:
AutoplayDispatch.SurvivalSkippedNoSlot` and from
`case StrategicGoalKind.Recovery:` with `skippedNoSlotDispatch:
AutoplayDispatch.RecoverySkippedNoSlot`. Exact parameter list/`in` usage is
your call as long as it compiles and both call sites end up calling one
shared body — the constraint that matters is "one implementation, two named
outcomes", not the exact refactor shape.

Update the method's own doc comment and the class-level remark above it
(currently: `"<see cref="StrategicGoalKind.Recovery"/> now has a real
assessor ... but this command does not yet call it -- wiring it into this
cycle ... is a separate, not-yet-delivered increment (AP-08/A2A4,
`AP-08_A2A4_DEEPSEEK_recovery_signal.md`)."`, added by Claude ahead of this
task) to say plainly that Recovery is now assessed and dispatched, the same
way Survival is documented.

## 2. `RunWindows` — track combat recency and add the signal

Inside the `for (int cycle = 1; cycle <= cycles; cycle++)` loop (around line
374), the composition already tracks state across cycles the same shape you
need: `previousReading` (declared before the loop, updated at the end of
each iteration) is the precedent to copy.

- Before the loop: `CombatRecencyTracker.State combatState =
  CombatRecencyTracker.State.Initial;`
- This cycle's `vitals` is already read earlier in the loop
  (`attached.TryReadPlayerVitals(out PlayerVitalsReading vitals, ...)`, do
  not change that call) but `now` (`DateTime now =
  TimeProvider.System.GetUtcNow().UtcDateTime;`) is only computed a few
  lines later, once `mapId` has also been read — call
  `(WorldFact<bool> inCombat, combatState) = CombatRecencyTracker.Update(combatState, vitals.Hp, now);`
  right after that `now` line (or anywhere between it and where
  `playerFacts` is built, your choice), reusing that exact `now` rather
  than reading the clock a second time. There must be exactly one `now` per
  cycle, reused everywhere, matching how the rest of the loop already
  treats it.
- Where `playerFacts` (the minimal `Player`) is already built for
  `AssessSurvivalUrgency` (around line 424-445 as of this spec — search for
  the comment `"A minimal Player carrying only what AssessSurvivalUrgency
  reads"` if the line has moved): no change needed to `playerFacts` itself,
  `AssessRecoveryUrgency` reads the exact same `Status.Resources` shape.
- Right after `StrategicSignal? survival =
  StrategyPlanner.AssessSurvivalUrgency(playerFacts);`, add:
  `StrategicSignal? recovery = StrategyPlanner.AssessRecoveryUrgency(playerFacts, inCombat);`
- In the `signals` list construction, add `recovery` **before** `survival`:

  ```csharp
  var signals = new List<StrategicSignal>(3);
  if (recovery is not null) signals.Add(recovery);
  if (survival is not null) signals.Add(survival);
  if (exploration is not null) signals.Add(exploration);
  ```

  This order is deliberate, not arbitrary: `SelectStrategicPlan` breaks a
  tied `Urgency` by picking whichever signal appears first in the list
  (`StrategyPlanner.cs`, already tested,
  `SelectStrategicPlan_TiedUrgency_PicksTheFirstOne`). `Recovery` and
  `Survival` compute the identical `1 - fraction` urgency for the same HP
  reading. **Correction (post-delivery independent audit):** an earlier
  draft of this rationale claimed the two are "by construction never both
  non-null at once" — that is false: whenever the character is out of
  combat with known HP below full, both fire at once with the same
  `Urgency` (this is the common case, not an edge case). The list order is
  exactly what decides the outcome in that case: putting `recovery` first
  makes it win the tie, which is the intended behaviour (out of combat, the
  more specific signal wins over the general one). Do not reorder this
  without updating this rationale.

## Tests

`AutoplayRecoveryDispatchTests.cs` (new): mirror
`AutoplayCommandTests.cs`'s existing Survival-dispatch tests exactly, but
construct a `StrategicPlan` with `SelectedKind =
StrategicGoalKind.Recovery` directly (as the existing file already does for
`Survival`/`Exploration` — `ExecuteOneCycle` does not care how the plan was
produced). Cover at minimum:

- `Recovery` selected + a configured slot -> dispatches to
  `RecoverCommand.ExecuteOneRound`, result `AutoplayDispatch.Recovered`
  (mirror the existing Survival case byte-for-byte, just with
  `StrategicGoalKind.Recovery`).
- `Recovery` selected + no slot configured -> `AutoplayDispatch.RecoverySkippedNoSlot`,
  distinguishable from `SurvivalSkippedNoSlot` (assert the exact enum
  value, not just "not equal").
- The refactored `Survival` case still produces exactly the outcomes the
  existing tests in `AutoplayCommandTests.cs` already assert — run that
  file unmodified after your refactor and confirm every existing test in it
  still passes; if any fails, the extraction changed observable behaviour
  and must be fixed, not the test.

No test is expected or wanted for `RunWindows` itself (Windows-only,
real-client-attached, same limitation every other cycle-composition test in
this command already accepts — see the existing file's own scope note).

## Build/test

```
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

Expect 0 new errors/warnings, every existing test in both projects still
green, plus your new file's tests. Report the exact numbers (not "all
green") in your completion report, the same as every other delivery in this
project.

## Report back

Per `CLAUDE.md`: task id (`AP-08/A2A4 — Recovery signal`), files
created/modified, the exact build/test commands and their numeric results,
verification level (`Present` — this closes the wiring gap but is not
`Verified`/`Integrated` until an operator runs `--autoplay` against a real
client and observes a real Recovery dispatch; say this explicitly, do not
claim more), blockers if any, and handoff notes. Report in Italian, per
`CLAUDE.md`'s "Economia dei token e lingua"; code/identifiers/comments in
English as everywhere else in this repository.
