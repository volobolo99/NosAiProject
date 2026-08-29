// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 3 — Pipeline Decisionale a Ciclo Chiuso
// Imported as supplied; source ends mid-RecoveryController and is therefore
// intentionally not certified as complete.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate3
{
    public enum TrustTier : byte { Tier0_ReadOnly = 0, Tier1_Assisted = 1, Tier2_SemiAutonomous = 2, Tier3_AutonomousRestricted = 3, Tier4_FullAutonomous = 4 }
    public enum ActionType : byte { None = 0, MoveToPosition = 1, TargetEntity = 2, UseBasicAttack = 3, UseSkill = 4, UseConsumable = 5, CollectGroundItem = 6, RestAndRecover = 7, EmergencyFlee = 8 }
    public enum RuntimeMode : byte { Normal = 0, Degraded = 1, Recovery = 2, Cooling = 3, Stopped = 4 }
    public enum RecoveryStrategy : byte { Retry = 0, Replan = 1, DegradedReplan = 2, Cooling = 3, HaltAndAlert = 4 }

    public sealed record ActionCandidate(Guid CandidateId, ActionType Type, string TargetId, int TargetX, int TargetY, int SkillOrItemId, TrustTier RequiredTrust, string Rationale);
    public sealed record PredictedOutcome(Guid CandidateId, int ExpectedHpDelta, int ExpectedMpDelta, int ExpectedTimeMs, float SuccessProbability, float RiskScore, string StateSignatureAfter);
    public sealed record GuardEvaluationResult(bool IsAllowedByPolicy, float AssessedRisk, string Rationale, ImmutableArray<string> ViolatedConstraints);
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
        { CandidateId = candidateId; GrantedTier = grantedTier; Signature = signature; IssuedAtUtc = DateTime.UtcNow; ExpiresAtUtc = IssuedAtUtc + ttl; }
        public bool TryConsume() => DateTime.UtcNow <= ExpiresAtUtc && Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;
    }
    public sealed record ExecutionResult(Guid CandidateId, bool ExecutionInitiated, bool ExecutionCompleted, int ActualDurationMs, string? ErrorMessage);
    public sealed record VerificationResult(Guid CandidateId, bool IsSuccess, float DiscrepancyScore, string AnalysisReport);

    public sealed class SimulationEngine
    {
        public PredictedOutcome Simulate(ActionCandidate candidate, int currentHp, int currentMp, int maxHp)
        {
            int hpDelta = 0, mpDelta = 0, timeMs = 250; float successProb = .95f, risk = .05f;
            switch (candidate.Type)
            {
                case ActionType.MoveToPosition: timeMs = 400; risk = currentHp < maxHp * .25 ? .40f : .05f; break;
                case ActionType.UseBasicAttack: timeMs = 600; hpDelta = -15; risk = currentHp < maxHp * .30 ? .65f : .15f; break;
                case ActionType.UseSkill: mpDelta = -35; timeMs = 800; risk = currentMp < 35 ? .90f : .10f; successProb = currentMp >= 35 ? .98f : 0f; break;
                case ActionType.UseConsumable: hpDelta = 300; mpDelta = 150; timeMs = 150; risk = .01f; break;
                case ActionType.EmergencyFlee: timeMs = 500; risk = .10f; break;
            }
            var signature = $"POST_HP_{Math.Clamp(currentHp + hpDelta, 0, maxHp)}_MP_{Math.Max(0, currentMp + mpDelta)}";
            return new PredictedOutcome(candidate.CandidateId, hpDelta, mpDelta, timeMs, successProb, risk, signature);
        }
    }

    public sealed class TacticalRankingEngine
    {
        public IReadOnlyList<(ActionCandidate Candidate, float UtilityScore)> RankCandidates(IReadOnlyList<ActionCandidate> candidates, IReadOnlyDictionary<Guid, PredictedOutcome> predictions, int playerHp, int maxHp)
        {
            var ranked = new List<(ActionCandidate, float)>(); double hpPercent = (double)playerHp / Math.Max(1, maxHp);
            foreach (var candidate in candidates)
            {
                if (!predictions.TryGetValue(candidate.CandidateId, out var outcome)) continue;
                float utility = 0f;
                if (hpPercent < .30) { if (candidate.Type is ActionType.UseConsumable or ActionType.EmergencyFlee) utility += .85f; else if (candidate.Type == ActionType.UseBasicAttack) utility -= .50f; }
                else { if (candidate.Type == ActionType.UseSkill) utility += .70f; else if (candidate.Type == ActionType.UseBasicAttack) utility += .55f; else if (candidate.Type == ActionType.MoveToPosition) utility += .40f; }
                utility += outcome.SuccessProbability * .30f - outcome.RiskScore * .40f;
                ranked.Add((candidate, MathF.Max(0f, utility)));
            }
            return ranked.OrderByDescending(x => x.Item2).ToList();
        }
    }

    public sealed class ActionPlanner
    {
        public List<ActionCandidate> PlanCandidates(int playerHp, int maxHp, int playerMp, bool hasTarget, bool isInCombat)
        {
            var list = new List<ActionCandidate>();
            if (playerHp < maxHp * .35) { list.Add(new ActionCandidate(Guid.NewGuid(), ActionType.UseConsumable, "ITEM_POTION_HP", 0, 0, 101, TrustTier.Tier1_Assisted, "HP critico: uso pozione di recupero")); list.Add(new ActionCandidate(Guid.NewGuid(), ActionType.EmergencyFlee, "SAFE_POS", 100, 80, 0, TrustTier.Tier1_Assisted, "HP critico: riposizionamento difensivo")); }
            if (hasTarget) { if (playerMp >= 35) list.Add(new ActionCandidate(Guid.NewGuid(), ActionType.UseSkill, "TARGET_MOB_01", 125, 85, 201, TrustTier.Tier2_SemiAutonomous, "Bersaglio attivo: skill ad alto impatto")); list.Add(new ActionCandidate(Guid.NewGuid(), ActionType.UseBasicAttack, "TARGET_MOB_01", 125, 85, 0, TrustTier.Tier2_SemiAutonomous, "Bersaglio attivo: attacco base")); }
            else list.Add(new ActionCandidate(Guid.NewGuid(), ActionType.MoveToPosition, "WAYPOINT_A", 130, 90, 0, TrustTier.Tier1_Assisted, "Esplorazione verso waypoint"));
            return list;
        }
    }

    public sealed class GuardPolicyEngine
    {
        public GuardEvaluationResult Evaluate(ActionCandidate candidate, PredictedOutcome outcome, RuntimeMode currentMode)
        {
            var violations = new List<string>();
            if (currentMode == RuntimeMode.Stopped) { violations.Add("Runtime in stato STOPPED: tutte le azioni sono inibite."); return new(false, 1f, "Blocco fail-closed Watchdog.", violations.ToImmutableArray()); }
            if (currentMode == RuntimeMode.Cooling && candidate.Type is ActionType.UseSkill or ActionType.UseBasicAttack) { violations.Add("Runtime in stato COOLING: inibite azioni di combattimento non necessarie."); return new(false, .8f, "Throttling termico attivo.", violations.ToImmutableArray()); }
            if (outcome.RiskScore > .75f && candidate.Type != ActionType.EmergencyFlee) { violations.Add($"Rischio stimato eccessivo ({outcome.RiskScore:P1} > 75%)."); return new(false, outcome.RiskScore, "Violazione soglia rischio massimo.", violations.ToImmutableArray()); }
            return new(true, outcome.RiskScore, "Azione conforme alle policy operative.", ImmutableArray<string>.Empty);
        }
    }

    public sealed class TrustBoundary
    {
        private TrustTier _currentTrust; private readonly object _lock = new();
        public TrustTier CurrentTier { get { lock (_lock) return _currentTrust; } }
        public TrustBoundary(TrustTier initialTier = TrustTier.Tier2_SemiAutonomous) => _currentTrust = initialTier;
        public bool IsAuthorized(TrustTier requiredTier) { lock (_lock) return _currentTrust >= requiredTier; }
        public void DowngradeTrust(TrustTier newTier) { lock (_lock) if (newTier < _currentTrust) _currentTrust = newTier; }
    }

    public sealed class SafetyGate
    {
        private readonly TrustBoundary _trustBoundary; private readonly GuardPolicyEngine _guardPolicy; private readonly byte[] _gateSigningKey;
        public SafetyGate(TrustBoundary trustBoundary, GuardPolicyEngine guardPolicy) { _trustBoundary = trustBoundary; _guardPolicy = guardPolicy; _gateSigningKey = RandomNumberGenerator.GetBytes(32); }
        public bool TryAuthorize(ActionCandidate candidate, PredictedOutcome outcome, RuntimeMode currentMode, out SafetyToken? token, out string? rejectionReason)
        {
            token = null; rejectionReason = null; var guard = _guardPolicy.Evaluate(candidate, outcome, currentMode);
            if (!guard.IsAllowedByPolicy) { rejectionReason = $"Diniego Guard AI: {guard.Rationale} [{string.Join(", ", guard.ViolatedConstraints)}]"; return false; }
            if (!_trustBoundary.IsAuthorized(candidate.RequiredTrust)) { rejectionReason = $"Diniego Trust: Richiesto {candidate.RequiredTrust}, livello corrente {_trustBoundary.CurrentTier}."; return false; }
            var signature = HMACSHA256.HashData(_gateSigningKey, candidate.CandidateId.ToByteArray()); token = new SafetyToken(candidate.CandidateId, _trustBoundary.CurrentTier, signature, TimeSpan.FromMilliseconds(1500)); return true;
        }
        public bool ValidateToken(SafetyToken token)
        { var expected = HMACSHA256.HashData(_gateSigningKey, token.CandidateId.ToByteArray()); return CryptographicOperations.FixedTimeEquals(expected, token.Signature) && token.ExpiresAtUtc >= DateTime.UtcNow; }
    }

    public sealed class AuthorizedActionExecutor
    {
        private readonly SafetyGate _safetyGate;
        public AuthorizedActionExecutor(SafetyGate safetyGate) => _safetyGate = safetyGate;
        public async Task<ExecutionResult> ExecuteAuthorizedAsync(ActionCandidate candidate, SafetyToken token, CancellationToken cancellationToken = default)
        {
            if (!_safetyGate.ValidateToken(token)) return new(candidate.CandidateId, false, false, 0, "SafetyToken non valido o firma contraffatta.");
            if (token.CandidateId != candidate.CandidateId || !token.TryConsume()) return new(candidate.CandidateId, false, false, 0, "SafetyToken già consumato, scaduto o non associato al candidato.");
            var sw = Stopwatch.StartNew();
            try { await Task.Delay(50, cancellationToken).ConfigureAwait(false); return new(candidate.CandidateId, true, true, (int)sw.ElapsedMilliseconds, null); }
            catch (Exception ex) { return new(candidate.CandidateId, true, false, (int)sw.ElapsedMilliseconds, ex.Message); }
            finally { sw.Stop(); }
        }
    }

    public sealed class ActionExecutionVerifier
    {
        public VerificationResult Verify(ActionCandidate candidate, PredictedOutcome predicted, ExecutionResult execution, int actualHpAfter, int actualMpAfter)
        {
            if (!execution.ExecutionCompleted) return new(candidate.CandidateId, false, 1f, $"Esecuzione fallita con errore: {execution.ErrorMessage ?? "Sconosciuto"}");
            var observed = $"POST_HP_{actualHpAfter}_MP_{actualMpAfter}"; bool matches = predicted.StateSignatureAfter == observed;
            return new(candidate.CandidateId, matches, matches ? 0f : .45f, matches ? "Verifica confermata: lo stato reale corrisponde alla simulazione." : $"Discrepanza rilevata: atteso {predicted.StateSignatureAfter}, osservato {observed}.");
        }
    }

    // NOTE: The supplied source terminates inside RecoveryController.HandleFailure.
    // No missing behavior is fabricated during import. Gate 3 remains uncertified.
}
