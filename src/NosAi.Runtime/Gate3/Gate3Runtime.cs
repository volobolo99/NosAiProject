// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 3 — Pipeline Decisionale a Ciclo Chiuso
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate3
{
    public enum TrustTier : byte
    {
        Tier0_ReadOnly = 0,
        Tier1_Assisted = 1,
        Tier2_SemiAutonomous = 2,
        Tier3_AutonomousRestricted = 3,
        Tier4_FullAutonomous = 4
    }

    public enum ActionType : byte
    {
        None = 0,
        MoveToPosition = 1,
        TargetEntity = 2,
        UseBasicAttack = 3,
        UseSkill = 4,
        UseConsumable = 5,
        CollectGroundItem = 6,
        RestAndRecover = 7,
        EmergencyFlee = 8
    }

    public enum RuntimeMode : byte
    {
        Normal = 0,
        Degraded = 1,
        Recovery = 2,
        Cooling = 3,
        Stopped = 4
    }

    public enum RecoveryStrategy : byte
    {
        Retry = 0,
        Replan = 1,
        DegradedReplan = 2,
        Cooling = 3,
        HaltAndAlert = 4
    }

    public sealed record ActionCandidate(
        Guid CandidateId,
        ActionType Type,
        string TargetId,
        int TargetX,
        int TargetY,
        int SkillOrItemId,
        TrustTier RequiredTrust,
        string Rationale);

    public sealed record PredictedOutcome(
        Guid CandidateId,
        int ExpectedHpDelta,
        int ExpectedMpDelta,
        int ExpectedTimeMs,
        float SuccessProbability,
        float RiskScore,
        string StateSignatureAfter);

    public sealed record GuardEvaluationResult(
        bool IsAllowedByPolicy,
        float AssessedRisk,
        string Rationale,
        ImmutableArray<string> ViolatedConstraints);

    public sealed class SafetyToken
    {
        public Guid TokenId { get; } = Guid.NewGuid();
        public Guid CandidateId { get; }
        public DateTime IssuedAtUtc { get; }
        public DateTime ExpiresAtUtc { get; }
        public TrustTier GrantedTier { get; }
        public byte[] Signature { get; }

        private int _consumed;

        public SafetyToken(Guid candidateId, TrustTier grantedTier, byte[] signature, TimeSpan ttl)
        {
            CandidateId = candidateId;
            GrantedTier = grantedTier;
            Signature = signature;
            IssuedAtUtc = DateTime.UtcNow;
            ExpiresAtUtc = IssuedAtUtc + ttl;
        }

        public bool TryConsume() =>
            DateTime.UtcNow <= ExpiresAtUtc &&
            Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    }

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
                    else if (candidate.Type == ActionType.UseBasicAttack)
                        utility -= 0.50f;
                }
                else
                {
                    if (candidate.Type == ActionType.UseSkill)
                        utility += 0.70f;
                    else if (candidate.Type == ActionType.UseBasicAttack)
                        utility += 0.55f;
                    else if (candidate.Type == ActionType.MoveToPosition)
                        utility += 0.40f;
                }

                utility += outcome.SuccessProbability * 0.30f - outcome.RiskScore * 0.40f;
                ranked.Add((candidate, MathF.Max(0.0f, utility)));
            }

            return ranked.OrderByDescending(x => x.Item2).ToList();
        }
    }

    public sealed class ActionPlanner
    {
        public List<ActionCandidate> PlanCandidates(
            int playerHp,
            int maxHp,
            int playerMp,
            bool hasTarget,
            bool isInCombat)
        {
            var list = new List<ActionCandidate>();

            if (playerHp < maxHp * 0.35)
            {
                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.UseConsumable,
                    "ITEM_POTION_HP",
                    0,
                    0,
                    101,
                    TrustTier.Tier1_Assisted,
                    "HP critico: uso pozione di recupero"));

                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.EmergencyFlee,
                    "SAFE_POS",
                    100,
                    80,
                    0,
                    TrustTier.Tier1_Assisted,
                    "HP critico: riposizionamento difensivo"));
            }

            if (hasTarget)
            {
                if (playerMp >= 35)
                {
                    list.Add(new ActionCandidate(
                        Guid.NewGuid(),
                        ActionType.UseSkill,
                        "TARGET_MOB_01",
                        125,
                        85,
                        201,
                        TrustTier.Tier2_SemiAutonomous,
                        "Bersaglio attivo: skill ad alto impatto"));
                }

                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.UseBasicAttack,
                    "TARGET_MOB_01",
                    125,
                    85,
                    0,
                    TrustTier.Tier2_SemiAutonomous,
                    "Bersaglio attivo: attacco base"));
            }
            else
            {
                list.Add(new ActionCandidate(
                    Guid.NewGuid(),
                    ActionType.MoveToPosition,
                    "WAYPOINT_A",
                    130,
                    90,
                    0,
                    TrustTier.Tier1_Assisted,
                    "Esplorazione verso waypoint"));
            }

            return list;
        }
    }

    public sealed class GuardPolicyEngine
    {
        public GuardEvaluationResult Evaluate(
            ActionCandidate candidate,
            PredictedOutcome outcome,
            RuntimeMode currentMode)
        {
            var violations = new List<string>();

            if (currentMode == RuntimeMode.Stopped)
            {
                violations.Add("Runtime in stato STOPPED: tutte le azioni sono inibite.");
                return new GuardEvaluationResult(
                    false,
                    1.0f,
                    "Blocco fail-closed Watchdog.",
                    violations.ToImmutableArray());
            }

            if (currentMode == RuntimeMode.Cooling &&
                candidate.Type is ActionType.UseSkill or ActionType.UseBasicAttack)
            {
                violations.Add("Runtime in stato COOLING: inibite azioni di combattimento non necessarie.");
                return new GuardEvaluationResult(
                    false,
                    0.8f,
                    "Throttling termico attivo.",
                    violations.ToImmutableArray());
            }

            if (outcome.RiskScore > 0.75f && candidate.Type != ActionType.EmergencyFlee)
            {
                violations.Add($"Rischio stimato eccessivo ({outcome.RiskScore:P1} > 75%).");
                return new GuardEvaluationResult(
                    false,
                    outcome.RiskScore,
                    "Violazione soglia rischio massimo.",
                    violations.ToImmutableArray());
            }

            return new GuardEvaluationResult(
                true,
                outcome.RiskScore,
                "Azione conforme alle policy operative.",
                ImmutableArray<string>.Empty);
        }
    }

    public sealed class TrustBoundary
    {
        private TrustTier _currentTrust;
        private readonly object _lock = new();

        public TrustTier CurrentTier
        {
            get
            {
                lock (_lock)
                    return _currentTrust;
            }
        }

        public TrustBoundary(TrustTier initialTier = TrustTier.Tier2_SemiAutonomous) =>
            _currentTrust = initialTier;

        public bool IsAuthorized(TrustTier requiredTier)
        {
            lock (_lock)
                return _currentTrust >= requiredTier;
        }

        public void DowngradeTrust(TrustTier newTier)
        {
            lock (_lock)
            {
                if (newTier < _currentTrust)
                    _currentTrust = newTier;
            }
        }
    }

    public sealed class SafetyGate
    {
        private readonly TrustBoundary _trustBoundary;
        private readonly GuardPolicyEngine _guardPolicy;
        private readonly byte[] _gateSigningKey;

        public SafetyGate(TrustBoundary trustBoundary, GuardPolicyEngine guardPolicy)
        {
            _trustBoundary = trustBoundary;
            _guardPolicy = guardPolicy;
            _gateSigningKey = RandomNumberGenerator.GetBytes(32);
        }

        public bool TryAuthorize(
            ActionCandidate candidate,
            PredictedOutcome outcome,
            RuntimeMode currentMode,
            out SafetyToken? token,
            out string? rejectionReason)
        {
            token = null;
            rejectionReason = null;

            GuardEvaluationResult guard = _guardPolicy.Evaluate(candidate, outcome, currentMode);
            if (!guard.IsAllowedByPolicy)
            {
                rejectionReason = $"Diniego Guard AI: {guard.Rationale} [{string.Join(", ", guard.ViolatedConstraints)}]";
                return false;
            }

            if (!_trustBoundary.IsAuthorized(candidate.RequiredTrust))
            {
                rejectionReason = $"Diniego Trust: Richiesto {candidate.RequiredTrust}, livello corrente {_trustBoundary.CurrentTier}.";
                return false;
            }

            byte[] signature = HMACSHA256.HashData(
                _gateSigningKey,
                candidate.CandidateId.ToByteArray());

            token = new SafetyToken(
                candidate.CandidateId,
                _trustBoundary.CurrentTier,
                signature,
                TimeSpan.FromMilliseconds(1500));

            return true;
        }

        public bool ValidateToken(SafetyToken token)
        {
            byte[] expected = HMACSHA256.HashData(
                _gateSigningKey,
                token.CandidateId.ToByteArray());

            return CryptographicOperations.FixedTimeEquals(expected, token.Signature) &&
                   token.ExpiresAtUtc >= DateTime.UtcNow;
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
        public VerificationResult Verify(
            ActionCandidate candidate,
            PredictedOutcome predicted,
            ExecutionResult execution,
            ObservedState observed)
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

            if (!observed.IsFullyObserved)
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
    }

    public sealed class RecoveryController
    {
        private readonly TrustBoundary _trustBoundary;
        private readonly int _maxRetries = 2;
        private int _consecutiveFailures;

        public int ConsecutiveFailures => _consecutiveFailures;

        public RecoveryController(TrustBoundary trustBoundary) =>
            _trustBoundary = trustBoundary;

        public RecoveryStrategy HandleFailure(
            VerificationResult verification,
            ref RuntimeMode runtimeMode)
        {
            _consecutiveFailures++;

            if (_consecutiveFailures <= _maxRetries)
            {
                runtimeMode = RuntimeMode.Recovery;
                return RecoveryStrategy.Retry;
            }

            if (_consecutiveFailures == _maxRetries + 1)
            {
                _trustBoundary.DowngradeTrust(TrustTier.Tier1_Assisted);
                runtimeMode = RuntimeMode.Degraded;
                return RecoveryStrategy.DegradedReplan;
            }

            _trustBoundary.DowngradeTrust(TrustTier.Tier0_ReadOnly);
            runtimeMode = RuntimeMode.Stopped;
            return RecoveryStrategy.HaltAndAlert;
        }

        public void ResetFailures() => _consecutiveFailures = 0;
    }

    /// <summary>How a full Observe -> Plan -> Guard -> Execute -> Verify cycle ended.</summary>
    public enum CycleOutcome : byte
    {
        /// <summary>Executed and confirmed against an observation.</summary>
        Confirmed = 0,

        /// <summary>Nothing was planned, or nothing survived ranking.</summary>
        NoCandidate = 1,

        /// <summary>The Safety Gate refused authorisation.</summary>
        Blocked = 2,

        /// <summary>Policy forbids live input, so nothing was attempted.</summary>
        ExecutionDisabled = 3,

        /// <summary>Executed, but nothing could be observed to confirm it.</summary>
        Unverified = 4,

        /// <summary>Executed and the world does not match the prediction, or execution failed.</summary>
        Failed = 5
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
        private readonly IWorldStateObserver _observer;

        public RuntimeMode CurrentMode { get; private set; } = RuntimeMode.Normal;
        public TrustBoundary Trust => _trust;
        public RuntimeSafetyPolicy Policy { get; }

        /// <summary>Whether anything is bound that can actually act on the world.</summary>
        public bool CanExecute => _executor.Effector.CanApply;

        /// <summary>Whether anything is bound that can read the world back.</summary>
        public bool CanVerify => _observer.CanObserve;

        /// <param name="policy">
        /// Defaults to <see cref="RuntimeSafetyPolicy.SafeDefault"/>, which keeps live
        /// input and packet injection off.
        /// </param>
        /// <param name="effector">Bound only when the policy permits acting.</param>
        /// <param name="observer">
        /// Without one, every executed cycle ends unverified. That is the honest
        /// result, not a degraded mode to be papered over.
        /// </param>
        public Gate3ExecutionOrchestrator(
            RuntimeSafetyPolicy? policy = null,
            IActionEffector? effector = null,
            IWorldStateObserver? observer = null,
            TrustTier initialTrust = TrustTier.Tier2_SemiAutonomous)
        {
            Policy = policy ?? RuntimeSafetyPolicy.SafeDefault;
            _planner = new ActionPlanner();
            _simulation = new SimulationEngine();
            _ranking = new TacticalRankingEngine();
            _guard = new GuardPolicyEngine();
            _trust = new TrustBoundary(initialTrust);
            _safetyGate = new SafetyGate(_trust, _guard);
            _executor = new AuthorizedActionExecutor(_safetyGate, ActionEffectorFactory.ForPolicy(Policy, effector));
            _verifier = new ActionExecutionVerifier();
            _recovery = new RecoveryController(_trust);
            _observer = observer ?? new UnavailableWorldStateObserver();
        }

        public async Task<Gate3CycleResult> ExecuteCycleAsync(
            int playerHp,
            int maxHp,
            int playerMp,
            bool hasTarget,
            bool isInCombat,
            CancellationToken token = default)
        {
            List<ActionCandidate> candidates = _planner.PlanCandidates(
                playerHp, maxHp, playerMp, hasTarget, isInCombat);

            if (candidates.Count == 0)
                return Result(CycleOutcome.NoCandidate, "Nessun candidato d'azione pianificato.", ActionType.None, null);

            var predictions = new Dictionary<Guid, PredictedOutcome>(candidates.Count);
            foreach (ActionCandidate candidate in candidates)
                predictions[candidate.CandidateId] = _simulation.Simulate(candidate, playerHp, playerMp, maxHp);

            IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> ranked =
                _ranking.RankCandidates(candidates, predictions, playerHp, maxHp);

            if (ranked.Count == 0)
                return Result(CycleOutcome.NoCandidate, "Nessun candidato idoneo dopo il ranking tattico.", ActionType.None, null);

            (ActionCandidate best, float utility) = ranked[0];
            PredictedOutcome predicted = predictions[best.CandidateId];

            if (!_safetyGate.TryAuthorize(best, predicted, CurrentMode, out SafetyToken? safetyToken, out string? rejection))
                return Result(CycleOutcome.Blocked, $"Blocco Safety Gate: {rejection}", best.Type, null);

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

            // The world is read back, never derived from the prediction being checked.
            ObservedState observed = await _observer.ObserveAsync(token).ConfigureAwait(false);
            VerificationResult verification = _verifier.Verify(best, predicted, execution, observed);

            if (verification.IsConfirmed)
            {
                _recovery.ResetFailures();
                CurrentMode = RuntimeMode.Normal;
                return Result(
                    CycleOutcome.Confirmed,
                    $"Ciclo confermato: {best.Type} (utility {utility:F2}). {verification.AnalysisReport}",
                    best.Type,
                    null);
            }

            if (verification.Outcome == VerificationOutcome.Unverified)
            {
                // Executed but unconfirmed. Not counted as a failure -- the action may
                // well have worked -- but never reported as success, and the failure
                // counter is left untouched rather than reset.
                return Result(CycleOutcome.Unverified, verification.AnalysisReport, best.Type, null);
            }

            RuntimeMode recoveredMode = CurrentMode;
            RecoveryStrategy strategy = _recovery.HandleFailure(verification, ref recoveredMode);
            CurrentMode = recoveredMode;

            return Result(
                CycleOutcome.Failed,
                $"Fallimento ciclo: {verification.AnalysisReport} -> strategia recovery: {strategy}",
                best.Type,
                strategy);
        }

        private Gate3CycleResult Result(CycleOutcome outcome, string summary, ActionType action, RecoveryStrategy? strategy)
            => new(outcome, summary, action, CurrentMode, _trust.CurrentTier, strategy);
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
            new(Guid.NewGuid(), type, "TARGET", 10, 10, 0, required, "test");

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

            var failure = new VerificationResult(
                Guid.NewGuid(), VerificationOutcome.Discrepant, 1.0f, "test", DataSourceKind.Live);

            RecoveryStrategy first = recovery.HandleFailure(failure, ref mode);
            RecoveryStrategy second = recovery.HandleFailure(failure, ref mode);
            RecoveryStrategy third = recovery.HandleFailure(failure, ref mode);
            RecoveryStrategy fourth = recovery.HandleFailure(failure, ref mode);

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
            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, true, false).ConfigureAwait(false);

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

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, true, false).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Unverified && !result.IsConfirmed && !orchestrator.CanVerify;
        }

        private static async Task<bool> TestObservedMismatchIsDiscrepancyAsync()
        {
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var observer = new FixedObserver(ObservedState.Live(1, 1));
            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector(), observer);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, true, false).ConfigureAwait(false);

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
            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(hp, maxHp, mp, true, false).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Confirmed && result.IsConfirmed;
        }

        private static async Task<bool> TestFailingObserverIsUnverifiedAsync()
        {
            // A perception fault must leave the cycle unverified, never tear down the
            // pipeline and never look like a confirmation.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var observer = new DelegateWorldStateObserver(_ => throw new InvalidOperationException("probe down"));
            var orchestrator = new Gate3ExecutionOrchestrator(policy, new RecordingEffector(), observer);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, true, false).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Unverified && !result.IsConfirmed;
        }

        private static async Task<bool> TestBlockedCycleNeverExecutesAsync()
        {
            // Guard denial has to stop the action before the effector, not merely
            // report a refusal after the world was already touched.
            var policy = new RuntimeSafetyPolicy(true, false, true, true);
            var effector = new RecordingEffector();
            var orchestrator = new Gate3ExecutionOrchestrator(
                policy, effector, new FixedObserver(ObservedState.Live(0, 0)), TrustTier.Tier0_ReadOnly);

            Gate3CycleResult result = await orchestrator.ExecuteCycleAsync(800, 1000, 100, true, false).ConfigureAwait(false);

            return result.Outcome == CycleOutcome.Blocked && effector.Applications == 0;
        }
    }
}
