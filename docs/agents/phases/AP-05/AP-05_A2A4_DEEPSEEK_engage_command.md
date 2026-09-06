# AP-05 / A2+A4 — DeepSeek — The `--engage` operator command

## The decision this task executes

`docs/agents/phases/AP-05/AP-05_A1_STATUS.md` §"Indagine su Gate3Runtime per
skill/attacco — conclusa" left two paths open and started neither. The
decision is now made: **path (a)** — a player-vitals-only combat
verification, independent of `Gate3Runtime`. Path (b) (closing AP-02's
Mob HP fusion gap first) stays blocked on OCR/ONNX, a data/model-asset
problem no contract-writing task here can close; waiting on it would block
AP-05's execute/verify stages indefinitely for no gain the honest scope
below cannot already deliver.

**What this buys, and what it deliberately does not:** it confirms *"this
skill act had a resource cost"* (the player's own MP fell). It does
**not** confirm the target was hit, damaged, or affected in any way —
`Mob.Status.Resources` is not populated today (AP-02's entity fusion is
blocked on the same OCR/ONNX gap). `CombatExecutionEvidence`
(`src/NosAi.Core/WorldModel/Combat/CombatExecutionContracts.cs`, already
written, read it before starting) exists exactly to keep that boundary
explicit in the type itself, not just in prose — every one of its
members says so.

## Why this shape, and not a Gate3Runtime bridge

Same read-only-investigation discipline as AP-04's `--scout`, applied here
by the AP-05/A1 agent already (`AP-05_A1_STATUS.md` §"Indagine su
Gate3Runtime..."): `Gate3Runtime.ActionPlanner.Plan` generates skill
candidates with a **hardcoded** `SkillOrItemId = 201` (one literal
occurrence in the whole file), `SimulationEngine.Simulate` returns a fixed
placeholder (`hpDelta=-15`/`mpDelta=-35`) that never reads which skill was
used, and no real skill damage/cost data exists anywhere in the repository
(`GameReferenceDatabase` explicitly declines to decode those fields).
`UseBasicAttackPostCondition`/`UseSkillPostCondition`
(`src/NosAi.Runtime/Gate3/PostConditions.cs`) are real and do check
observed HP/MP direction from the wire, but are tightly coupled to
`Gate3Runtime`'s own private `Gate3WorldState`/`ReadBackAsync` (private
instance)/`CollectSightings` (private static) — not reusable by an
independent command without duplicating that private machinery. Do not
touch `Gate3Runtime.cs`, `ActionPlanner`, `SimulationEngine`,
`GuardPolicyEngine` or `PostConditions.cs` in this task.

What **is** real, tested and reusable today, confirmed by direct
inspection for this task:

- `NosAi.LiveIntegration.ClientMemorySession.TryReadPlayerVitals(out PlayerVitalsReading reading, out string? failureReason)`
  (`src/NosAi.Runtime/LiveIntegration/ClientMemorySession.cs`) — the exact
  chain `NosAi.LiveIntegration.PlayerVitalsProbe` (`--player-vitals`)
  already reports as `[LIVE]`: a memory chain
  (`NosTaleClientLayout.PlayerVitalsModuleOffset` →
  `MaxHpChainOffset`/`MaxMpChainOffset`) validated twice against the wire
  in two separate sessions, with an anchor that survived a client restart.
  `PlayerVitalsReading(uint Hp, uint MaxHp, uint Mp, uint MaxMp)`
  (`src/NosAi.Runtime/LiveIntegration/PlayerVitals.cs`). This is the same
  family of primitive `ScoutCommand` already used
  (`ClientMemorySession.TryReadPlayer` for position) — same session type,
  same `TryAttach` pattern, no new attach mechanism to invent.
- `NosAi.Runtime.LowLevel.KeybindMap`/`Keybind(ushort VirtualKey, string Label, bool Confirmed)`
  (`src/NosAi.Runtime/LowLevel/KeybindMap.cs`) — real, file-backed
  (`KeybindMap.RelativePath = "data/keybinds.json"`), `TryLoad`/`TryGet`.
  `KeybindsCheck.SkillPrefix = "skill."` is the exact intent naming
  `InputActionEffector` already composes internally
  (`Gate3/InputActionEffector.cs`, private `PressKey`, line ~272) — reuse
  the naming convention, not that private method.
