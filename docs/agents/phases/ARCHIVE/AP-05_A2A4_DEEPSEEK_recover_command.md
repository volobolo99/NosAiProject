# AP-05 / A2+A4 — DeepSeek — The `--recover` operator command

## Why this task exists

AP-08's investigation (`docs/agents/phases/AP-08/AP-08_A1_STATUS.md`
§"AP-08/A2+A4") found that `StrategyPlanner.AssessSurvivalUrgency`
(already real, AP-08/A3) has no real response to act on: pressing a
healing consumable already has a real, Gate3Runtime-independent
execution primitive (`CombatActionKind.UseConsumable`,
`KeybindsCheck.ConsumablePrefix = "consumable."` — the same
`KeybindMap`+`GatedInputBackend.KeyPress` mechanism `--engage` already
uses for skills), but nothing verifies it. This is AP-05 work (the
`CombatActionKind.UseConsumable` candidate lives in AP-05's own
`CombatContracts.cs`), not new AP-08 infrastructure — `--recover` is a
sibling of `--engage`, not a new subsystem.

**What this buys, and what it deliberately does not:** it confirms *"the
player's own Health rose after pressing this consumable slot"* — a real,
memory-confirmed vitals reading, not a guess. It does **not** know that
slot holds a healing item — the operator asserts that by choosing which
slot to name, the same "operator names it directly" pattern `--engage`
already uses for the skill id. A slot that turns out to hold something
else (a buff scroll, an unbind scroll) will honestly report
`NoResourceChangeObserved`, never a fabricated gain.

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/Combat/CombatExecutionContracts.cs`:
  `CombatExecutionResult.ResourceGainConfirmed` (already added this
  task, read it in full — the recovery counterpart to
  `ResourceCostConfirmed`, direction is the caller's assertion per its
  own doc comment) and `CombatExecutionEvidence.ResourceGainConfirmed`
  (bool convenience property, mirrors `ResourceCostConfirmed`).
- `src/NosAi.Core/WorldModel/Combat/CombatContracts.cs`:
  `CombatActionCandidate(CombatActionKind.UseConsumable, item: ItemId)` —
  requires `Item`, rejects `Target`/`Skill`/`Destination` (already
  enforced by the constructor).
- `src/NosAi.Runtime/WorldModel/Fusion/CombatVerificationProjector.cs`
  (AP-05/A2, already delivered): read `Project`/`ExpectedResource` in
  full. Do not change `ExpectedResource`'s existing mapping or `Project`'s
  existing behavior for `UseSkill` — both are already correct and tested.
  Add a **new**, separate method (see §1) rather than overloading the
  existing one: `Project` answers "which resource does this `Kind`
  generally spend" (a question with no honest general answer for
  `UseConsumable`, per the contract's own doc comment); this task answers
  a narrower, operator-asserted question ("did Health specifically rise
  after this particular consumable") that only makes sense when the
  operator has already told you which slot they believe heals.
- `src/NosAi.Runtime/Gate3/InputActionEffector.cs` line ~249-251 (read-only
  reference): confirms the real precedent —
  `ActionType.UseConsumable => PressKey(candidate, $"consumable.{slot.Slot}", clock)`
  presses by **slot number**, not item id (the comment there explains
  why: the operator's quickbar slot, not a catalogue number). Mirror this
  choice: `--recover` takes a slot number as its argument, not a vnum.
- `src/NosAi.Runtime/Tactical/EngageCommand.cs` (already delivered,
  audited, integrated): the structural template to mirror closely —
  `ExecuteOneRound` shape, `Run`/`RunWindows` composition
  (`ClientMemorySession.TryAttach`, `KeybindMap.TryLoad` from
  `KeybindsCheck.ResolvePath()`, `RuntimeComposition.CreateSafe()` for
  the gated backend), the `[REFUSED]`-on-blank-argument fix
  (`AP-05_A5_AUDIT.md` Difetto 1 — apply the same validation shape from
  the start here, do not reintroduce that defect).

## OWN (new files only)

- `src/NosAi.Runtime/Tactical/RecoverCommand.cs` (A4)
- `tests/NosAi.Runtime.Tests/RecoverCommandTests.cs`

## MODIFY

- `src/NosAi.Runtime/WorldModel/Fusion/CombatVerificationProjector.cs`:
  add one new **additive** public method (§1 below). Do not change any
  existing method's signature or behavior.
- `src/NosAi.Runtime/Program.cs`: register the new flag (see §3).

Do not touch `EngageCommand.cs`, `Gate3Runtime.cs`, `InputActionEffector.cs`,
`PostConditions.cs`.

## Scope, stated explicitly

`--recover <slot>` verifies **one `UseConsumable` press at one
operator-named slot**, asserting Health as the tracked resource. It does
**not**:

- discover which slot holds a healing item — the operator names it
  directly, same as `--engage`'s skill id.
- know or check the consumable's remaining count, cooldown, or whether
  the slot is empty — `CombatPlanner`'s hard constraints are not wired
  into this command (same "operator asserts, runtime verifies" scope as
  `--engage`).
- support any `CombatActionKind` other than `UseConsumable`. Refuse
  cleanly for anything else, same pattern as `--engage` refusing
  non-`UseSkill`.

## 1. `CombatVerificationProjector.ProjectRecovery` (A2 addition)

```csharp
/// <summary>
/// Projects one UseConsumable act's before/after Health into recovery
/// evidence. Unlike <see cref="Project"/>, the tracked resource is not
/// inferred from <paramref name="candidate"/>'s Kind (UseConsumable has no
/// general answer -- see this type's own remarks) -- it is always Health,
/// because the caller (an operator naming a slot as a recovery item) is
/// the one asserting that. A slot that does not actually heal reports
/// NoResourceChangeObserved honestly, never a fabricated gain.
/// </summary>
/// <param name="candidate">Must be CombatActionKind.UseConsumable; any other Kind returns Unobserved/ResourceNotObservableReason, same convention as Project.</param>
public static CombatExecutionEvidence ProjectRecovery(
    CombatActionCandidate candidate,
    PlayerVitalsReading? before,
    PlayerVitalsReading? after,
    DateTime observedAtUtc)
{
    if (candidate.Kind != CombatActionKind.UseConsumable)
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
            candidate, ResourceKind.Health,
            WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
            WorldFact<double>.Unknown(VitalsNotObservedReason, observedAtUtc),
            CombatExecutionResult.Unobserved, VitalsNotObservedReason, observedAtUtc);
    }

    WorldFact<double> beforeFact = WorldFact<double>.Live(b.Hp, confidence: 1d, observedAtUtc);
    WorldFact<double> afterFact = WorldFact<double>.Live(a.Hp, confidence: 1d, observedAtUtc);

    CombatExecutionResult result = a.Hp > b.Hp
        ? CombatExecutionResult.ResourceGainConfirmed
        : CombatExecutionResult.NoResourceChangeObserved;

    return new CombatExecutionEvidence(candidate, ResourceKind.Health, beforeFact, afterFact, result, null, observedAtUtc);
}
```

Test: a `UseConsumable` candidate with `after.Hp > before.Hp` →
`ResourceGainConfirmed`; `after.Hp <= before.Hp` →
`NoResourceChangeObserved` (equal counts as no gain, same "fails toward
under-claiming" discipline `Project` already uses); either `before`/
`after` null → `Unobserved`/`VitalsNotObservedReason`; a non-`UseConsumable`
candidate → `Unobserved`/`ResourceNotObservableReason` regardless of
vitals, mirroring `Project`'s own sweep.

## 2. `RecoverCommand` (A4)

Same two-layer split as `EngageCommand`.

### 2a. `ExecuteOneRound` — testable, no I/O

```csharp
namespace NosAi.Runtime.Tactical;

