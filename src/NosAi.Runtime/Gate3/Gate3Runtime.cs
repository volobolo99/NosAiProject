using SafetyGate = NosAi.Runtime.Autonomy.SafetyGate;
using TrustTier = NosAi.Runtime.Autonomy.TrustTier;
using NosAi.Runtime.Autonomy;
// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Gate 3 — Closed-Loop Decision Pipeline
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate3
{


    /// <param name="Reason">
    /// Why the action ended in this state. Present for every state except
    /// <see cref="ExecutionState.Completed"/>, where there is nothing to explain.
    /// </param>
    public sealed record ExecutionResult(
        Guid CandidateId,
        ExecutionState State,
        int ActualDurationMs,
        string? Reason)
    {
        /// <summary>True only when the action really was applied to the world.</summary>
        public bool Completed => State == ExecutionState.Completed;

        /// <summary>True when nothing was attempted because policy forbids it.</summary>
        public bool SuppressedByPolicy => State == ExecutionState.Disabled;
    }

    /// <summary>What the verify step could establish about an executed action.</summary>
    public enum VerificationOutcome : byte
    {
        /// <summary>The observed world matches the prediction.</summary>
        Confirmed = 0,

        /// <summary>The world was observed and does not match the prediction.</summary>
        Discrepant = 1,

        /// <summary>
        /// Nothing could be observed, so the prediction is neither confirmed nor
        /// refuted. Never treated as success.
        /// </summary>
        Unverified = 2,

        /// <summary>The action did not complete, so there is nothing to verify.</summary>
        NotExecuted = 3
    }

    /// <param name="Source">
    /// Provenance of the comparison. <c>Live</c> only when a real observation was
    /// used; <c>Unknown</c> when the verifier had nothing to compare against.
    /// </param>
    public sealed record VerificationResult(
        Guid CandidateId,
        VerificationOutcome Outcome,
        float DiscrepancyScore,
        string AnalysisReport,
        DataSourceKind Source)
    {
        /// <summary>
        /// Confirmed and nothing else. An unverified cycle is not a successful one:
        /// treating "could not check" as "worked" is how a closed loop stops being closed.
        /// </summary>
        public bool IsConfirmed => Outcome == VerificationOutcome.Confirmed;

        /// <summary>Whether recovery should count this as a failure.</summary>
        public bool CountsAsFailure => Outcome is VerificationOutcome.Discrepant or VerificationOutcome.NotExecuted;
    }

    public sealed class SimulationEngine
    {
        public PredictedOutcome Simulate(ActionCandidate candidate, int currentHp, int currentMp, int maxHp)
        {
            int hpDelta = 0;
            int mpDelta = 0;
            int timeMs = 250;
            float successProb = 0.95f;
            float risk = 0.05f;

            switch (candidate.Type)
            {
                case ActionType.MoveToPosition:
                    timeMs = 400;
                    risk = currentHp < maxHp * 0.25 ? 0.40f : 0.05f;
                    break;

                case ActionType.UseBasicAttack:
                    timeMs = 600;
                    hpDelta = -15;
                    risk = currentHp < maxHp * 0.30 ? 0.65f : 0.15f;
                    break;

                case ActionType.UseSkill:
                    mpDelta = -35;
                    timeMs = 800;
                    risk = currentMp < 35 ? 0.90f : 0.10f;
                    successProb = currentMp >= 35 ? 0.98f : 0.0f;
                    break;

                case ActionType.UseConsumable:
                    hpDelta = 300;
                    mpDelta = 150;
                    timeMs = 150;
                    risk = 0.01f;
                    break;

                case ActionType.EmergencyFlee:
                    timeMs = 500;
                    risk = 0.10f;
                    break;

                // Selecting costs a click and commits to nothing: the swing that
                // follows is a separate candidate the planner has to justify
                // separately. What can go wrong is aiming at a square the monster
                // has already left, which selects nothing and is caught at
                // verification rather than paid for in health.
                case ActionType.TargetEntity:
                    timeMs = 200;
                    risk = 0.05f;
                    successProb = 0.90f;
                    break;
            }

            string signature = $"POST_HP_{Math.Clamp(currentHp + hpDelta, 0, maxHp)}_MP_{Math.Max(0, currentMp + mpDelta)}";

            return new PredictedOutcome(
                candidate.CandidateId,
                hpDelta,
                mpDelta,
                timeMs,
                successProb,
                risk,
                signature);
        }
    }

    public sealed class TacticalRankingEngine
    {
        public IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> RankCandidates(
            IReadOnlyList<ActionCandidate> candidates,
            IReadOnlyDictionary<Guid, PredictedOutcome> predictions,
            int playerHp,
            int maxHp)
        {
            var ranked = new List<(ActionCandidate, float)>();
            double hpPercent = (double)playerHp / Math.Max(1, maxHp);

            foreach (var candidate in candidates)
            {
                if (!predictions.TryGetValue(candidate.CandidateId, out var outcome))
                    continue;

                float utility = 0.0f;

                if (hpPercent < 0.30)
                {
                    if (candidate.Type is ActionType.UseConsumable or ActionType.EmergencyFlee)
                        utility += 0.85f;
                    // Picking a new fight at low health is the same mistake as
                    // swinging at one, and costs the same.
                    else if (candidate.Type is ActionType.UseBasicAttack or ActionType.TargetEntity)
                        utility -= 0.50f;
                }
                else
                {
                    if (candidate.Type == ActionType.UseSkill)
                        utility += 0.70f;
                    else if (candidate.Type == ActionType.UseBasicAttack)
                        utility += 0.55f;
                    // Above the exploration move on purpose: with a monster
                    // observed nearby, walking to a fixed waypoint instead of
                    // aiming at it is the behaviour that made the loop wander
                    // past everything it could see.
                    else if (candidate.Type == ActionType.TargetEntity)
                        utility += 0.50f;
                    else if (candidate.Type == ActionType.MoveToPosition)
                        utility += 0.40f;
                }

                utility += outcome.SuccessProbability * 0.30f - outcome.RiskScore * 0.40f;
                ranked.Add((candidate, MathF.Max(0.0f, utility)));
            }

            return ranked.OrderByDescending(x => x.Item2).ToList();
        }
    }

    /// <summary>
    /// Turns an observed state into the actions worth considering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each rule reads its own facts and is skipped when one of them is unknown
    /// (ADR-0016). The planner used to take five plain values, which forced the
    /// caller to have all five — so the loop refused to plan at all whenever the
    /// wire had not established the targeting state, even with HP critical and
    /// fully observed.
    /// </para>
    /// <para>
    /// The rule that matters most is the one about the unknown target. Planning the
    /// exploration move on an unknown <c>HasTarget</c> would mean walking to a
    /// waypoint because nobody knows whether something is being fought; planning
    /// the attack would mean swinging at a target nobody has seen. Neither branch
    /// is a safe default, so an unknown fact selects neither.
    /// </para>
    /// </remarks>
    public sealed class ActionPlanner
    {
        /// <summary>Plans from a classified state, skipping the rules it cannot support.</summary>
        public List<ActionCandidate> PlanCandidates(Gate3WorldState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (!state.HasVitals)
                return new List<ActionCandidate>();

            return Plan(
                state.Hp.Value,
                state.MaxHp.Value,
                state.Mp.Value,
                state.HasTarget.HasValue ? state.HasTarget.Value : null,
                state);
        }

        /// <summary>
        /// Plans from plain values, for dry runs and the certification runners.
        /// </summary>
        /// <remarks>
        /// <paramref name="isInCombat"/> is accepted and not read, as it always was:
        /// no rule here consults it. It stays in the signature because the runners
        /// pass it, and it is named in ADR-0016 as the field that was blocking the
        /// loop while changing no decision.
        /// </remarks>
        public List<ActionCandidate> PlanCandidates(
            int playerHp,
            int maxHp,
            int playerMp,
            bool hasTarget,
            bool isInCombat)
            => Plan(playerHp, maxHp, playerMp, hasTarget);

        /// <summary>
        /// The quickbar slot the HP potion sits in, as far as the planner knows.
        /// </summary>
        /// <remarks>
        /// A number the planner asserts and nobody has confirmed, exactly as the
        /// waypoint below is. It reaches no key on its own: the effector looks the
        /// slot up in the operator's keybinds and refuses by name when it is not
        /// configured, so an unconfirmed slot costs a named refusal rather than a
        /// keypress during a fight.
        /// </remarks>
        private const int HpPotionSlot = 1;

        /// <summary>
        /// Where exploration heads when there is no target.
        /// </summary>
        /// <remarks>
        /// Still a constant nobody observed — the same <c>130, 90</c> the old
        /// <c>"WAYPOINT_A"</c> carried. It is a real map point in the type system
        /// now, which is not the same as a real map point: turning it into one is
        /// navigation's job, not this card's. Until the screen projection is
        /// calibrated (F2-3) the effector refuses to click it at all.
        /// </remarks>
        private static readonly MapPoint ExplorationWaypoint = new(130, 90);

        /// <summary>
        /// How the runtime chooses which entity to aim at when it has none.
        /// </summary>
        /// <remarks>
        /// A constant nobody has tuned against a real fight, and it is one place
        /// rather than a number spread through the rules. Twelve tiles is not a
        /// claim about attack range: past roughly that the entity is not drawn, the
        /// projection puts it outside the client area, and the click is refused.
        /// </remarks>
        private static readonly TargetSelectionPolicy Targeting = TargetSelectionPolicy.Default;

        /// <param name="hasTarget">Null when nobody has established it.</param>
        /// <param name="state">
        /// The full state, for the rules that read more than the four vitals. Null
        /// from the plain-value overload, which the dry runs and certification
        /// runners use and which has no entities to offer — so the rule that needs
        /// them is skipped rather than fed something invented.
        /// </param>
        private static List<ActionCandidate> Plan(
            int playerHp,
            int maxHp,
            int playerMp,
            bool? hasTarget,
            Gate3WorldState? state = null)
        {
            var list = new List<ActionCandidate>();

            // Survival reads the vitals and nothing else, which is why it is the
            // one thing this runtime can decide on the network channel alone.
            if (playerHp < maxHp * 0.35)
            {
                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.UseConsumable,
                    new ActionTarget.InventorySlot(HpPotionSlot),
                    101,
                    TrustTier.Tier1_Assisted,
                    "HP critico: uso pozione di recupero"));

                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.EmergencyFlee,
                    new ActionTarget.Position(new MapPoint(100, 80)),
                    0,
                    TrustTier.Tier1_Assisted,
                    "HP critico: riposizionamento difensivo"));
            }

            if (hasTarget == true)
            {
                if (playerMp >= 35)
                {
                    list.Add(new ActionCandidate(
                        Guid.NewGuid(),
                        ActionType.UseSkill,
                        ActionTarget.Entity.Unidentified,
                        201,
                        TrustTier.Tier2_SemiAutonomous,
                        "Bersaglio attivo: skill ad alto impatto"));
                }

                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.UseBasicAttack,
                    ActionTarget.Entity.Unidentified,
                    0,
                    TrustTier.Tier2_SemiAutonomous,
                    "Bersaglio attivo: attacco base"));
            }
            else if (hasTarget == false)
            {
                // Nothing is being fought, so the first question is whether there
                // is anything to fight. Until this rule existed the answer was
                // always no: every entity candidate carried Entity.Unidentified
                // and the effector refused it, so the loop could only attack a
                // target somebody else had selected.
                if (TrySelectTarget(state) is { } chosen)
                {
                    list.Add(new ActionCandidate(
                        Guid.NewGuid(),
                        ActionType.TargetEntity,
                        new ActionTarget.Entity(chosen.Entity.EntityId, chosen.Entity.At),
                        0,
                        TrustTier.Tier2_SemiAutonomous,
                        chosen.Rationale));
                }

                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.MoveToPosition,
                    new ActionTarget.Position(ExplorationWaypoint),
                    0,
                    TrustTier.Tier1_Assisted,
                    "Esplorazione verso waypoint"));
            }
            // hasTarget unknown: neither branch. Not knowing whether there is a
            // target is not the same as knowing there is none, and this is the
            // exact point where treating it as the same would send the character
            // walking away from a fight.

            return list;
        }

        /// <summary>
        /// The entity worth aiming at, or null when there is none to aim at.
        /// </summary>
        /// <remarks>
        /// Null covers a genuinely empty map, a state that carries no entities at
        /// all, and an unknown position — none of which is a reason to plan an
        /// attack on something unnamed. <see cref="TargetSelector"/> distinguishes
        /// them by name; the planner only needs to know whether it has a target,
        /// and the distinction belongs in the loop's diagnostics rather than in a
        /// candidate.
        /// </remarks>
        private static TargetChoice? TrySelectTarget(Gate3WorldState? state)
        {
            if (state?.Entities is not { } entities || state.PlayerPosition is not { } position)
                return null;

            return TargetSelector.TrySelect(
                entities, position, DateTime.UtcNow, Targeting, out TargetChoice? choice, out _)
                ? choice
                : null;
        }
    }


    /// <summary>
    /// Runs an authorised action through an <see cref="IActionEffector"/>.
    /// </summary>
    /// <remarks>
    /// The executor owns authorisation, not effect. It checks the token signature,
    /// binding and single use, and only then hands the action to the effector. It
    /// never decides that an action succeeded: that answer comes from whatever
    /// actually touched the world, and when nothing did, the result says so.
    /// </remarks>
    public sealed class AuthorizedActionExecutor
    {
        private readonly SafetyGate _safetyGate;
        private readonly IActionEffector _effector;

        /// <param name="effector">
        /// Defaults to <see cref="DisabledActionEffector"/>, matching a safety policy
        /// with live input off. Passing nothing yields a pipeline that refuses to
        /// act, never one that pretends to.
        /// </param>
        public AuthorizedActionExecutor(SafetyGate safetyGate, IActionEffector? effector = null)
        {
            _safetyGate = safetyGate;
            _effector = effector ?? new DisabledActionEffector();
        }

        public IActionEffector Effector => _effector;

        public async Task<ExecutionResult> ExecuteAuthorizedAsync(
            ActionCandidate candidate,
            SafetyToken token,
            CancellationToken cancellationToken = default)
        {
            if (!_safetyGate.ValidateToken(token))
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    ExecutionState.Refused,
                    0,
                    "safety_token_invalid_or_forged");
            }

            // Binding is checked before consumption: a token for another candidate
            // must not be burned by the attempt to misuse it.
            if (token.CandidateId != candidate.CandidateId)
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    ExecutionState.Refused,
                    0,
                    "safety_token_bound_to_another_candidate");
            }

            if (!token.TryConsume())
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    ExecutionState.Refused,
                    0,
                    "safety_token_already_consumed_or_expired");
            }

            var sw = Stopwatch.StartNew();
            try
            {
                ExecutionResult result = await _effector
                    .ApplyAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);

                return result with { ActualDurationMs = (int)sw.ElapsedMilliseconds };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    ExecutionState.Failed,
                    (int)sw.ElapsedMilliseconds,
                    $"effector_failed:{ex.GetType().Name}");
            }
            finally
            {
                sw.Stop();
            }
        }
    }

    /// <summary>
    /// Compares a prediction against the world as it was actually read back.
    /// </summary>
    /// <remarks>
    /// The comparison must be against an observation. The previous implementation
    /// was handed a post-state computed from the prediction's own deltas, so it
    /// compared the prediction to itself and confirmed every cycle — the verify
    /// step of the closed loop could not fail. Where there is no observation there
    /// is now no confirmation: the result is <see cref="VerificationOutcome.Unverified"/>,
    /// classified UNKNOWN.
    /// </remarks>
    public sealed class ActionExecutionVerifier
    {
        /// <param name="nowUtc">
        /// When the check is being made, for VER-04's freshness bound. The system
        /// clock when omitted, so a caller with no clock of its own is unchanged.
        /// </param>
        /// <param name="maxAge">
        /// The freshness bound. <see cref="Gate3ExecutionOrchestrator.DefaultMaxObservationAge"/>
        /// when omitted.
        /// </param>
        public VerificationResult Verify(
            ActionCandidate candidate,
            PredictedOutcome predicted,
            ExecutionResult execution,
            ObservedState observed,
            DateTime? nowUtc = null,
            TimeSpan? maxAge = null)
        {
            if (execution.SuppressedByPolicy)
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.NotExecuted,
                    0.0f,
                    $"Nessuna esecuzione: {execution.Reason ?? "inibita da policy"}. Nulla da verificare.",
                    DataSourceKind.Unknown);
            }

            if (!execution.Completed)
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.NotExecuted,
                    1.0f,
                    $"Esecuzione non completata: {execution.Reason ?? "motivo sconosciuto"}.",
                    DataSourceKind.Unknown);
            }

            // VER-04: the verification tier is not stricter than the actuation
            // tier. This used to demand both readings LIVE while ADR-0016 § 2
            // already let the runtime act on a fresh DERIVED or CACHED one, so
            // every screen-driven cycle ended Unverified by rule rather than by
            // observation. The bound is the caller's, for the same reason the
            // orchestrator owns the freshness policy.
            if (!observed.IsUsableForVerification(nowUtc ?? DateTime.UtcNow, maxAge ?? Gate3ExecutionOrchestrator.DefaultMaxObservationAge))
            {
                string reason = observed.Hp.FailureReason ?? observed.Mp.FailureReason ?? "stato non osservato";
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.Unverified,
                    0.0f,
                    $"Azione eseguita ma non verificabile: {reason}. La previsione non è né confermata né smentita.",
                    DataSourceKind.Unknown);
            }

            string observedSignature = $"POST_HP_{observed.Hp.Value}_MP_{observed.Mp.Value}";
            bool matches = predicted.StateSignatureAfter == observedSignature;

            return new VerificationResult(
                candidate.CandidateId,
                matches ? VerificationOutcome.Confirmed : VerificationOutcome.Discrepant,
                matches ? 0.0f : 0.45f,
                matches
                    ? $"Verifica confermata su stato osservato: {observedSignature}."
                    : $"Discrepanza: atteso {predicted.StateSignatureAfter}, osservato {observedSignature}.",
                DataSourceKind.Live);
        }

        /// <summary>
        /// Verifies an executed action against its own post-condition.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The path docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md defines, and the one
        /// the orchestrator takes. The prediction is not a parameter: VER-01 says
        /// the predicate is over observations and never over the prediction, and
        /// the surest way to keep that is to make the prediction unreachable from
        /// here.
        /// </para>
        /// <para>
        /// An action with no card is not verified leniently — it is not executed.
        /// Reaching this method without one is a wiring fault, and it reports as
        /// <see cref="VerificationOutcome.Unverified"/> naming the missing card
        /// rather than confirming anything.
        /// </para>
        /// </remarks>
        public VerificationResult VerifyPostCondition(
            ExecutionResult execution,
            in PostConditionInput input,
            PostConditionTable? table = null)
        {
            ActionCandidate candidate = input.Candidate;

            if (execution.SuppressedByPolicy)
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.NotExecuted,
                    0.0f,
                    $"Nessuna esecuzione: {execution.Reason ?? "inibita da policy"}. Nulla da verificare.",
                    DataSourceKind.Unknown);
            }

            if (!execution.Completed)
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.NotExecuted,
                    1.0f,
                    $"Esecuzione non completata: {execution.Reason ?? "motivo sconosciuto"}.",
                    DataSourceKind.Unknown);
            }

            PostConditionTable catalogue = table ?? PostConditionTable.Catalogue;
            if (!catalogue.TryGet(candidate.Type, out IPostCondition postCondition))
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    VerificationOutcome.Unverified,
                    0.0f,
                    $"Nessuna post-condizione in tabella per {candidate.Type}: "
                    + $"{PostConditionTable.RefusalReason(candidate.Type)}.",
                    DataSourceKind.Unknown);
            }

            PostConditionVerdict verdict = postCondition.Evaluate(input);

            return new VerificationResult(
                candidate.CandidateId,
                verdict.Outcome,
                verdict.Divergence,
                Describe(candidate, postCondition, verdict),
                verdict.Outcome is VerificationOutcome.Confirmed or VerificationOutcome.Discrepant
                    ? WeakestObservedSource(input)
                    : DataSourceKind.Unknown);
        }

        private static string Describe(
            ActionCandidate candidate, IPostCondition postCondition, PostConditionVerdict verdict)
        {
            string window = string.Create(
                CultureInfo.InvariantCulture,
                $"{postCondition.Window.TotalMilliseconds:F0}ms{(postCondition.WindowIsMeasured ? "" : " dichiarata")}");

            return verdict.Outcome switch
            {
                VerificationOutcome.Confirmed => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Post-condizione di {candidate.Type} soddisfatta ({verdict.Reason}), d={verdict.Divergence:F2}, finestra {window}."),
                VerificationOutcome.Discrepant => string.Create(
                    CultureInfo.InvariantCulture,
                    $"Post-condizione di {candidate.Type} contraddetta ({verdict.Reason}), d={verdict.Divergence:F2}, finestra {window}."),
                _ => $"Post-condizione di {candidate.Type} non verificabile: {verdict.Reason}. "
                     + "Non è né riuscita né fallita (VER-05).",
            };
        }

        /// <summary>
        /// The provenance of the readings the verdict rests on: the weakest of
        /// them, so a series holding one CACHED element is not reported LIVE.
        /// </summary>
        private static DataSourceKind WeakestObservedSource(in PostConditionInput input)
        {
            static int Rank(DataSourceKind kind) => kind switch
            {
                DataSourceKind.Live => 4,
                DataSourceKind.Derived => 3,
                DataSourceKind.Cached => 2,
                DataSourceKind.Simulated => 1,
                _ => 0,
            };

            DataSourceKind weakest = DataSourceKind.Unknown;
            var seen = false;
            foreach (Gate3WorldState state in input.AfterDispatch)
            {
                foreach (ClassifiedValue<int> vital in new[] { state.Hp, state.MaxHp, state.Mp })
                {
                    if (!vital.HasValue) continue;
                    if (!seen || Rank(vital.Source) < Rank(weakest)) weakest = vital.Source;
                    seen = true;
                }
            }

            return seen ? weakest : DataSourceKind.Unknown;
        }
    }


    /// <summary>How a full Observe -> Plan -> Guard -> Execute -> Verify cycle ended.</summary>
    public enum CycleOutcome : byte
    {
        /// <summary>Executed and confirmed against an observation.</summary>
        Confirmed = 0,

        /// <summary>Nothing was planned, or nothing survived ranking.</summary>
        NoCandidate = 1,

        /// <summary>
        /// The world state could not be read, so there was nothing to plan from.
        /// Planning over UNKNOWN would mean inventing the inputs.
        /// </summary>
        NoWorldState = 6,

        /// <summary>
        /// The plan was built on simulated state while a live effector was bound.
        /// Refused: a dry run must never reach the real client.
        /// </summary>
        RefusedSimulatedInput = 7,

        /// <summary>The Safety Gate refused authorisation.</summary>
        Blocked = 2,

        /// <summary>Policy forbids live input, so nothing was attempted.</summary>
        ExecutionDisabled = 3,

        /// <summary>Executed, but nothing could be observed to confirm it.</summary>
        Unverified = 4,

        /// <summary>Executed and the world does not match the prediction, or execution failed.</summary>
        Failed = 5,

        /// <summary>
        /// The state was really observed and is no longer recent enough to act on,
        /// while a live effector was bound.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="RefusedSimulatedInput"/> on purpose. Merging
        /// them was the old behaviour and it told the operator that a reading taken
        /// a second and a half ago was a simulation — false, and it hid the age,
        /// which is the only number that makes the refusal diagnosable (ADR-0016).
        /// </remarks>
        RefusedStaleInput = 8
    }

    /// <param name="Strategy">Recovery decision, when one was taken.</param>
    public sealed record Gate3CycleResult(
        CycleOutcome Outcome,
        string Summary,
        ActionType SelectedAction,
        RuntimeMode ModeAfter,
        TrustTier TrustAfter,
        RecoveryStrategy? Strategy)
    {
        /// <summary>Confirmed and nothing else.</summary>
        /// <remarks>
        /// Deliberately narrow. A disabled or unverified cycle is not a success, and
        /// a caller that treats "did not fail" as "worked" is the reason the previous
        /// pipeline reported healthy cycles while touching nothing.
        /// </remarks>
        public bool IsConfirmed => Outcome == CycleOutcome.Confirmed;
    }

    /// <summary>
    /// The Gate 3 closed loop: plan, simulate, rank, guard, authorise, execute, verify.
    /// </summary>
    /// <remarks>
    /// The canonical order is not negotiable and every step is fail-closed. What
    /// this orchestrator will not do is fill a gap with an assumption: an action
    /// that policy forbids is reported as not executed, and one that cannot be
    /// observed afterwards is reported as unverified.
    /// </remarks>
    public sealed class Gate3ExecutionOrchestrator
    {
        private readonly ActionPlanner _planner;
        private readonly SimulationEngine _simulation;
        private readonly TacticalRankingEngine _ranking;
        private readonly GuardPolicyEngine _guard;
        private readonly TrustBoundary _trust;
        private readonly SafetyGate _safetyGate;
        private readonly AuthorizedActionExecutor _executor;
        private readonly ActionExecutionVerifier _verifier;
        private readonly RecoveryController _recovery;
        private readonly PipelineStageBoard _stageBoard;
        private readonly IWorldStateObserver _observer;
        private readonly TimeProvider _clock;
        private readonly Action? _ensureSessionVerified;
        private readonly PostConditionTable _postConditions;
        private readonly Func<CancellationToken, Task<Gate3WorldState>>? _worldSampler;

        public RuntimeMode CurrentMode { get; private set; } = RuntimeMode.Normal;
        public TrustBoundary Trust => _trust;
        public RuntimeSafetyPolicy Policy { get; }

        /// <summary>The breaker this cycle reports outcomes to.</summary>
        public RecoveryController Recovery => _recovery;

        /// <summary>Last outcome per canonical pipeline stage, for a halt dump.</summary>
        public PipelineStageBoard StageBoard => _stageBoard;

        /// <summary>Whether anything is bound that can actually act on the world.</summary>
        public bool CanExecute => _executor.Effector.CanApply;

        /// <summary>Whether anything is bound that can read the world back.</summary>
        public bool CanVerify => _observer.CanObserve;

        /// <summary>The post-conditions this loop admits actions against.</summary>
        /// <remarks>
        /// An action with no card here is refused at admission. That is the
        /// property docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md § 7 asks to be fixed
        /// by test: nothing executes that nothing can check.
        /// </remarks>
        public PostConditionTable PostConditions => _postConditions;

        /// <summary>
        /// How old an observation may be and still drive a real action.
        /// </summary>
        /// <remarks>
        /// Deliberately stricter than the gameplay provider's own retention, which
        /// republishes a reading as CACHED for several seconds. That leaves a band
        /// where the runtime will reason about a state and refuse to act on it —
        /// the distinction the previous all-LIVE rule could not express, since it
        /// called every non-live reading simulated (ADR-0016).
        /// </remarks>
        public static readonly TimeSpan DefaultMaxObservationAge = TimeSpan.FromSeconds(2);

        /// <summary>The freshness bound this loop acts within.</summary>
        public TimeSpan MaxObservationAge { get; }

        /// <param name="policy">
        /// Defaults to <see cref="RuntimeSafetyPolicy.SafeDefault"/>, which keeps live
        /// input and packet injection off.
        /// </param>
        /// <param name="effector">Bound only when the policy permits acting.</param>
        /// <param name="observer">
        /// Without one, every executed cycle ends unverified. That is the honest
        /// result, not a degraded mode to be papered over.
        /// </param>
        /// <param name="maxObservationAge">
        /// How old a reading may be and still reach the effector.
        /// <see cref="DefaultMaxObservationAge"/> when omitted.
        /// </param>
        /// <param name="clock">Time source; the system clock unless a test supplies one.</param>
        /// <param name="policySource">
        /// The live policy, read on every action instead of once here. The host
        /// builds this orchestrator while every switch is still off, so an
        /// effector chosen from <paramref name="policy"/> alone would stay
        /// disabled for the life of the process and the operator's switch would
        /// do nothing. Supplying this is what makes arming — and disarming —
        /// take effect on the next action.
        /// </param>
        /// <param name="ensureSessionVerified">
        /// Called at the start of every cycle that can produce an act, before the
        /// effector is asked whether actuation is on offer and before a plan is
        /// composed. The production host binds
        /// <c>SessionActuationAuthority.EnsureVerified</c>. Null leaves the cycle
        /// answering on the standing verdict, which is what the certification
        /// suites want against a recording backend with no session.
        /// </param>
        public Gate3ExecutionOrchestrator(
            RuntimeSafetyPolicy? policy = null,
            IActionEffector? effector = null,
            IWorldStateObserver? observer = null,
            TrustTier initialTrust = TrustTier.Tier2_SemiAutonomous,
            TimeSpan? maxObservationAge = null,
            TimeProvider? clock = null,
            Func<RuntimeSafetyPolicy>? policySource = null,
            Action? ensureSessionVerified = null,
            RecoveryController? recovery = null,
            PipelineStageBoard? stageBoard = null,
            PostConditionTable? postConditions = null,
            Func<CancellationToken, Task<Gate3WorldState>>? worldSampler = null)
        {
            Policy = policy ?? policySource?.Invoke() ?? RuntimeSafetyPolicy.SafeDefault;
            TimeSpan maxAge = maxObservationAge ?? DefaultMaxObservationAge;
            if (maxAge < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxObservationAge));
            MaxObservationAge = maxAge;
            _clock = clock ?? TimeProvider.System;
            _planner = new ActionPlanner();
            _simulation = new SimulationEngine();
            _ranking = new TacticalRankingEngine();
            _guard = new GuardPolicyEngine();
            _trust = new TrustBoundary(initialTrust);
            _safetyGate = new SafetyGate(_trust, _guard);
            _executor = new AuthorizedActionExecutor(_safetyGate, policySource is null
                ? ActionEffectorFactory.ForPolicy(Policy, effector)
                : ActionEffectorFactory.ForPolicy(policySource, effector));
            _verifier = new ActionExecutionVerifier();
            _recovery = recovery ?? new RecoveryController(_trust);
            _stageBoard = stageBoard ?? new PipelineStageBoard();
            _observer = observer ?? new UnavailableWorldStateObserver();
            _ensureSessionVerified = ensureSessionVerified;
            _postConditions = postConditions ?? PostConditionTable.Catalogue;
            _worldSampler = worldSampler;
        }

        /// <summary>
        /// Runs one cycle over explicitly hypothetical state.
        /// </summary>
        /// <remarks>
        /// Kept for dry runs and tests. The numbers are labelled SIMULATED, so a plan
        /// built from them cannot be mistaken for one built from the game, and the
        /// orchestrator will refuse to carry it through to a live effector.
        /// </remarks>
        public Task<Gate3CycleResult> ExecuteCycleAsync(
            int playerHp,
            int maxHp,
            int playerMp,
            bool hasTarget,
            bool isInCombat,
            CancellationToken token = default)
            => ExecuteCycleAsync(
                Gate3WorldState.Simulated(playerHp, maxHp, playerMp, hasTarget, isInCombat),
                token);

        /// <summary>Runs one cycle over a classified world state.</summary>
        public async Task<Gate3CycleResult> ExecuteCycleAsync(
            Gate3WorldState state,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(state);

            // Nothing to reason about. Planning here would mean inventing the inputs,
            // which is the input-side twin of confirming an unobserved outcome. The
            // bar is the vitals: every other fact gates only the rules that read it,
            // and a rule whose facts are unknown is skipped rather than defaulted
            // (ADR-0016).
            if (!state.IsPlannable)
            {
                _stageBoard.Record("Observe", false, state.UnusableReason ?? "world_state_unavailable");
                return Result(
                    CycleOutcome.NoWorldState,
                    $"Stato del mondo non disponibile: {state.UnusableReason}. Nessuna pianificazione possibile.",
                    ActionType.None,
                    null);
            }

            _stageBoard.Record("Observe", true);
            _stageBoard.Record("WorldState", true);

            // X-P3. Refresh the session verdict before the effector is asked
            // whether actuation is on offer (CanExecute) and before a plan is
            // composed. EnsureVerified is a no-op on a standing verified path.
            // It is not inside the effector: asking whether the capability exists
            // must remain a pure read.
            _ensureSessionVerified?.Invoke();

            DateTime now = _clock.GetUtcNow().UtcDateTime;

            // Planning on hypothetical numbers is legitimate; acting on them is not.
            if (CanExecute && state.IsSimulated)
            {
                return Result(
                    CycleOutcome.RefusedSimulatedInput,
                    "Stato simulato con effector reale collegato: esecuzione rifiutata. "
                    + "Si può pianificare su dati simulati, non agire.",
                    ActionType.None,
                    null);
            }

            // A real reading that is no longer recent is a different failure, and
            // saying "simulated" about it — as this check used to — is false and
            // hides the only number that would let anyone diagnose it.
            if (CanExecute && !state.IsActionable(now, MaxObservationAge))
            {
                string age = state.AgeAt(now) is { } elapsed
                    ? $"{elapsed.TotalSeconds:F1}s"
                    : "sconosciuta";
                return Result(
                    CycleOutcome.RefusedStaleInput,
                    $"Osservazione troppo vecchia per agire: età {age}, limite "
                    + $"{MaxObservationAge.TotalSeconds:F1}s. Pianificazione possibile, esecuzione rifiutata.",
                    ActionType.None,
                    null);
            }

            int playerHp = state.Hp.Value;
            int maxHp = state.MaxHp.Value;
            int playerMp = state.Mp.Value;

            List<ActionCandidate> candidates = _planner.PlanCandidates(state);

            if (candidates.Count == 0)
            {
                _stageBoard.Record("Planner", false, "no_candidate");
                return Result(CycleOutcome.NoCandidate, "Nessun candidato d'azione pianificato.", ActionType.None, null);
            }

            _stageBoard.Record("Planner", true);

            var predictions = new Dictionary<Guid, PredictedOutcome>(candidates.Count);
            foreach (ActionCandidate candidate in candidates)
                predictions[candidate.CandidateId] = _simulation.Simulate(candidate, playerHp, playerMp, maxHp);
            _stageBoard.Record("Simulation", true);

            IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> ranked =
                _ranking.RankCandidates(candidates, predictions, playerHp, maxHp);

            if (ranked.Count == 0)
            {
                _stageBoard.Record("Ranking", false, "no_ranked_candidate");
                return Result(CycleOutcome.NoCandidate, "Nessun candidato idoneo dopo il ranking tattico.", ActionType.None, null);
            }

            _stageBoard.Record("Ranking", true);

            (ActionCandidate best, float utility) = ranked[0];
            PredictedOutcome predicted = predictions[best.CandidateId];

            // Nothing executes that nothing can check. An action with no card in
            // the post-condition table is refused here, by name, rather than
            // executed and then found unverifiable — which is the difference
            // between a declared gap and a silent one
            // (docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md § 7).
            if (!_postConditions.IsAdmissible(best.Type))
            {
                string refusal = PostConditionTable.RefusalReason(best.Type);
                _stageBoard.Record("Safety", false, refusal);
                return Result(
                    CycleOutcome.Blocked,
                    $"Azione non ammissibile: {refusal}. "
                    + "Nessuna post-condizione dichiarata, quindi nessuna esecuzione.",
                    best.Type,
                    null);
            }

            // Admission control before authorisation, because a breaker that only
            // labels the runtime does not slow it down: the previous version
            // escalated to Degraded and then went on acting at the same rate, so the
            // next failure was never far away. A refusal here is the breaker working
            // and is reported as a block, never fed back as a failed action.
            RuntimeMode admissionMode = CurrentMode;
            if (!_recovery.TryBeginAction(ref admissionMode, out string? recoveryRefusal))
            {
                CurrentMode = admissionMode;
                return Result(
                    CycleOutcome.Blocked,
                    $"Blocco recovery: {recoveryRefusal}. Stato breaker: {_recovery.State}.",
                    best.Type,
                    null);
            }

            CurrentMode = admissionMode;

            if (!_safetyGate.TryAuthorize(best, predicted, CurrentMode, out SafetyToken? safetyToken, out string? rejection))
            {
                _stageBoard.Record("Safety", false, rejection);
                _stageBoard.Record("Guard", false, rejection);
                return Result(CycleOutcome.Blocked, $"Blocco Safety Gate: {rejection}", best.Type, null);
            }

            _stageBoard.Record("Safety", true);
            _stageBoard.Record("Guard", true);

            // Taken before the act, because VER-03 measures every confirming
            // observation from the instant the act left the runtime. Stamping it
            // afterwards would silently admit a reading taken while the act was in
            // flight, which describes a world the action had not finished touching.
            DateTime dispatchedAt = _clock.GetUtcNow().UtcDateTime;

            ExecutionResult execution = await _executor
                .ExecuteAuthorizedAsync(best, safetyToken!, token)
                .ConfigureAwait(false);

            // Nothing was attempted, so there is nothing to recover from and nothing
            // to verify. Reporting it as failure would drive the recovery controller
            // to degrade trust over a configuration that is working as intended.
            if (execution.SuppressedByPolicy)
            {
                return Result(
                    CycleOutcome.ExecutionDisabled,
                    $"Azione autorizzata ma non eseguita: {execution.Reason}. Ciclo completo fino al gate, esecuzione inibita.",
                    best.Type,
                    null);
            }

            // The world is read back, never derived from the prediction being
            // checked, and it is read as a series rather than as one endpoint:
            // stat and st are sent when a number changes, not on a schedule, so
            // two endpoints compare two moments the traffic chose (VER-09).
            IReadOnlyList<Gate3WorldState> series =
                await ReadBackAsync(state, token).ConfigureAwait(false);

            VerificationResult verification = _verifier.VerifyPostCondition(
                execution,
                new PostConditionInput(
                    best,
                    dispatchedAt,
                    series,
                    CollectSightings(series),
                    Deaths: null),
                _postConditions);

            if (verification.IsConfirmed)
            {
                // The orchestrator used to reset the failure count and assign Normal
                // itself, which put the decision to resume full speed in the one
                // place that cannot see the history it depends on. It reports the
                // outcome now; the controller decides what the outcome earns.
                RuntimeMode confirmedMode = CurrentMode;
                RecoveryState breakerState = _recovery.HandleSuccess(ref confirmedMode);
                CurrentMode = confirmedMode;

                string trial = breakerState == RecoveryState.Probing
                    ? $" In prova: {_recovery.ProbeSuccessesToClose} successi consecutivi per rientrare."
                    : string.Empty;

                return Result(
                    CycleOutcome.Confirmed,
                    $"Ciclo confermato: {best.Type} (utility {utility:F2}). {verification.AnalysisReport}{trial}",
                    best.Type,
                    null);
            }

            if (verification.Outcome == VerificationOutcome.Unverified)
            {
                // Executed but unconfirmed. Not counted as a failure -- the action may
                // well have worked -- but never reported as success, and the failure
                // counter is left untouched rather than reset. § 5 gives it Replan and
                // never Continue: nothing was established, so the next step is to
                // plan again rather than to carry on as though it had been.
                RecoveryStrategy unverifiedNext = Escalate(best.Type, RecoveryStrategy.Replan);

                if (unverifiedNext == RecoveryStrategy.HaltAndAlert)
                {
                    // § 4.8: a flight nobody could verify is not repeated. The case
                    // where the check fails is by construction the case where the
                    // situation is worse, and a loop of unverified flights is what
                    // the breaker exists to stop.
                    RuntimeMode haltedMode = CurrentMode;
                    _recovery.HandleFailure(ref haltedMode);
                    CurrentMode = haltedMode;
                    return Result(
                        CycleOutcome.Failed,
                        $"{verification.AnalysisReport} Fuga non verificata: nessuna ripetizione, "
                        + "arresto e allarme all'operatore.",
                        best.Type,
                        RecoveryStrategy.HaltAndAlert);
                }

                return Result(CycleOutcome.Unverified, verification.AnalysisReport, best.Type, unverifiedNext);
            }

            RuntimeMode recoveredMode = CurrentMode;
            RecoveryStrategy fromBreaker = _recovery.HandleFailure(ref recoveredMode);
            CurrentMode = recoveredMode;

            // The breaker decides from its own history; § 5 sets a floor from the
            // measured divergence. The severer of the two wins, so a single badly
            // divergent cycle is not softened by a clean history and a long run of
            // small failures is not softened by a small divergence.
            RecoveryStrategy strategy = Escalate(
                best.Type, Severer(fromBreaker, DivergenceBands.Next(verification.DiscrepancyScore) ?? fromBreaker));

            return Result(
                CycleOutcome.Failed,
                $"Fallimento ciclo: {verification.AnalysisReport} -> strategia recovery: {strategy}",
                best.Type,
                strategy);
        }

        /// <summary>
        /// Raises a strategy to a hard stop for an action whose card forbids a
        /// retry, and leaves every other action's strategy alone.
        /// </summary>
        private RecoveryStrategy Escalate(ActionType action, RecoveryStrategy strategy)
            => _postConditions.TryGet(action, out IPostCondition postCondition) && postCondition.RetryForbidden
                ? RecoveryStrategy.HaltAndAlert
                : strategy;

        /// <summary>The stricter of two recovery strategies.</summary>
        private static RecoveryStrategy Severer(RecoveryStrategy a, RecoveryStrategy b)
        {
            static int Rank(RecoveryStrategy strategy) => strategy switch
            {
                RecoveryStrategy.Retry => 0,
                RecoveryStrategy.Replan => 1,
                RecoveryStrategy.DegradedReplan => 2,
                RecoveryStrategy.Cooling => 3,
                RecoveryStrategy.HaltAndAlert => 4,
                _ => 0,
            };
            return Rank(a) >= Rank(b) ? a : b;
        }

        /// <summary>
        /// How many times the world is sampled across one action's window when a
        /// sampler is bound.
        /// </summary>
        /// <remarks>
        /// Enough for a minimum or a maximum to mean something (VER-09) and few
        /// enough that a cycle does not spend its budget polling. The pacing of
        /// those samples belongs to the sampler, which is the only part that knows
        /// how its channel delivers: this loop asks, and does not sleep on the
        /// caller's behalf.
        /// </remarks>
        public const int WorldSamplesPerWindow = 3;

        /// <summary>
        /// Reads the world back as a series: the state the act was planned on,
        /// then what the window showed.
        /// </summary>
        /// <remarks>
        /// The first element is the baseline every card measures against, and it
        /// carries the instant it was really observed, so an action emitted on a
        /// remembered reading is judged against that reading and not against the
        /// moment the cycle ran. What follows is what came back afterwards: from
        /// the sampler when one is bound, and otherwise the single vitals reading
        /// the world-state observer can give — in which case a card that needs the
        /// position, the entities or the inventory says so by name rather than
        /// concluding anything.
        /// </remarks>
        private async Task<IReadOnlyList<Gate3WorldState>> ReadBackAsync(
            Gate3WorldState planned, CancellationToken token)
        {
            var series = new List<Gate3WorldState> { planned };

            if (_worldSampler is { } sample)
            {
                for (var i = 0; i < WorldSamplesPerWindow; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Gate3WorldState reading;
                    try
                    {
                        reading = await sample(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A perception fault leaves the cycle unverified, never
                        // torn down: the samples already taken still stand.
                        series.Add(Gate3WorldState.Unobserved($"world_sampler_failed:{ex.GetType().Name}"));
                        break;
                    }

                    series.Add(reading);
                }

                return series;
            }

            ObservedState observed = await _observer.ObserveAsync(token).ConfigureAwait(false);
            series.Add(observed.ToWorldState());
            return series;
        }

        /// <summary>
        /// Every entity sighting the series carried, each keeping its own instant.
        /// </summary>
        /// <remarks>
        /// Flattened rather than deduplicated: VER-09 asks for the series, and a
        /// target whose health fell and was healed inside one window is exactly
        /// the case a last-value view would lose.
        /// </remarks>
        private static IReadOnlyList<SelectableEntity> CollectSightings(IReadOnlyList<Gate3WorldState> series)
        {
            var sightings = new List<SelectableEntity>();
            foreach (Gate3WorldState state in series)
                if (state.Entities is { } entities) sightings.AddRange(entities);
            return sightings;
        }

        private Gate3CycleResult Result(CycleOutcome outcome, string summary, ActionType action, RecoveryStrategy? strategy)
        {
            switch (outcome)
            {
                case CycleOutcome.Confirmed:
                    _stageBoard.Record("Execute", true);
                    _stageBoard.Record("Verify", true);
                    break;
                case CycleOutcome.ExecutionDisabled:
                    _stageBoard.Record("Execute", false, "execution_disabled");
                    break;
                case CycleOutcome.Unverified:
                    _stageBoard.Record("Execute", true);
                    _stageBoard.Record("Verify", false, "unverified");
                    break;
                case CycleOutcome.Failed:
                    _stageBoard.Record("Execute", true);
                    _stageBoard.Record("Verify", false, "verification_failed");
                    break;
                case CycleOutcome.Blocked:
                    _stageBoard.Record("Orchestrator", false, "blocked");
                    break;
            }

            return new(outcome, summary, action, CurrentMode, _trust.CurrentTier, strategy);
        }
    }

    /// <summary>
    /// Gate 3 certification suite.
    /// </summary>
    /// <remarks>
    /// Results are accumulated rather than short-circuited, so one failure never
    /// hides the checks after it, and a test that throws is reported as a failure
    /// carrying its message instead of tearing down the run.
    /// </remarks>
    public static class Gate3TestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Gate 3 checks — Decision & Safety Closed Loop ===");

            bool allPassed = true;

            allPassed &= Run("Simulation is deterministic and side-effect free", TestSimulationPurity);
            allPassed &= Run("Ranking puts survival first at critical HP", TestTacticalRankingPriorities);
            allPassed &= Run("Safety Gate denies an action above the trust tier", TestSafetyGateTrustDenial);
            allPassed &= Run("A forged safety token is rejected", TestForgedTokenRejected);
            allPassed &= Run("A safety token is single use", TestTokenSingleUse);
            allPassed &= Run("An expired token authorises nothing", TestExpiredTokenRejected);
            allPassed &= Run("Guard blocks every action while STOPPED", TestGuardBlocksWhenStopped);
            allPassed &= Run("Guard blocks combat while COOLING", TestGuardBlocksCombatWhileCooling);
            allPassed &= Run("Guard blocks an over-risk action", TestGuardBlocksExcessiveRisk);
            allPassed &= Run("Recovery never escalates trust", TestRecoveryNeverEscalatesTrust);
            allPassed &= Run("Recovery degrades in order: retry, degraded, halt", TestRecoveryLadder);
            allPassed &= Run("An unobserved reading is not read as zero", TestUnobservedIsNotZero);

            allPassed &= await RunAsync("A token bound to another candidate is refused", TestTokenBindingEnforcedAsync);
            allPassed &= await RunAsync("Disabled execution is not reported as success", TestDisabledExecutionIsNotSuccessAsync);
            allPassed &= await RunAsync("Executed but unobserved is UNVERIFIED, not success", TestUnobservedExecutionIsUnverifiedAsync);
            allPassed &= await RunAsync("An observed mismatch is a discrepancy", TestObservedMismatchIsDiscrepancyAsync);
            allPassed &= await RunAsync("An observed match confirms the cycle", TestObservedMatchConfirmsAsync);
            allPassed &= await RunAsync("A failing observer leaves the cycle unverified", TestFailingObserverIsUnverifiedAsync);
            allPassed &= await RunAsync("A blocked cycle never reaches the effector", TestBlockedCycleNeverExecutesAsync);
            allPassed &= await RunAsync("Planning over UNKNOWN state is refused", TestUnknownWorldStateIsRefusedAsync);
            allPassed &= await RunAsync("Simulated state never reaches a live effector", TestSimulatedStateCannotActAsync);

            Console.WriteLine(allPassed
                ? "=== Gate 3 checks passed. Local only: this is not real-environment verification. ==="
                : "=== Gate 3 checks FAILED. See the lines marked FAIL above. ===");

            return allPassed;
        }

        private static bool Run(string name, Func<bool> check)
        {
            try { return Report(name, check(), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
        {
            try { return Report(name, await check().ConfigureAwait(false), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static bool Report(string name, bool passed, string? error)
        {
            string detail = error is null ? string.Empty : $" [{error}]";
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
            return passed;
        }

        // -- helpers ---------------------------------------------------------

        private static ActionCandidate Candidate(
            ActionType type = ActionType.MoveToPosition,
            TrustTier required = TrustTier.Tier1_Assisted) =>
            new(Guid.NewGuid(), type, TargetFor(type), 0, required, "test");

        /// <summary>A target of the shape the action type requires.</summary>
        private static ActionTarget TargetFor(ActionType type) => type switch
        {
            ActionType.UseBasicAttack or ActionType.TargetEntity or ActionType.UseSkill
                => new ActionTarget.Entity(101, new MapPoint(10, 10)),
            ActionType.MoveToPosition or ActionType.EmergencyFlee or ActionType.CollectGroundItem
                => new ActionTarget.Position(new MapPoint(10, 10)),
            ActionType.UseConsumable => new ActionTarget.InventorySlot(1),
            _ => ActionTarget.None.Instance,
        };

        private static PredictedOutcome Outcome(Guid id, float risk = 0.0f, string signature = "SIG") =>
            new(id, 0, 0, 200, 1.0f, risk, signature);

        private static SafetyGate Gate(TrustTier tier) =>
            new(new TrustBoundary(tier), new GuardPolicyEngine());

        /// <summary>An effector that records whether it was ever reached.</summary>
        private sealed class RecordingEffector : IActionEffector
        {
            public int Applications { get; private set; }
            public bool CanApply => true;
            public string? UnavailableReason => null;

            public Task<ExecutionResult> ApplyAsync(ActionCandidate candidate, CancellationToken cancellationToken = default)
            {
                Applications++;
                return Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null));
            }
        }

        private sealed class FixedObserver : IWorldStateObserver
        {
            private readonly ObservedState _state;
            public FixedObserver(ObservedState state) => _state = state;
            public bool CanObserve => true;
            public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default) => Task.FromResult(_state);
        }

        // -- checks ----------------------------------------------------------

        private static bool TestSimulationPurity()
        {
            var sim = new SimulationEngine();
            ActionCandidate candidate = Candidate(ActionType.UseSkill, TrustTier.Tier2_SemiAutonomous);

            PredictedOutcome first = sim.Simulate(candidate, 1000, 100, 1000);
            PredictedOutcome second = sim.Simulate(candidate, 1000, 100, 1000);

            return first.ExpectedMpDelta == -35 && first.StateSignatureAfter == second.StateSignatureAfter;
        }

        private static bool TestTacticalRankingPriorities()
        {
            var planner = new ActionPlanner();
            var sim = new SimulationEngine();
            var ranking = new TacticalRankingEngine();

            List<ActionCandidate> candidates = planner.PlanCandidates(200, 1000, 50, true, true);
            Dictionary<Guid, PredictedOutcome> predictions =
                candidates.ToDictionary(c => c.CandidateId, c => sim.Simulate(c, 200, 50, 1000));

            IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> ranked =
                ranking.RankCandidates(candidates, predictions, 200, 1000);

            return ranked.Count > 0
                   && ranked[0].Candidate.Type is ActionType.UseConsumable or ActionType.EmergencyFlee;
        }

        private static bool TestSafetyGateTrustDenial()
        {
            SafetyGate gate = Gate(TrustTier.Tier0_ReadOnly);
            ActionCandidate candidate = Candidate(ActionType.UseBasicAttack, TrustTier.Tier2_SemiAutonomous);

            bool authorized = gate.TryAuthorize(
                candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal, out _, out string? reason);

            return !authorized && reason is not null && reason.Contains("Diniego Trust", StringComparison.Ordinal);
        }

        private static bool TestForgedTokenRejected()
        {
            // A token whose signature does not come from this gate's key must never
            // authorise: without the check, anyone able to construct the type could act.
            SafetyGate gate = Gate(TrustTier.Tier4_FullAutonomous);
            var forged = new SafetyToken(Guid.NewGuid(), TrustTier.Tier4_FullAutonomous, new byte[32], TimeSpan.FromMinutes(1));

            return !gate.ValidateToken(forged);
        }

        private static bool TestTokenSingleUse()
        {
            SafetyGate gate = Gate(TrustTier.Tier4_FullAutonomous);
            ActionCandidate candidate = Candidate();

            if (!gate.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal, out SafetyToken? token, out _))
                return false;

            return token!.TryConsume() && !token.TryConsume();
        }

        private static bool TestExpiredTokenRejected()
        {
            SafetyGate gate = Gate(TrustTier.Tier4_FullAutonomous);
            ActionCandidate candidate = Candidate();

            if (!gate.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal, out SafetyToken? issued, out _))
                return false;

            // Same candidate id, so the signature verifies; only the TTL is past.
            var expired = new SafetyToken(
                candidate.CandidateId, issued!.GrantedTier, issued.Signature, TimeSpan.FromMilliseconds(-1));

            return !gate.ValidateToken(expired) && !expired.TryConsume();
        }

        private static bool TestGuardBlocksWhenStopped()
        {
            var guard = new GuardPolicyEngine();
            ActionCandidate candidate = Candidate(ActionType.UseConsumable);

            GuardEvaluationResult result = guard.Evaluate(
                candidate, Outcome(candidate.CandidateId), RuntimeMode.Stopped);

            return !result.IsAllowedByPolicy && result.ViolatedConstraints.Length > 0;
        }

        private static bool TestGuardBlocksCombatWhileCooling()
        {
            var guard = new GuardPolicyEngine();
            ActionCandidate attack = Candidate(ActionType.UseBasicAttack);
            ActionCandidate heal = Candidate(ActionType.UseConsumable);

            bool combatBlocked = !guard.Evaluate(attack, Outcome(attack.CandidateId), RuntimeMode.Cooling).IsAllowedByPolicy;
            // Recovery must stay possible while cooling, or thermal throttling would
            // prevent the character from saving itself.
            bool healAllowed = guard.Evaluate(heal, Outcome(heal.CandidateId), RuntimeMode.Cooling).IsAllowedByPolicy;

            return combatBlocked && healAllowed;
        }

        private static bool TestGuardBlocksExcessiveRisk()
        {
            var guard = new GuardPolicyEngine();
            ActionCandidate risky = Candidate(ActionType.UseSkill);
            ActionCandidate flee = Candidate(ActionType.EmergencyFlee);

            bool riskyBlocked = !guard.Evaluate(risky, Outcome(risky.CandidateId, risk: 0.9f), RuntimeMode.Normal).IsAllowedByPolicy;
            // Fleeing is the exception: it is the action taken *because* the situation
            // is dangerous, so the risk ceiling must not forbid it.
            bool fleeAllowed = guard.Evaluate(flee, Outcome(flee.CandidateId, risk: 0.9f), RuntimeMode.Normal).IsAllowedByPolicy;

            return riskyBlocked && fleeAllowed;
        }

        private static bool TestRecoveryNeverEscalatesTrust()
        {
            var trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            trust.DowngradeTrust(TrustTier.Tier0_ReadOnly);
            trust.DowngradeTrust(TrustTier.Tier4_FullAutonomous); // must be ignored

            bool stayedLow = trust.CurrentTier == TrustTier.Tier0_ReadOnly;

            bool hasEscalation = typeof(RecoveryController).GetMethods()
                .Select(m => m.Name.ToLowerInvariant())
                .Any(n => n.Contains("upgrade", StringComparison.Ordinal)
                          || n.Contains("elevate", StringComparison.Ordinal)
                          || n.Contains("grant", StringComparison.Ordinal));

            return stayedLow && !hasEscalation;
        }

        private static bool TestRecoveryLadder()
        {
            var trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            var recovery = new RecoveryController(trust);
            var mode = RuntimeMode.Normal;

            RecoveryStrategy first = recovery.HandleFailure(ref mode);
            RecoveryStrategy second = recovery.HandleFailure(ref mode);
            RecoveryStrategy third = recovery.HandleFailure(ref mode);
            RecoveryStrategy fourth = recovery.HandleFailure(ref mode);

            return first == RecoveryStrategy.Retry
                   && second == RecoveryStrategy.Retry
                   && third == RecoveryStrategy.DegradedReplan
                   && fourth == RecoveryStrategy.HaltAndAlert
                   && mode == RuntimeMode.Stopped
                   && trust.CurrentTier == TrustTier.Tier0_ReadOnly;
        }

        private static bool TestUnobservedIsNotZero()
        {
            // UNKNOWN must never collapse to a number. A verifier that read an absent
            // observation as 0 would confirm a prediction of death whenever perception
            // was simply unavailable.
            ObservedState unobserved = ObservedState.Unobserved("no_perception_backend");

            return !unobserved.IsFullyObserved
                   && unobserved.Hp.Source == DataSourceKind.Unknown
                   && !unobserved.Hp.HasValue
                   && unobserved.Hp.FailureReason == "no_perception_backend";
        }

        private static async Task<bool> TestTokenBindingEnforcedAsync()
        {
            SafetyGate gate = Gate(TrustTier.Tier4_FullAutonomous);
            var executor = new AuthorizedActionExecutor(gate, new RecordingEffector());

            ActionCandidate authorised = Candidate();
            ActionCandidate other = Candidate();

            if (!gate.TryAuthorize(authorised, Outcome(authorised.CandidateId), RuntimeMode.Normal, out SafetyToken? token, out _))
                return false;

            ExecutionResult result = await executor.ExecuteAuthorizedAsync(other, token!).ConfigureAwait(false);

            // Refused, and the token survives for its rightful owner: a misuse attempt
            // must not burn the authorisation someone else legitimately holds.
            return result.State == ExecutionState.Refused && token!.TryConsume();
        }

        private static async Task<bool> TestDisabledExecutionIsNotSuccessAsync()
        {
            // The regression this pins: the pipeline used to sleep 50 ms and report a
            // completed action while nothing had touched the client.
            var orchestrator = new Gate3ExecutionOrchestrator();
            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.ExecutionDisabled
                   && !result.IsConfirmed
                   && !orchestrator.CanExecute
                   && orchestrator.CurrentMode == RuntimeMode.Normal;
        }

        private static async Task<bool> TestUnobservedExecutionIsUnverifiedAsync()
        {
            // Executed for real, but nothing can read the world back. The cycle must
            // say so rather than claim the prediction held.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector());

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Unverified && !result.IsConfirmed && !orchestrator.CanVerify;
        }

        private static async Task<bool> TestObservedMismatchIsDiscrepancyAsync()
        {
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var observer = new FixedObserver(ObservedState.Live(1, 1));
            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector(), observer);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Failed && result.Strategy is not null;
        }

        private static async Task<bool> TestObservedMatchConfirmsAsync()
        {
            // The observation is built to match what the simulation predicts for the
            // action ranking will choose, so a confirmed cycle is reachable at all.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var sim = new SimulationEngine();
            var planner = new ActionPlanner();
            var ranking = new TacticalRankingEngine();

            const int hp = 800, maxHp = 1000, mp = 100;
            List<ActionCandidate> candidates = planner.PlanCandidates(hp, maxHp, mp, true, false);
            Dictionary<Guid, PredictedOutcome> predictions =
                candidates.ToDictionary(c => c.CandidateId, c => sim.Simulate(c, hp, mp, maxHp));
            (ActionCandidate best, _) = ranking.RankCandidates(candidates, predictions, hp, maxHp)[0];
            PredictedOutcome predicted = predictions[best.CandidateId];

            var observer = new FixedObserver(ObservedState.Live(
                Math.Clamp(hp + predicted.ExpectedHpDelta, 0, maxHp),
                Math.Max(0, mp + predicted.ExpectedMpDelta)));

            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector(), observer);
            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(hp, maxHp, mp, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Confirmed && result.IsConfirmed;
        }

        private static async Task<bool> TestFailingObserverIsUnverifiedAsync()
        {
            // A perception fault must leave the cycle unverified, never tear down the
            // pipeline and never look like a confirmation.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var observer = new DelegateWorldStateObserver(_ => throw new InvalidOperationException("probe down"));
            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector(), observer);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Unverified && !result.IsConfirmed;
        }

        /// <summary>
        /// Nothing known means nothing to plan from.
        /// </summary>
        /// <remarks>
        /// The input-side twin of confirming an unobserved outcome: the cycle used to
        /// take bare integers, so a caller could hand the planner invented numbers and
        /// get back a confident plan with nothing marking it as fiction.
        /// </remarks>
        private static async Task<bool> TestUnknownWorldStateIsRefusedAsync()
        {
            var orchestrator = new Gate3ExecutionOrchestrator();

            Gate3CycleResult result = await orchestrator
                .ExecuteCycleAsync(Gate3WorldState.Unobserved("gameplay_provider_not_available"))
                .ConfigureAwait(false);

            return result.Outcome == CycleOutcome.NoWorldState
                   && result.SelectedAction == ActionType.None
                   && result.Summary.Contains("gameplay_provider_not_available", StringComparison.Ordinal);
        }

        /// <summary>You may plan on simulated state; you may not act on it.</summary>
        private static async Task<bool> TestSimulatedStateCannotActAsync()
        {
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var effector = new RecordingEffector();
            var orchestrator = new Gate3ExecutionOrchestrator(policy, effector);

            Gate3CycleResult refused = await orchestrator
                .ExecuteCycleAsync(Gate3WorldState.Simulated(800, 1000, 100, true, false))
                .ConfigureAwait(false);

            // A dry run with nothing able to act is still legitimate.
            var dryRun = new Gate3ExecutionOrchestrator();
            Gate3CycleResult planned = await dryRun
                .ExecuteCycleAsync(Gate3WorldState.Simulated(800, 1000, 100, true, false))
                .ConfigureAwait(false);

            return refused.Outcome == CycleOutcome.RefusedSimulatedInput
                   && effector.Applications == 0
                   && planned.Outcome == CycleOutcome.ExecutionDisabled
                   && planned.SelectedAction != ActionType.None;
        }

        private static async Task<bool> TestBlockedCycleNeverExecutesAsync()
        {
            // Guard denial has to stop the action before the effector, not merely
            // report a refusal after the world was already touched.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var effector = new RecordingEffector();
            var orchestrator = new Gate3ExecutionOrchestrator(
                policy, effector, new FixedObserver(ObservedState.Live(0, 0)), TrustTier.Tier0_ReadOnly);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(Gate3WorldState.Live(800, 1000, 100, true, false)).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Blocked && effector.Applications == 0;
        }
    }
}