- `NosAi.Runtime.LowLevel.IInputBackend.KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)`
  (`src/NosAi.Runtime/LowLevel/Win32InputBackend.cs`), implemented by
  `GatedInputBackend` (`src/NosAi.Runtime/LowLevel/GatedInputBackend.cs`)
  — the same gated backend family `WalkCommand`/`SingleStepExecutor`
  already use for movement input. Not exclusive to `Gate3Runtime`: this
  confirms a "press a skill key" primitive independent of it.
- `NosAi.Runtime.LowLevel.ActuationAuthority.Commanded(string operatorCommand)`
  (ADR-0020, exactly two legitimate authority kinds) — same family
  `--walk`/`--scout` already use for an operator-started routine; no new
  authority kind, no bypass.

## OWN (new files only)

- `src/NosAi.Runtime/WorldModel/Fusion/CombatVerificationProjector.cs` (A2)
- `src/NosAi.Runtime/Tactical/EngageCommand.cs` (A4)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/CombatVerificationProjectorTests.cs`
- `tests/NosAi.Runtime.Tests/EngageCommandTests.cs`

## MODIFY

- `src/NosAi.Runtime/Program.cs`: register the new flag (see §3). Additive
  only, same pattern as `--scout`'s wiring.

Do not modify any other existing file. In particular: do not add a
parameter to `InputActionEffector.PressKey` (private, Gate3-owned) — this
task's execution path is independent of it by design, not an extension of
it.

## Scope for this task, stated explicitly (read before writing code)

`--engage` verifies **one `UseSkill` act, named directly by the operator**
(target entity id + skill id as command-line arguments). It does **not**:

- run `CombatPlanner.GenerateCandidates`/`CheckHardConstraints`
  (`src/NosAi.Core/WorldModel/Combat/CombatPlanner.cs`) — those need a
  fully-fused `Player`/`EquatableArray<Mob>` (skills, cooldowns, live mob
  positions), which nothing in this one-shot live-composition context
  assembles today. Wiring that fusion is a separate, later task (mirrors
  `ScoutCommand`'s own documented limitation: `mobs: EquatableArray<Mob>.Empty`
  because no live mob feed is wired into that command's context either).
  Do not build that fusion here — out of scope.
- support `CombatActionKind.BasicAttack`. `CombatExecutionEvidence`'s own
  contract says why: `BasicAttack` has no known player-side resource cost,
  so verifying it through this mechanism would always produce
  `ResourceObserved: null`/`Unobserved` — no information. Refuse it
  cleanly (see `ExecuteOneRound` below), do not invent a workaround.
- attempt any repositioning, targeting-by-click, or multi-step combo. One
  skill, one verification, one process invocation. `--watch <n>` (same
  convention as `--scout`) may repeat this round `n` times, each with its
  own independent before/after read — it does not chain rounds into a
  combo.

This mirrors exactly how narrow `--scout`'s first slice was (one frontier
round, no mob feed, no multi-map routing) — a small, honest, reviewable
first step, not the whole of AP-05/A2+A4's eventual scope.

## 1. `CombatVerificationProjector` (A2)

Pure, mechanical bridge — same role as AP-04/A2's
`MovementVerificationProjector`: `NosAi.Core` never references
`NosAi.Runtime`, so this is where a real `PlayerVitalsReading` becomes a
`CombatExecutionEvidence`.

```csharp
namespace NosAi.Runtime.WorldModel.Fusion;

public static class CombatVerificationProjector
{
    public const string ResourceNotObservableReason = "resource_kind_not_observable_for_kind";
    public const string VitalsNotObservedReason = "vitals_not_observed";

    /// <summary>Which ResourceKind CombatActionKind.UseSkill is expected to spend. Null for every other kind -- see AP-05_A2A4 scope.</summary>
    public static ResourceKind? ExpectedResource(CombatActionKind kind) => kind switch
    {
        CombatActionKind.UseSkill => ResourceKind.Mana,
        _ => null
    };