public static class RecoverCommand
{
    public const string Flag = "--recover";
    public const string OperatorSessionId = "operator-recover";
    private const int KeyPressMs = 80;

    public const string UnsupportedKindReason = "recover_v1_supports_useconsumable_only";
    public const string KeybindNotConfirmedReason = "keybind_not_confirmed";
    public const string KeyPressNotAcceptedReason = "key_press_not_accepted";
    public const string InvalidArgumentsReason = "recover_requires_positive_slot_and_rounds";

    public static CombatExecutionEvidence ExecuteOneRound(
        CombatActionCandidate candidate,
        int slot,
        KeybindMap keybinds,
        IInputBackend input,
        Func<PlayerVitalsReading?> readVitals,
        Action verificationDelay,
        in ActuationAuthority authority,
        DateTime nowUtc)
    {
        // Same shape as EngageCommand.ExecuteOneRound: authority check first
        // (ActuationAuthority.MissingReason on ActuationAuthorityKind.None),
        // then Kind check (UnsupportedKindReason for anything but
        // UseConsumable), then keybind lookup on $"{KeybindsCheck.ConsumablePrefix}{slot}"
        // (KeybindNotConfirmedReason unless bind.Confirmed), then
        // before-read -> KeyPress -> accepted check (KeyPressNotAcceptedReason) ->
        // verificationDelay() -> after-read -> CombatVerificationProjector.ProjectRecovery.
    }

