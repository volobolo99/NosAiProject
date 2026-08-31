using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Threading;

namespace NosAi.Runtime.Autonomy;

/// <summary>
/// The vocabulary and safety primitives shared by every autonomous cycle.
/// </summary>
/// <remarks>
/// <para>
/// These types were declared three times over — once in <c>Gate3Runtime.cs</c>,
/// once in <c>Gate6Runtime.cs</c> and (for <see cref="TrustTier"/>) once more in
/// <c>NosAiMasterRuntimeHost.cs</c>. The copies were not identical, and the
/// differences were invisible precisely because they were copies: reading either
/// file alone gave no hint that another version existed, let alone that it
/// behaved differently.
/// </para>
/// <para>
/// <b>What the duplication was hiding.</b> Gate 6's <c>SafetyGate.ValidateToken</c>
/// checked the signature but <i>not</i> the expiry, while Gate 3's checked both.
/// Both gates issue tokens with a 1500 ms lifetime, so in the Gate 6 path that
/// lifetime was decoration: an expired token still validated. The merged
/// <see cref="SafetyGate"/> below enforces expiry, which is the fail-closed
/// behaviour both gates were written to have.
/// </para>
/// <para>
/// <b>What is deliberately not here.</b> <c>AuthorizedActionExecutor</c>,
/// <c>ActionExecutionVerifier</c>, <c>ExecutionResult</c> and
/// <c>VerificationResult</c> also appear in both gates, but they are not the same
/// thing under two names: Gate 3's are bound to a real effector and to classified
/// observation, Gate 6's to a simulated world. Folding them together would either
/// break Gate 6's certification or label simulated data as live, so each gate
/// keeps its own and says which world it is talking about.
/// </para>
/// </remarks>
public static class AutonomyPipelineNotes
{
    /// <summary>The namespace this file establishes, quoted where docs reference it.</summary>
    public const string Namespace = "NosAi.Runtime.Autonomy";
}

// ---------------------------------------------------------------- vocabulary

/// <summary>How much autonomy the runtime is currently trusted with.</summary>
/// <remarks>
/// Not to be confused with <c>NosAi.Runtime.Contracts.TrustTier</c>, which grades
/// the sensitivity of a <c>RuntimeCapability</c> request and numbers its tiers
/// 1–4 with no read-only floor. This one grades the runtime's own autonomy and
/// starts at <see cref="Tier0_ReadOnly"/>. Two different questions that happened
/// to pick the same word.
/// </remarks>
public enum TrustTier : byte
{
    Tier0_ReadOnly = 0,
    Tier1_Assisted = 1,
    Tier2_SemiAutonomous = 2,
    Tier3_AutonomousRestricted = 3,
    Tier4_FullAutonomous = 4
}

/// <summary>The operating state that gates which actions may run at all.</summary>
public enum RuntimeMode : byte
{
    Normal = 0,
    Degraded = 1,
    Recovery = 2,
    Cooling = 3,
    Stopped = 4
}

/// <summary>The kinds of action a cycle can propose.</summary>
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

/// <summary>What the runtime should do after a cycle failed verification.</summary>
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

/// <summary>
/// A Guard policy decision with its reasoning attached.
/// </summary>
/// <remarks>
/// Gate 6 previously reduced this to <c>bool</c> plus an <c>out string</c>. The
/// decision was identical; what was lost was the assessed risk and the list of
/// constraints violated, which is what makes a refusal reviewable afterwards.
/// </remarks>
public sealed record GuardEvaluationResult(
    bool IsAllowedByPolicy,
    float AssessedRisk,
    string Rationale,
    ImmutableArray<string> ViolatedConstraints);

// ------------------------------------------------------------- authorisation

/// <summary>
/// A single-use, signed, expiring authorisation to perform one specific action.
/// </summary>
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

    /// <summary>True for the first caller only, and only before expiry.</summary>
    /// <remarks>
    /// The compare-exchange is what makes "single use" true under concurrency: two
    /// threads racing on the same token must not both come away authorised.
    /// </remarks>
    public bool TryConsume() =>
        DateTime.UtcNow <= ExpiresAtUtc &&
        Interlocked.CompareExchange(ref _consumed, 1, 0) == 0;

    /// <summary>True once the token has been spent. Exposed for diagnostics.</summary>
    public bool IsConsumed => Volatile.Read(ref _consumed) == 1;

    /// <summary>True once the token is past its lifetime, spent or not.</summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAtUtc;
}