    public static CombatExecutionEvidence Project(
        CombatActionCandidate candidate,
        PlayerVitalsReading? before,
        PlayerVitalsReading? after,
        DateTime observedAtUtc)
    {
        ResourceKind? resource = ExpectedResource(candidate.Kind);
        if (resource is null)
        {
            return new CombatExecutionEvidence(
                candidate, null,
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                WorldFact<double>.Unknown(ResourceNotObservableReason, observedAtUtc),
                CombatExecutionResult.Unobserved, ResourceNotObservableReason, observedAtUtc);
        }

        if (before is not { } b || after is not { } a)
        {
            return new CombatExecutionEvidence(
                candidate, resource,
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
                CombatExecutionResult.Unobserved, VitalsNotObservedReason, observedAtUtc);
        }

        double beforeValue = ResourceValue(b, resource.Value);
        double afterValue = ResourceValue(a, resource.Value);
        WorldFact<double> beforeFact = WorldFact<double>.Live(beforeValue, confidence: 1d, observedAtUtc);
        WorldFact<double> afterFact = WorldFact<double>.Live(afterValue, confidence: 1d, observedAtUtc);

        CombatExecutionResult result = afterValue < beforeValue
            ? CombatExecutionResult.ResourceCostConfirmed
            : CombatExecutionResult.NoResourceChangeObserved;

        return new CombatExecutionEvidence(candidate, resource, beforeFact, afterFact, result, null, observedAtUtc);
    }

    private static double ResourceValue(PlayerVitalsReading reading, ResourceKind kind) => kind switch
    {
        ResourceKind.Mana => reading.Mp,
        ResourceKind.Health => reading.Hp,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind,
            "CombatVerificationProjector only reads Health/Mana off PlayerVitalsReading; extend ResourceValue before using another ResourceKind.")
    };
}
```

Test: every `CombatActionKind` other than `UseSkill` maps to
`ExpectedResource(kind) == null` and `Project` returns `Unobserved`/
`ResourceNotObservableReason` regardless of `before`/`after`; `UseSkill`
with `before is null` or `after is null` returns `Unobserved`/
`VitalsNotObservedReason`; `UseSkill` with `after.Mp < before.Mp` returns
`ResourceCostConfirmed` with `Before`/`After` as `Live` facts carrying the
exact Mp values; `UseSkill` with `after.Mp >= before.Mp` returns
`NoResourceChangeObserved`. Use a `CombatActionCandidate` built with
`CombatActionKind.UseSkill` (needs `Skill` set; `Target` optional per the
existing constructor) for the positive cases, and one of each other `Kind`
for the negative sweep.

## 2. `EngageCommand` (A4 — the heavy lot)

Same two-layer split as `ScoutCommand`: a pure, testable round, and a thin
live-composition shell.

### 2a. `EngageCommand.ExecuteOneRound` — testable, no I/O

```csharp
namespace NosAi.Runtime.Tactical;

public static class EngageCommand
{
    public const string Flag = "--engage";
    public const string OperatorSessionId = "operator-engage";
    private const int KeyPressMs = 80;

