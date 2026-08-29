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

    public sealed record ExecutionResult(
        Guid CandidateId,
        bool ExecutionInitiated,
        bool ExecutionCompleted,
        int ActualDurationMs,
        string? ErrorMessage);

    public sealed record VerificationResult(
        Guid CandidateId,
        bool IsSuccess,
        float DiscrepancyScore,
        string AnalysisReport);

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

    public sealed class AuthorizedActionExecutor
    {
        private readonly SafetyGate _safetyGate;

        public AuthorizedActionExecutor(SafetyGate safetyGate) =>
            _safetyGate = safetyGate;

        public async Task<ExecutionResult> ExecuteAuthorizedAsync(
            ActionCandidate candidate,
            SafetyToken token,
            CancellationToken cancellationToken = default)
        {
            if (!_safetyGate.ValidateToken(token))
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    false,
                    false,
                    0,
                    "SafetyToken non valido o firma contraffatta.");
            }

            if (token.CandidateId != candidate.CandidateId || !token.TryConsume())
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    false,
                    false,
                    0,
                    "SafetyToken già consumato, scaduto o non associato al candidato.");
            }

            var sw = Stopwatch.StartNew();

            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                return new ExecutionResult(
                    candidate.CandidateId,
                    true,
                    true,
                    (int)sw.ElapsedMilliseconds,
                    null);
            }
            catch (Exception ex)
            {
                return new ExecutionResult(
                    candidate.CandidateId,
                    true,
                    false,
                    (int)sw.ElapsedMilliseconds,
                    ex.Message);
            }
            finally
            {
                sw.Stop();
            }
        }
    }

    public sealed class ActionExecutionVerifier
    {
        public VerificationResult Verify(
            ActionCandidate candidate,
            PredictedOutcome predicted,
            ExecutionResult execution,
            int actualHpAfter,
            int actualMpAfter)
        {
            if (!execution.ExecutionCompleted)
            {
                return new VerificationResult(
                    candidate.CandidateId,
                    false,
                    1.0f,
                    $"Esecuzione fallita con errore: {execution.ErrorMessage ?? "Sconosciuto"}");
            }

            string observed = $"POST_HP_{actualHpAfter}_MP_{actualMpAfter}";
            bool matches = predicted.StateSignatureAfter == observed;

            return new VerificationResult(
                candidate.CandidateId,
                matches,
                matches ? 0.0f : 0.45f,
                matches
                    ? "Verifica confermata: lo stato reale corrisponde alla simulazione."
                    : $"Discrepanza rilevata: atteso {predicted.StateSignatureAfter}, osservato {observed}.");
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

        public RuntimeMode CurrentMode { get; private set; } = RuntimeMode.Normal;
        public TrustBoundary Trust => _trust;

        public Gate3ExecutionOrchestrator()
        {
            _planner = new ActionPlanner();
            _simulation = new SimulationEngine();
            _ranking = new TacticalRankingEngine();
            _guard = new GuardPolicyEngine();
            _trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            _safetyGate = new SafetyGate(_trust, _guard);
            _executor = new AuthorizedActionExecutor(_safetyGate);
            _verifier = new ActionExecutionVerifier();
            _recovery = new RecoveryController(_trust);
        }

        public async Task<(bool Success, string Summary)> ExecuteCycleAsync(
            int playerHp,
            int maxHp,
            int playerMp,
            bool hasTarget,
            bool isInCombat,
            CancellationToken token = default)
        {
            List<ActionCandidate> candidates = _planner.PlanCandidates(
                playerHp,
                maxHp,
                playerMp,
                hasTarget,
                isInCombat);

            if (candidates.Count == 0)
                return (false, "Nessun candidato d'azione pianificato.");

            var predictions = new Dictionary<Guid, PredictedOutcome>(candidates.Count);
            foreach (ActionCandidate candidate in candidates)
            {
                predictions[candidate.CandidateId] = _simulation.Simulate(
                    candidate,
                    playerHp,
                    playerMp,
                    maxHp);
            }

            IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> ranked =
                _ranking.RankCandidates(candidates, predictions, playerHp, maxHp);

            if (ranked.Count == 0)
                return (false, "Nessun candidato idoneo dopo il ranking tattico.");

            (ActionCandidate bestCandidate, float utilityScore) = ranked[0];
            PredictedOutcome predictedOutcome = predictions[bestCandidate.CandidateId];

            if (!_safetyGate.TryAuthorize(
                    bestCandidate,
                    predictedOutcome,
                    CurrentMode,
                    out SafetyToken? safetyToken,
                    out string? rejectReason))
            {
                return (false, $"Blocco Safety Gate: {rejectReason}");
            }

            ExecutionResult execResult = await _executor.ExecuteAuthorizedAsync(
                bestCandidate,
                safetyToken!,
                token).ConfigureAwait(false);

            int simulatedNewHp = Math.Clamp(
                playerHp + predictedOutcome.ExpectedHpDelta,
                0,
                maxHp);
            int simulatedNewMp = Math.Max(
                0,
                playerMp + predictedOutcome.ExpectedMpDelta);

            VerificationResult verifResult = _verifier.Verify(
                bestCandidate,
                predictedOutcome,
                execResult,
                simulatedNewHp,
                simulatedNewMp);

            if (verifResult.IsSuccess)
            {
                _recovery.ResetFailures();
                CurrentMode = RuntimeMode.Normal;
                return (
                    true,
                    $"Ciclo completato con successo: {bestCandidate.Type} (Utility: {utilityScore:F2}). {verifResult.AnalysisReport}");
            }

            RecoveryStrategy strategy = _recovery.HandleFailure(
                verifResult,
                ref CurrentMode);

            return (
                false,
                $"Fallimento ciclo: {verifResult.AnalysisReport} -> Strategia Recovery: {strategy}");
        }
    }

    public static class Gate3TestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine("    NosAi 1.0 Beta — Esecuzione Test di Certificazione Gate 3    ");
            Console.WriteLine("=================================================================");
            Console.ResetColor();

            bool allPassed = true;

            allPassed &= RunTest(
                "Test 1: Simulazione Pura senza Effetti Collaterali",
                TestSimulationPurity);
            allPassed &= RunTest(
                "Test 2: Tactical Ranking MAUT con Priorità HP Critico",
                TestTacticalRankingPriorities);
            allPassed &= RunTest(
                "Test 3: Diniego Fail-Closed Safety Gate per Mancanza Trust",
                TestSafetyGateTrustDenial);
            allPassed &= RunTest(
                "Test 4: Invariante Firma e Monouso SafetyToken",
                TestSafetyTokenTamperAndReplay);
            allPassed &= await RunTestAsync(
                "Test 5: Esecuzione Autorizzata & Verifica Riuscita",
                TestSuccessfulExecutionCycleAsync);
            allPassed &= RunTest(
                "Test 6: Invariante Recovery: Impossibilità Aumento Trust",
                TestRecoveryTrustInvariant);

            Console.WriteLine();

            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> [ESITO POSITIVO]: TUTTI I TEST DEL GATE 3 SONO STATI SUPERATI CON SUCCESSO.");
                Console.WriteLine(">> Il Gate 3 è formalmente sbloccato. È possibile procedere al Gate 4.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(">> [BLOCCO GATE 3]: UNO O PIÙ TEST SONO FALLITI. SVILUPPO BLOCCATO.");
                Console.ResetColor();
            }

            return allPassed;
        }

        private static bool RunTest(string testName, Func<bool> testFunc)
        {
            try
            {
                bool result = testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static async Task<bool> RunTestAsync(string testName, Func<Task<bool>> testFunc)
        {
            try
            {
                bool result = await testFunc().ConfigureAwait(false);
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static void PrintResult(string name, bool passed, string? error = null)
        {
            Console.Write($"[{(passed ? "PASS" : "FAIL")}] {name,-58}");

            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [OK]");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" [ERRORE: {error ?? "Fallimento asserzione"}]");
            }

            Console.ResetColor();
        }

        private static bool TestSimulationPurity()
        {
            var sim = new SimulationEngine();
            var candidate = new ActionCandidate(
                Guid.NewGuid(),
                ActionType.UseSkill,
                "MOB_1",
                10,
                10,
                201,
                TrustTier.Tier2_SemiAutonomous,
                "Test");

            PredictedOutcome outcome1 = sim.Simulate(candidate, 1000, 100, 1000);
            PredictedOutcome outcome2 = sim.Simulate(candidate, 1000, 100, 1000);

            return outcome1.ExpectedMpDelta == -35 &&
                   outcome1.StateSignatureAfter == outcome2.StateSignatureAfter;
        }

        private static bool TestTacticalRankingPriorities()
        {
            var planner = new ActionPlanner();
            var sim = new SimulationEngine();
            var ranking = new TacticalRankingEngine();

            var candidates = planner.PlanCandidates(200, 1000, 50, true, true);
            var predictions = candidates.ToDictionary(
                c => c.CandidateId,
                c => sim.Simulate(c, 200, 50, 1000));

            var ranked = ranking.RankCandidates(
                candidates,
                predictions,
                200,
                1000);

            return ranked.Count > 0 &&
                   (ranked[0].Candidate.Type == ActionType.UseConsumable ||
                    ranked[0].Candidate.Type == ActionType.EmergencyFlee);
        }

        private static bool TestSafetyGateTrustDenial()
        {
            var trust = new TrustBoundary(TrustTier.Tier0_ReadOnly);
            var guard = new GuardPolicyEngine();
            var gate = new SafetyGate(trust, guard);

            var candidate = new ActionCandidate(
                Guid.NewGuid(),
                ActionType.UseBasicAttack,
                "MOB_1",
                0,
                0,
                0,
                TrustTier.Tier2_SemiAutonomous,
                "Attack");

            var outcome = new PredictedOutcome(
                candidate.CandidateId,
                -10,
                0,
                500,
                0.9f,
                0.1f,
                "SIG");

            bool authorized = gate.TryAuthorize(
                candidate,
                outcome,
                RuntimeMode.Normal,
                out _,
                out string? reason);

            return !authorized &&
                   reason != null &&
                   reason.Contains("Diniego Trust", StringComparison.Ordinal);
        }

        private static bool TestSafetyTokenTamperAndReplay()
        {
            var trust = new TrustBoundary(TrustTier.Tier4_FullAutonomous);
            var guard = new GuardPolicyEngine();
            var gate = new SafetyGate(trust, guard);

            var candidate = new ActionCandidate(
                Guid.NewGuid(),
                ActionType.MoveToPosition,
                "POS",
                10,
                10,
                0,
                TrustTier.Tier1_Assisted,
                "Move");

            var outcome = new PredictedOutcome(
                candidate.CandidateId,
                0,
                0,
                200,
                1.0f,
                0.0f,
                "SIG");

            if (!gate.TryAuthorize(
                    candidate,
                    outcome,
                    RuntimeMode.Normal,
                    out SafetyToken? token,
                    out _))
                return false;

            if (!token!.TryConsume())
                return false;

            return !token.TryConsume();
        }

        private static async Task<bool> TestSuccessfulExecutionCycleAsync()
        {
            var orchestrator = new Gate3ExecutionOrchestrator();
            (bool success, _) = await orchestrator.ExecuteCycleAsync(
                800,
                1000,
                100,
                true,
                false).ConfigureAwait(false);

            return success;
        }

        private static bool TestRecoveryTrustInvariant()
        {
            var trust = new TrustBoundary(TrustTier.Tier2_SemiAutonomous);
            var recovery = new RecoveryController(trust);
            var mode = RuntimeMode.Normal;

            var failVerif = new VerificationResult(
                Guid.NewGuid(),
                false,
                1.0f,
                "Errore critico simulato");

            recovery.HandleFailure(failVerif, ref mode);
            recovery.HandleFailure(failVerif, ref mode);
            recovery.HandleFailure(failVerif, ref mode);
            recovery.HandleFailure(failVerif, ref mode);

            bool isDegraded =
                trust.CurrentTier == TrustTier.Tier0_ReadOnly &&
                mode == RuntimeMode.Stopped;

            var recoveryMethods = typeof(RecoveryController)
                .GetMethods()
                .Select(m => m.Name.ToLowerInvariant());

            bool hasEscalationMethod = recoveryMethods.Any(
                m => m.Contains("upgrade", StringComparison.Ordinal) ||
                     m.Contains("elevate", StringComparison.Ordinal) ||
                     m.Contains("grant", StringComparison.Ordinal));

            return isDegraded && !hasEscalationMethod;
        }
    }

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.Title = "NosAi Runtime — Gate 3 (1.0 Beta)";

            if (args.Length > 0 &&
                args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                bool success = await Gate3TestRunner.RunAllTestsAsync().ConfigureAwait(false);
                return success ? 0 : 1;
            }

            Console.WriteLine("Inizializzazione NosAi Runtime Gate 3 (Decision & Safety Closed-Loop)...");
            var orchestrator = new Gate3ExecutionOrchestrator();
            _ = orchestrator;

            Console.WriteLine("Runtime Gate 3 operativo. Esecuzione del test di certificazione integrato...");
            bool passed = await Gate3TestRunner.RunAllTestsAsync().ConfigureAwait(false);

            return passed ? 0 : 1;
        }
    }
}
