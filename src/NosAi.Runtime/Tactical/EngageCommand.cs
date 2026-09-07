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
/// The operator command that executes and verifies one <c>UseSkill</c> act
/// (AP-05 "Combat Intelligence", execute → verify), named directly by the
/// operator as <c>--engage &lt;targetEntityId&gt; &lt;skillId&gt;</c>: it
/// resolves the skill's key from the operator's own
/// <see cref="KeybindMap"/> (the <c>skill.{id}</c> intent, confirmed binds
/// only), presses it through the gated input boundary, reads the player's
/// vitals (the live memory chain
/// <c>ClientMemorySession.TryReadPlayerVitals</c>, the same reading
/// <c>--player-vitals</c> reports as <c>[LIVE]</c>) before and after, and
/// reports the verdict through
/// <see cref="NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honest scope (AP-05/A2A4 spec).</b> This first slice verifies exactly
/// one <see cref="CombatActionKind.UseSkill"/> act per round and confirms only
/// what the player's own resources can show: that the act had a real resource
/// cost (the player's <c>Mp</c> fell). It does <b>not</b> confirm the target
/// was hit, damaged or affected in any way -- <c>Mob.Status.Resources</c> is
/// not populated anywhere today. The reason is Runtime plumbing, not the
/// OCR/ONNX gap an earlier version of this remark named: see
/// <see cref="NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector"/>'s
/// own remarks for what the wire already carries and what is still missing.
/// <see cref="CombatActionKind.BasicAttack"/> (and every other
/// non-<c>UseSkill</c> kind) is refused by design: those acts have no
/// observable player-side resource pool, so verifying them through this
/// mechanism would always produce an empty answer -- see
/// <see cref="ExecuteOneRound"/>. Candidate *generation* from
/// <c>CombatPlanner</c>, repositioning and multi-step combos stay out of
/// scope: the operator still names the target and the skill.
/// </para>
/// <para>
/// <b>The target is verified before anything is pressed.</b> The operator's
/// entity id used to be taken on trust -- nothing checked that it named a real
/// entity, or one that is hostile, alive and in range. Every round now
/// observes the world through <see cref="LiveCombatObserver"/> and judges the
/// candidate with <see cref="CombatPlanner.CheckTargetConstraints"/>; a
/// violated verdict refuses the round by name, before the key press, and the
/// refusal reads identically to what <see cref="CombatReportCommand"/> prints
/// for the same entity, because both compute it from the same observation.
/// The verdict is re-taken every round rather than once, and a target refusal
/// therefore does not end the invocation the way a keybind refusal does: a mob
/// walks back into range, and one that has not attacked yet becomes
/// established the moment it does.
/// </para>
/// <para>
/// Two consequences worth stating plainly. The check needs the packet
/// capture, so this command now needs Administrator; and when the capture or
/// the client read fails it <b>refuses</b> rather than proceeding unverified
/// -- see <see cref="TargetNotVerifiableReason"/> for why that direction and
/// not the other.
/// </para>
/// <para>
/// <b>Known gap, stated plainly.</b> <see cref="ExecuteOneRound"/> accepts an
/// <see cref="ActuationAuthority"/> and refuses a missing one, but this
/// command has no Guard/Trust/Safety gate of its own yet: it is not bridged
/// to the real Safety Gate the way <c>WalkCommand</c>'s
/// <see cref="NosAi.Runtime.Navigation.StepGuardChain"/> bridges movement.
/// The key press passes through <see cref="GatedInputBackend"/> (policy,
/// ADR-0003/ADR-0020) -- an unarmed policy refuses the press by name and the
/// evidence says so. But note the real boundary's shape: the production gate
/// built by <see cref="NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe"/>
/// wires a <see cref="CommitPointValidator"/>, and that validator's five
/// conditions re-read the desktop facts of a <i>pixel act</i> (window
/// geometry, foreground, the exact screen point, the operator, the scale) --
/// a skill key press has no target pixel, so it cannot open the
/// scope-and-commit-point path the way a click does. On the armed production
/// gate a bare press therefore refuses with the commit point's
/// scope-required reason and the round reports it as
/// <see cref="KeyPressNotAcceptedReason"/>. Bridging skill execution to that
/// gate (what a pixel-less act's commit should be) is a separate, later task
/// -- this slice reports the refusal rather than fabricating a point or
/// skipping the gate. Do not read the <c>authority</c> parameter as a check
/// that exists where the gate does not: it is the act's attribution for the
/// audit trail, same as every operator command in this project.
/// </para>
/// </remarks>
public static class EngageCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--engage";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Tactical";

    /// <summary>Session id of an operator engage round, not a Gate cycle.</summary>
    public const string OperatorSessionId = "operator-engage";

    /// <summary>How long a skill key is held when pressed.</summary>
    private const int KeyPressMs = 80;

    /// <summary>Reason recorded when the candidate kind is not this command's supported one.</summary>
    public const string UnsupportedKindReason = "engage_v1_supports_useskill_only";

    /// <summary>Reason recorded when the skill has no confirmed keybind.</summary>
    public const string KeybindNotConfirmedReason = "keybind_not_confirmed";

    /// <summary>Reason recorded when the gated backend refused the key press.</summary>
    public const string KeyPressNotAcceptedReason = "key_press_not_accepted";

    /// <summary>Reported off Windows, where there is no client to attach to.</summary>
    public const string NotWindowsReason = "engage_requires_windows";

    /// <summary>Reported when the operator's keybind file cannot be read.</summary>
    public const string KeybindsUnavailableReason = "keybinds_unavailable";

    /// <summary>Reported when the client cannot be attached for a vitals read.</summary>
    public const string ClientNotReadableReason = "client_not_readable";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "engage_input_backend_not_gated";

    /// <summary>
    /// Reported when the target could not be judged at all: no packet capture,
    /// or the client's own position could not be read.
    /// </summary>
    /// <remarks>
    /// This is a refusal, not a warning, and the direction is deliberate. An
    /// entity nothing has established stays unknown, and the unknown does not
    /// authorise an act (ADR-0016) -- the same rule
    /// <see cref="NosAi.Runtime.Autonomy.TargetEstablishment"/> is built on.
    /// Acting on an unverified id is exactly what this command used to do, and
    /// carrying on when the check cannot run would keep doing it while looking
    /// like it did not. The practical consequence: the packet capture needs
    /// Administrator, so this command does too.
    /// </remarks>
    public const string TargetNotVerifiableReason = "engage_target_not_verifiable";

    /// <summary>Reported when the target was judged and the hard constraints refused it.</summary>
    /// <remarks>
    /// The violated constraint names come straight from
    /// <see cref="CombatPlanner.CheckTargetConstraints"/> -- <c>target_not_found</c>,
    /// <c>target_not_hostile</c>, <c>target_not_alive</c>,
    /// <c>target_position_unknown</c>, <c>target_out_of_range</c> -- so a
    /// refusal here reads identically to the verdict <c>--combat-report</c>
    /// prints for the same entity.
    /// </remarks>
    public const string TargetRefusedReason = "engage_target_refused";

    /// <summary>
    /// Reported when <c>targetEntityId</c>/<c>skillId</c> is present but blank,
    /// or <c>rounds</c> is less than one. <see cref="Program"/>'s dispatch only
    /// checks argument *count*, not content, so a caller that resolves an
    /// entity id to an empty string (e.g. an automation harness driving this
    /// command from a not-yet-fused id) must get a clean refusal here, not an
    /// unhandled exception -- the same [REFUSED] boundary every other guard in
    /// this command already gives.
    /// </summary>
    public const string InvalidArgumentsReason = "engage_requires_non_blank_target_skill_and_positive_rounds";

    /// <summary>How long the after-read waits for the skill's resource cost to land.</summary>
    /// <remarks>
    /// Deliberately not a hardcoded value inside <see cref="ExecuteOneRound"/>:
    /// the delay is injected there (so the round is unit-testable without real
    /// time passing, same reasoning as the walk rig's clock injection); this is
    /// only the live shell's own choice of what to inject.
    /// </remarks>
    private const int VerificationDelayMs = 350;

    /// <summary>
    /// Executes and verifies one <c>UseSkill</c> act. Every non-<c>UseSkill</c>
    /// kind is refused before any input is emitted -- see the class remarks.
    /// </summary>
    /// <remarks>
    /// Testable without a desktop: the keybind map, the input backend, the
    /// vitals reads and the verification delay are all injected. No real time
    /// passes unless the caller's <paramref name="verificationDelay"/> says so.
    /// </remarks>
    /// <param name="candidate">The act to execute and verify. Only <see cref="CombatActionKind.UseSkill"/> is supported.</param>
    /// <param name="keybinds">The operator's keybind map, resolved to the skill intent <c>skill.{id}</c>.</param>
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

        if (candidate.Kind != CombatActionKind.UseSkill)
            return CombatExecutionEvidence.NotAttempted(candidate, UnsupportedKindReason, nowUtc);

        string intent = $"{KeybindsCheck.SkillPrefix}{candidate.Skill!.Value.Value}";
        if (!keybinds.TryGet(intent, out Keybind bind) || !bind.Confirmed)
            return CombatExecutionEvidence.NotAttempted(candidate, KeybindNotConfirmedReason, nowUtc);

        PlayerVitalsReading? before = readVitals();
        bool accepted = input.KeyPress(bind.VirtualKey, KeyPressMs);
        if (!accepted)
            return CombatExecutionEvidence.NotAttempted(candidate, KeyPressNotAcceptedReason, nowUtc);

        verificationDelay();
        PlayerVitalsReading? after = readVitals();

        return NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector.Project(candidate, before, after, nowUtc);
    }

    /// <summary>
    /// Console entry for <c>--engage &lt;targetEntityId&gt; &lt;skillId&gt;</c>,
    /// optionally <c>--watch &lt;n&gt;</c> rounds (default 1).
    /// </summary>
    public static int Run(string targetEntityId, string skillId, int rounds = 1)
    {
        if (string.IsNullOrWhiteSpace(targetEntityId) || string.IsNullOrWhiteSpace(skillId) || rounds < 1)
        {
            Console.WriteLine($"[REFUSED] {InvalidArgumentsReason}");
            return WalkCommand.ExitAbandoned;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(targetEntityId, skillId, rounds);
    }

    /// <summary>
    /// The hard-constraint verdict on this round's target, as evidence of a
    /// refusal, or <see langword="null"/> when the act may proceed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Judged with <see cref="CombatPlanner.CheckTargetConstraints"/> rather
    /// than the full <see cref="CombatPlanner.CheckHardConstraints"/>, and that
    /// choice is not a shortcut: no observation channel in this project reads
    /// the character's skill list, so the full check's skill half would report
    /// <c>skill_list_not_observed</c> on every real client and refuse every act
    /// for a reason that is about that gap, not about the target. That method's own
    /// remarks carry the argument; what matters here is the direction --
    /// checking the target half narrows what is permitted, never widens it.
    /// </para>
    /// <para>
    /// A refusal is reported through
    /// <see cref="CombatExecutionEvidence.NotAttempted"/>, the same channel a
    /// guard refusal already uses, so the round's evidence line reads the same
    /// whether the act was refused before or during execution -- and the ledger
    /// records both.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static CombatExecutionEvidence? JudgeTarget(
        LiveCombatObserver observer,
        ClientMemorySession attached,
        CombatActionCandidate candidate,
        DateTime nowUtc)
    {
        if (!observer.TryObserve(attached, nowUtc, out Player player, out EquatableArray<Mob> mobs, out string? observeFailure))
        {
            return CombatExecutionEvidence.NotAttempted(
                candidate, $"{TargetNotVerifiableReason}:{observeFailure}", nowUtc);
        }

        CombatConstraintCheck verdict = CombatPlanner.CheckTargetConstraints(candidate, player, mobs);
        if (verdict.IsAllowed)
            return null;

        return CombatExecutionEvidence.NotAttempted(
            candidate,
            $"{TargetRefusedReason}:{string.Join('|', verdict.ViolatedConstraints)}",
            nowUtc);
    }

    /// <summary>
    /// The live composition, mirroring <c>ScoutCommand</c>/<c>PlayerVitalsProbe</c>'s
    /// shape: attach to the running client, load the operator's keybinds, take the
    /// gated input backend from <see cref="RuntimeComposition.CreateSafe"/>, then
    /// drive <see cref="ExecuteOneRound"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(string targetEntityId, string skillId, int rounds)
    {
        var candidate = new CombatActionCandidate(
            CombatActionKind.UseSkill,
            target: new EntityId(targetEntityId),
            skill: new SkillId(skillId));

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

            // The target check, opened once for the whole invocation. Unlike
            // the ledger above this one IS a gate: see TargetNotVerifiableReason
            // for why an unopenable feed refuses instead of warning.
            using LiveCombatObserver? observer =
                LiveCombatObserver.TryOpen(attached.ProcessId, out string? observerFailure, out string? catalogueWarning);
            if (observer is null)
            {
                Console.WriteLine($"[REFUSED] {TargetNotVerifiableReason}:{observerFailure}");
                return WalkCommand.ExitAbandoned;
            }

            // A missing catalogue establishes no vnum as a monster, so every
            // target would be refused as target_not_found. Saying so once, up
            // front, beats letting the operator read the same puzzling refusal
            // once per round.
            if (catalogueWarning is not null)
                Console.WriteLine($"[WARN] entity_catalogue_unavailable:{catalogueWarning} -- ogni bersaglio verra' rifiutato come target_not_found");

            for (int round = 1; round <= rounds; round++)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"=== engage round {round} of {rounds}: skill {skillId} on {targetEntityId} ==="));

                DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;

                // The verdict, re-taken every round: between two rounds a
                // target can die, walk out of range, or stop being the thing
                // the wire had established. A check taken once at the start
                // would authorise the later rounds on a picture that no longer
                // holds.
                CombatExecutionEvidence? refusal = JudgeTarget(observer, attached, candidate, nowUtc);
                if (refusal is { } refused)
                    Console.WriteLine($"[REFUSED] {refused.Detail}");

                // A refused round is still a round that happened, and its
                // evidence goes through the same printing and the same ledger
                // as an executed one: a target that was refused for being out
                // of range is exactly the history AP-09's learning stage needs,
                // and dropping it would leave the ledger claiming the operator
                // never tried.
                CombatExecutionEvidence evidence = refusal ?? ExecuteOneRound(
                    candidate,
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
                    context: $"skill:{skillId}",
                    recordedAtUtc: nowUtc);

                // The purpose of the invocation is a confirmed resource cost. A
                // round that aborted (unsupported kind, missing/unconfirmed
                // keybind, refused press) is deterministic -- retrying it would
                // repeat the same refusal, so stop. A round that ran but saw no
                // cost (e.g. a cooldown still in the way) is transient, which is
                // exactly what --watch is for: keep going to the next round.
                if (evidence.Result == CombatExecutionResult.ResourceCostConfirmed)
                    return 0;

                // A target refusal shares the Aborted result but not that
                // reasoning, and this is the one place the difference matters:
                // it is a statement about *this instant*, and the next round
                // re-takes it. A mob out of range walks back into it; a mob
                // whose hostility nothing had established becomes established
                // the moment it hits the character. Stopping here would make
                // --watch useless for exactly the cases it exists for.
                if (refusal is null && evidence.Result == CombatExecutionResult.Aborted)
                    return WalkCommand.ExitAbandoned;

                if (round < rounds)
                    Console.WriteLine();
            }

            // Every round ran and none confirmed a resource cost.
            return 1;
        }
    }

    /// <summary>One evidence line per round, in the same spirit as SingleStepCommand.Format.</summary>
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
            $"engage-evidence: {evidence.Result} resource={resource} before={before} after={after}{detail}"));
    }
}