/// <summary>
/// The runtime's current autonomy level, which may fall but never rise here.
/// </summary>
/// <remarks>
/// <see cref="DowngradeTrust"/> is one-way by construction: recovery from a
/// degraded state is a decision for whoever is watching, not something the
/// failing component grants itself.
/// </remarks>
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

/// <summary>Applies the operating policy that decides whether an action may proceed.</summary>
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

    /// <summary>
    /// The boolean form Gate 6 used, kept so its call sites read unchanged.
    /// </summary>
    /// <remarks>
    /// It answers the same question as <see cref="Evaluate"/> and delegates to it,
    /// so the two can no longer drift apart. Callers that need the assessed risk or
    /// the violated constraints should use <see cref="Evaluate"/> directly.
    /// </remarks>
    public bool EvaluatePolicy(
        ActionCandidate candidate,
        PredictedOutcome outcome,
        RuntimeMode currentMode,
        out string? violation)
    {
        GuardEvaluationResult result = Evaluate(candidate, outcome, currentMode);
        violation = result.IsAllowedByPolicy ? null : result.Rationale;
        return result.IsAllowedByPolicy;
    }
}

/// <summary>
/// Issues and validates the signed tokens that authorise a single action.
/// </summary>
/// <remarks>
/// The signing key is generated per instance and never leaves it, so a token is
/// only valid at the gate that issued it.
/// </remarks>
public sealed class SafetyGate
{
    private readonly TrustBoundary _trustBoundary;
    private readonly GuardPolicyEngine _guardPolicy;
    private readonly byte[] _gateSigningKey;

    /// <summary>The lifetime both gates used, and the default here.</summary>
    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMilliseconds(1500);

    /// <summary>How long a token issued by this gate stays valid.</summary>
    public TimeSpan TokenLifetime { get; }

    /// <param name="tokenLifetime">
    /// Overridable so the expiry can actually be exercised. It was fixed at 1500 ms
    /// in both original copies, which is part of why the Gate 6 version could drop
    /// the expiry check without any test noticing.
    /// </param>
    public SafetyGate(TrustBoundary trustBoundary, GuardPolicyEngine guardPolicy, TimeSpan? tokenLifetime = null)
    {
        _trustBoundary = trustBoundary;
        _guardPolicy = guardPolicy;
        _gateSigningKey = RandomNumberGenerator.GetBytes(32);
        TokenLifetime = tokenLifetime ?? DefaultTokenLifetime;
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
            TokenLifetime);

        return true;
    }

    /// <summary>
    /// True only for a token this gate signed that has not yet expired.
    /// </summary>
    /// <remarks>
    /// The expiry check is load-bearing and was missing from the Gate 6 copy of
    /// this class: without it a token's lifetime is a comment rather than a limit,
    /// and an action authorised long ago could still be executed now.
    /// </remarks>
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
/// Escalates the response to repeated failures, giving up autonomy as it goes.
/// </summary>
/// <remarks>
/// Both gates ran this identical ladder — retry twice, then drop to assisted and
/// degrade, then drop to read-only and stop — but only Gate 3 reported which rung
/// it had reached. The shared version returns the strategy so a caller that wants
/// to know can, and one that does not can ignore it.
/// </remarks>
public sealed class RecoveryController
{
    private readonly TrustBoundary _trustBoundary;
    private readonly int _maxRetries;
    private int _consecutiveFailures;

    public int ConsecutiveFailures => _consecutiveFailures;

    public RecoveryController(TrustBoundary trustBoundary, int maxRetries = 2)
    {
        _trustBoundary = trustBoundary;
        _maxRetries = maxRetries;
    }

    /// <summary>
    /// Records one failure and returns how the runtime should respond to it.
    /// </summary>
    /// <remarks>
    /// Both original versions took the failing verification result and neither read
    /// it, so it is not a parameter here: a value the method ignores suggests the
    /// decision depends on it when it does not.
    /// </remarks>
    public RecoveryStrategy HandleFailure(ref RuntimeMode runtimeMode)
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

    /// <summary>The name Gate 6 used for <see cref="ResetFailures"/>.</summary>
    public void Reset() => ResetFailures();
}
