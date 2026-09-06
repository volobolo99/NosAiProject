using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Orchestration;
using NosAi.Storage;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The operator command that executes and verifies one
/// <see cref="CombatActionKind.UseConsumable"/> press at one operator-named
/// quickbar slot, as <c>--recover &lt;slot&gt;</c> (AP-05 "Combat
/// Intelligence", execute → verify — the recovery counterpart of
/// <see cref="EngageCommand"/>). It resolves the slot's key from the
/// operator's own <see cref="KeybindMap"/> (the <c>consumable.{slot}</c>
/// intent, confirmed binds only), presses it through the gated input
/// boundary, reads the player's vitals (the live memory chain
/// <c>ClientMemorySession.TryReadPlayerVitals</c>, the same reading
/// <c>--player-vitals</c> reports as <c>[LIVE]</c>) before and after, and
/// reports the verdict through
/// <see cref="NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector.ProjectRecovery"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honest scope (AP-05/A2A4 spec).</b> This verifies one
/// <see cref="CombatActionKind.UseConsumable"/> press at one slot and confirms
/// only what the player's own vitals can show: that <c>Health</c> specifically
/// rose after the press. It does <b>not</b> know that the slot holds a healing
/// item — the operator asserts that by naming the slot, the same
/// "operator names it directly" pattern <see cref="EngageCommand"/> uses for
/// the skill id. It does not check the consumable's remaining count, cooldown
/// or whether the slot is empty (<see cref="CombatPlanner"/>'s hard
/// constraints are not wired into this command — same scope as
/// <see cref="EngageCommand"/>). A slot that holds something else reports
/// <see cref="CombatExecutionResult.NoResourceChangeObserved"/> honestly, never
/// a fabricated gain. Every non-<c>UseConsumable</c> kind is refused by design,
/// the same way <see cref="EngageCommand"/> refuses non-<c>UseSkill</c> kinds.
/// </para>
/// <para>
/// <b>Known gap, stated plainly — the same one <see cref="EngageCommand"/>
/// carries.</b> <see cref="ExecuteOneRound"/> accepts an
/// <see cref="ActuationAuthority"/> and refuses a missing one, but this command
/// has no Guard/Trust/Safety gate of its own yet. The key press passes through
/// <see cref="GatedInputBackend"/> (policy, ADR-0003/ADR-0020): an unarmed
/// policy refuses the press by name and the evidence says so. But on the armed
/// production gate built by
/// <see cref="NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe"/>, the
/// <see cref="CommitPointValidator"/>'s five conditions re-read the desktop
/// facts of a <i>pixel act</i>, and a key press has no target pixel — so it
/// refuses with the commit point's scope-required reason and the round reports
/// it as <see cref="KeyPressNotAcceptedReason"/>. Bridging pixel-less acts to
/// that gate is a separate, later task — this command reports the refusal
/// rather than fabricating a point or skipping the gate. The
/// <c>authority</c> parameter is the act's attribution for the audit trail,
/// exactly as in <see cref="EngageCommand"/>.
/// </para>
/// </remarks>
public static class RecoverCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--recover";

    /// <summary>Session id of an operator recover round, not a Gate cycle.</summary>
    public const string OperatorSessionId = "operator-recover";

    /// <summary>How long the consumable key is held when pressed.</summary>
    private const int KeyPressMs = 80;

    /// <summary>How long the after-read waits for the consumable's effect to land.</summary>
    /// <remarks>
    /// Deliberately not a hardcoded value inside <see cref="ExecuteOneRound"/>:
    /// the delay is injected there (so the round is unit-testable without real
    /// time passing, same reasoning as the walk rig's clock injection); this is
    /// only the live shell's own choice of what to inject.
    /// </remarks>
    private const int VerificationDelayMs = 350;

    /// <summary>Reason recorded when the candidate kind is not this command's supported one.</summary>
    public const string UnsupportedKindReason = "recover_v1_supports_useconsumable_only";

    /// <summary>Reason recorded when the slot has no confirmed keybind.</summary>
    public const string KeybindNotConfirmedReason = "keybind_not_confirmed";

    /// <summary>Reason recorded when the gated backend refused the key press.</summary>
    public const string KeyPressNotAcceptedReason = "key_press_not_accepted";

    /// <summary>Reported off Windows, where there is no client to attach to.</summary>
    public const string NotWindowsReason = "recover_requires_windows";

    /// <summary>Reported when the operator's keybind file cannot be read.</summary>
    public const string KeybindsUnavailableReason = "keybinds_unavailable";

    /// <summary>Reported when the client cannot be attached for a vitals read.</summary>
    public const string ClientNotReadableReason = "client_not_readable";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "recover_input_backend_not_gated";

    /// <summary>
    /// Reported when <c>slot</c> is less than one or <c>rounds</c> is less than
    /// one. <see cref="Program"/>'s dispatch only checks that the flag has a
    /// following argument and that it parses as an integer, so a caller that
    /// reaches this command with a non-positive value (or an automation harness
    /// driving it from a not-yet-resolved slot) must get a clean refusal here,
    /// not an unhandled exception — the same [REFUSED] boundary every other
    /// guard in this command already gives, and the exact defect the AP-05 and
    /// AP-06 audits found in <see cref="EngageCommand.Run"/> and
    /// <see cref="CollectCommand.Run"/>, fixed at birth here.
    /// </summary>
    public const string InvalidArgumentsReason = "recover_requires_positive_slot_and_rounds";

    /// <summary>
    /// Executes and verifies one <see cref="CombatActionKind.UseConsumable"/>
    /// press at <paramref name="slot"/>. Every non-<c>UseConsumable</c> kind is
    /// refused before any input is emitted — see the class remarks.
    /// </summary>
    /// <remarks>
    /// Testable without a desktop: the keybind map, the input backend, the
    /// vitals reads and the verification delay are all injected. No real time
    /// passes unless the caller's <paramref name="verificationDelay"/> says so.
    /// </remarks>
    /// <param name="candidate">
    /// The act to execute and verify. Must be <see cref="CombatActionKind.UseConsumable"/>;
    /// its <see cref="CombatActionCandidate.Item"/> carries the slot number as
    /// its id (see the class-level explanation of why the candidate is built
    /// that way).
    /// </param>
    /// <param name="slot">The operator-named quickbar slot, <c>1</c>-based, pressed through the <c>consumable.{slot}</c> intent.</param>
    /// <param name="keybinds">The operator's keybind map, resolved to the consumable intent <c>consumable.{slot}</c>.</param>
    /// <param name="input">The input boundary the key press goes through (gated in production).</param>
    /// <param name="readVitals">Reads the player's current vitals, or <see langword="null"/> when none can be read right now.</param>
    /// <param name="verificationDelay">What happens between the press and the after-read. Injected so tests need no real delay.</param>
    /// <param name="authority">
    /// The authority of the act: <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>).
    /// A missing authority is refused by name; see the class remarks for the
    /// honest statement of what this parameter is and is not.
    /// </param>
    /// <param name="nowUtc">The instant this round runs at, stamped on every fact it creates.</param>
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
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(keybinds);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(readVitals);
        ArgumentNullException.ThrowIfNull(verificationDelay);

        if (authority.Kind == ActuationAuthorityKind.None)
            return CombatExecutionEvidence.NotAttempted(candidate, ActuationAuthority.MissingReason, nowUtc);

        if (candidate.Kind != CombatActionKind.UseConsumable)
            return CombatExecutionEvidence.NotAttempted(candidate, UnsupportedKindReason, nowUtc);

        string intent = $"{KeybindsCheck.ConsumablePrefix}{slot.ToString(CultureInfo.InvariantCulture)}";
        if (!keybinds.TryGet(intent, out Keybind bind) || !bind.Confirmed)
            return CombatExecutionEvidence.NotAttempted(candidate, KeybindNotConfirmedReason, nowUtc);

        PlayerVitalsReading? before = readVitals();
        bool accepted = input.KeyPress(bind.VirtualKey, KeyPressMs);
        if (!accepted)
            return CombatExecutionEvidence.NotAttempted(candidate, KeyPressNotAcceptedReason, nowUtc);

        verificationDelay();
        PlayerVitalsReading? after = readVitals();

        return NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector.ProjectRecovery(candidate, before, after, nowUtc);
    }

    /// <summary>
    /// Console entry for <c>--recover &lt;slot&gt;</c>, optionally
    /// <c>--watch &lt;n&gt;</c> rounds (default 1).
    /// </summary>
    public static int Run(int slot, int rounds = 1)
    {
        if (slot < 1 || rounds < 1)
        {
            Console.WriteLine($"[REFUSED] {InvalidArgumentsReason}");
            return WalkCommand.ExitAbandoned;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(slot, rounds);
    }

    /// <summary>
    /// The live composition, mirroring <see cref="EngageCommand"/>'s shape:
    /// attach to the running client, load the operator's keybinds, take the
    /// gated input backend from <see cref="RuntimeComposition.CreateSafe"/>, then
    /// drive <see cref="ExecuteOneRound"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(int slot, int rounds)
    {
        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure))
        {
            Console.WriteLine($"[REFUSED] {ClientNotReadableReason}:{attachFailure}");
            return WalkCommand.ExitAbandoned;
        }

        using (session)
        {
            string keybindsPath = KeybindsCheck.ResolvePath();
            if (!KeybindMap.TryLoad(keybindsPath, out KeybindMap keybinds, out string? loadFailure))
            {
                Console.WriteLine($"[REFUSED] {KeybindsUnavailableReason}:{loadFailure} ({keybindsPath})");
                return WalkCommand.ExitAbandoned;
            }

            RuntimeComponents components = RuntimeComposition.CreateSafe();
            if (components.InputBackend is not GatedInputBackend gated)
            {
                Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
                return WalkCommand.ExitAbandoned;
            }

            ClientMemorySession attached = session!;
            Func<PlayerVitalsReading?> readVitals = () =>
                attached.TryReadPlayerVitals(out PlayerVitalsReading reading, out _)
                    ? reading
                    : null;

            // The act is a key press through the gate, which refuses while live
            // input is not armed. The refusal is printed and reported; this
            // command never arms input itself.
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);

            // Ledger persistence is opportunistic history, never a gate: a
            // missing NOSAI-SSD volume warns and records nothing -- it must
            // never become a reason this command refuses.
            using ActionOutcomeLedgerStore? ledgerStore =
                ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? ledgerFailure);
            if (ledgerStore is null)
                Console.WriteLine($"[WARN] action_outcome_ledger_unavailable:{ledgerFailure}");

            for (int round = 1; round <= rounds; round++)
            {
                // The CombatActionCandidate constructor requires an Item. There is
                // no catalogue vnum here -- the operator named a quickbar slot, and
                // the slot number is the only identity this command knows or needs
                // (the same reason InputActionEffector presses consumable.{slot}
                // rather than an item id). The slot number doubles as the item id
                // so the candidate the evidence carries is the one the operator
                // actually named.
                string slotAsId = slot.ToString(CultureInfo.InvariantCulture);
                var candidate = new CombatActionCandidate(
                    CombatActionKind.UseConsumable,
                    item: new ItemId(slotAsId));

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"=== recover round {round} of {rounds}: consumable slot {slot} ==="));

                DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
                CombatExecutionEvidence evidence = ExecuteOneRound(
                    candidate,
                    slot,
                    keybinds,
                    gated,
                    readVitals,
                    verificationDelay: () => Thread.Sleep(VerificationDelayMs),
                    in authority,
                    nowUtc);

                PrintEvidence(evidence);

                NosAi.Runtime.WorldModel.Fusion.ActionOutcomeRecorder.RecordCombat(
                    ledgerStore,
                    new ActionId(Guid.NewGuid().ToString("N")),
                    candidate,
                    issuedAtUtc: nowUtc,
                    evidence,
                    MemoryType.Combat,
                    context: $"consumable-slot:{slot}",
                    recordedAtUtc: nowUtc);

                // The purpose of the invocation is a confirmed recovery. A round
                // that aborted (unsupported kind, missing/unconfirmed keybind,
                // refused press) is deterministic -- retrying it would repeat the
                // same refusal, so stop. A round that ran but saw no gain (e.g.
                // the slot holds a non-healing item, or the player was already at
                // full Health) might be transient, which is exactly what --watch
                // is for: keep going to the next round.
                if (evidence.Result == CombatExecutionResult.ResourceGainConfirmed)
                    return 0;

                if (evidence.Result == CombatExecutionResult.Aborted)
                    return WalkCommand.ExitAbandoned;

                if (round < rounds)
                    Console.WriteLine();
            }

            // Every round ran and none confirmed a recovery.
            return 1;
        }
    }

    /// <summary>One evidence line per round, in the same spirit as EngageCommand.PrintEvidence.</summary>
    public static void PrintEvidence(CombatExecutionEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        string resource = evidence.ResourceObserved?.ToString() ?? "none";
        string before = evidence.Before.HasValue
            ? evidence.Before.Value.ToString("F0", CultureInfo.InvariantCulture)
            : "UNKNOWN";
        string after = evidence.After.HasValue
            ? evidence.After.Value.ToString("F0", CultureInfo.InvariantCulture)
            : "UNKNOWN";
        string detail = evidence.Detail is { } named ? $" ({named})" : string.Empty;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"recover-evidence: {evidence.Result} resource={resource} before={before} after={after}{detail}"));
    }
}