    /// <summary>
    /// Executes and verifies one UseSkill act. Every non-UseSkill Kind is
    /// refused before any input is emitted -- see AP-05_A2A4 scope.
    /// </summary>
    public static CombatExecutionEvidence ExecuteOneRound(
        CombatActionCandidate candidate,
        KeybindMap keybinds,
        IInputBackend input,
        Func<PlayerVitalsReading?> readVitals,
        Action verificationDelay,
        in ActuationAuthority authority,
        DateTime nowUtc)
    {
        if (candidate.Kind != CombatActionKind.UseSkill)
            return CombatExecutionEvidence.NotAttempted(candidate, "engage_v1_supports_useskill_only", nowUtc);

        string intent = $"{NosAi.Runtime.LowLevel.KeybindsCheck.SkillPrefix}{candidate.Skill!.Value.Value}";
        if (!keybinds.TryGet(intent, out Keybind bind) || !bind.Confirmed)
            return CombatExecutionEvidence.NotAttempted(candidate, "keybind_not_confirmed", nowUtc);

        PlayerVitalsReading? before = readVitals();
        bool accepted = input.KeyPress(bind.VirtualKey, KeyPressMs);
        if (!accepted)
            return CombatExecutionEvidence.NotAttempted(candidate, "key_press_not_accepted", nowUtc);

        verificationDelay();
        PlayerVitalsReading? after = readVitals();

        return NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector.Project(candidate, before, after, nowUtc);
    }
}
```

Notes:

- `authority` is accepted and must be asserted present (same as
  `WalkCommand.Execute`'s own `in ActuationAuthority authority` parameter)
  even though this round has no Guard/Trust/Safety gate of its own to pass
  through yet — name this honestly in your completion report as a gap
  (this command's execution is not yet bridged to the real Safety Gate the
  same way `WalkCommand`'s `StepGuardChain` bridges movement), do not
  silently drop the parameter or fabricate a check that does not exist.
- `verificationDelay` is injected (not a hardcoded `Thread.Sleep`)
  specifically so `ExecuteOneRound` stays unit-testable without real time
  passing — same reasoning as `WalkRig`'s clock injection in
  `WalkCommandTests.cs`. `RunWindows` passes a real
  `() => Thread.Sleep(...)`; tests pass a no-op.
- `readVitals` is a `Func<PlayerVitalsReading?>` so a test can hand back
  two different fixed readings (before/after) without a real
  `ClientMemorySession` — mirrors `ScoutCommand.ExecuteOneRound`'s own
  `Func<PositionReading?> readPosition` parameter.

### 2b. `EngageCommand.Run`/`RunWindows` — live composition

Console entry point, mirroring `ScoutCommand.Run`/`RunWindows`'s structure
(same `ClientMemorySession.TryAttach`, `KeybindMap.TryLoad` from
`KeybindsCheck.ResolvePath()`, `GatedInputBackend` construction — copy that
composition shape, do not reinvent it).

- `Run(string targetEntityId, string skillId, int rounds = 1)`:
  parses `EntityId`/`SkillId` from the two required arguments, builds one
  `CombatActionCandidate(CombatActionKind.UseSkill, target: new EntityId(targetEntityId), skill: new SkillId(skillId))`
  once, then calls `ExecuteOneRound` up to `rounds` times (`--watch <n>`
  convention, default `1`), printing each returned
  `CombatExecutionEvidence` (`Result`/`ResourceObserved`/`Before`/`After`/
  `Detail`) to the console, one line per round.
- If `KeybindMap.TryLoad` fails, or `ClientMemorySession.TryAttach` fails:
  print `[REFUSED] {reason}` and return a non-zero exit code, same
  convention as `PlayerVitalsProbe`/`ScoutCommand`.
- `authority: ActuationAuthority.Commanded(Flag)` for every round.
- Do not persist evidence to a store in this task — printing is enough,
  same as `ScoutCommand`'s own stated scope for this first slice.

## 3. Wiring `Program.cs`

Additive, alongside the existing `--scout`/`--walk` block:

```csharp
if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.EngageCommand.Flag, StringComparison.OrdinalIgnoreCase)))
{
    int engageIndex = Array.FindIndex(args, a => string.Equals(a, NosAi.Runtime.Tactical.EngageCommand.Flag, StringComparison.OrdinalIgnoreCase));
    if (engageIndex + 2 >= args.Length)
    {
        Console.WriteLine("[REFUSED] --engage requires <targetEntityId> <skillId>");
        return 1;
    }

    string targetEntityId = args[engageIndex + 1];
    string skillId = args[engageIndex + 2];

    int watchFlag = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
    int rounds = watchFlag >= 0 && watchFlag + 1 < args.Length
                 && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRounds)
                 && parsedRounds > 0
        ? parsedRounds
        : 1;

    return NosAi.Runtime.Tactical.EngageCommand.Run(targetEntityId, skillId, rounds);
}
```

Place it next to the existing `--scout` block, not inside any other
conditional.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~CombatVerificationProjectorTests|FullyQualifiedName~EngageCommandTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

`EngageCommandTests` must cover, at minimum: a `UseSkill` candidate with a
confirmed keybind, an accepted keypress, and `after.Mp < before.Mp` →
`ResourceCostConfirmed`; the same with `after.Mp == before.Mp` →
`NoResourceChangeObserved`; an unconfirmed/missing keybind →
`NotAttempted`/`Aborted` with `Detail == "keybind_not_confirmed"`, and the
input backend's `KeyPress` never called (assert via a recording fake, same
style as `RecordingInputBackend` in `WalkCommandTests.cs`); a
`BasicAttack` (or any non-`UseSkill`) candidate → `NotAttempted` without
reading vitals or touching the input backend at all. All green, 0
warnings/0 errors, no regression in either full test suite (record exact
pass counts, same as every prior phase's status doc).

## Report back

Files created/modified; build/test evidence with exact pass counts; the
two scope limitations stated plainly in your own words (BasicAttack
refused by design, no bridge to a real Safety Gate yet for this
command) — name them as known gaps, not silent omissions; anything found
in `AP-05_A1_STATUS.md`/`CombatContracts.cs`/`CombatPlanner.cs` that looks
wrong (do not fix it yourself — report it for AP-05/A5's audit and
AP-05/A6's integration, if/when this phase gets one); verification level
(`Present`/`Integrated`) and why.