    public static int Run(int slot, int rounds = 1) { /* [REFUSED] InvalidArgumentsReason for slot < 1 or rounds < 1, mirroring AP-05_A5_AUDIT.md's fix -- do not reintroduce ArgumentException.ThrowIfNullOrWhiteSpace-style throws here */ }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static int RunWindows(int slot, int rounds) { /* mirror EngageCommand.RunWindows exactly: ClientMemorySession.TryAttach, KeybindMap.TryLoad, RuntimeComposition.CreateSafe() gated backend check, loop rounds with ActuationAuthority.Commanded(Flag), print each evidence */ }
}
```

Build a `CombatActionCandidate(CombatActionKind.UseConsumable, item: new ItemId(slot.ToString(CultureInfo.InvariantCulture)))`
once per round (the constructor requires `Item`; the slot number doubles
as the id here since no catalogue vnum is known or needed — name this
choice in a one-line comment, do not leave it unexplained). Intent string
for the keybind lookup: `$"{KeybindsCheck.ConsumablePrefix}{slot}"`.

Loop-stopping logic in `RunWindows`: `ResourceGainConfirmed` → return 0;
`Aborted` → return `WalkCommand.ExitAbandoned`; anything else continues to
the next round if any remain (same reasoning `EngageCommand` already
documents: a round that ran but did not confirm a gain might be
transient, `--watch` exists for exactly that).

## 3. Wiring `Program.cs`

Additive, alongside the existing `--engage` block:

```csharp
if (args.Any(a => string.Equals(a, NosAi.Runtime.Tactical.RecoverCommand.Flag, StringComparison.OrdinalIgnoreCase)))
{
    int recoverIndex = Array.FindIndex(args, a => string.Equals(a, NosAi.Runtime.Tactical.RecoverCommand.Flag, StringComparison.OrdinalIgnoreCase));
    if (recoverIndex + 1 >= args.Length
        || !int.TryParse(args[recoverIndex + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int recoverSlot))
    {
        Console.WriteLine("[REFUSED] --recover requires <slot>");
        return 1;
    }

    int watchFlag = Array.FindIndex(args, a => string.Equals(a, "--watch", StringComparison.OrdinalIgnoreCase));
    int rounds = watchFlag >= 0 && watchFlag + 1 < args.Length
                 && int.TryParse(args[watchFlag + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedRounds)
                 && parsedRounds > 0
        ? parsedRounds
        : 1;

    return NosAi.Runtime.Tactical.RecoverCommand.Run(recoverSlot, rounds);
}
```

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~RecoverCommandTests|FullyQualifiedName~CombatVerificationProjectorTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

`RecoverCommandTests` must cover, at minimum, the same sweep
`EngageCommandTests` already established for its own command: confirmed
keybind + accepted press + `after.Hp > before.Hp` → `ResourceGainConfirmed`;
same with `after.Hp <= before.Hp` → `NoResourceChangeObserved`;
unconfirmed/missing keybind → `NotAttempted`/`Aborted` with the key press
never reaching the backend; a non-`UseConsumable` candidate →
`NotAttempted` without reading vitals or touching input; `Run` with
`slot < 1` or `rounds < 1` → `[REFUSED]`/`ExitAbandoned`, never an
unhandled exception (this is the exact defect `AP-05_A5_AUDIT.md` found
in `EngageCommand.Run` and `AP-06_A5_AUDIT.md` found again in
`CollectCommand.Run` — do not let it happen a third time). All green, 0
warnings/0 errors, no regression in either full test suite.

## Report back

Files created/modified; build/test evidence with exact pass counts;
verification level (`Present`/`Integrated`) and why; anything found in
`CombatVerificationProjector.cs`/`EngageCommand.cs` that looks wrong (do
not fix it yourself — report it).
