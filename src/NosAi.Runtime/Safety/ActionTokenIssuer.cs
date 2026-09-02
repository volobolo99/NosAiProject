using System.Security.Cryptography;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

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
/// Issues and validates the signed tokens that authorise a single action.
/// </summary>
/// <remarks>
/// The signing key is generated per instance and never leaves it, so a token is
/// only valid at the gate that issued it.
/// </remarks>
public sealed class ActionTokenIssuer
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
    public ActionTokenIssuer(TrustBoundary trustBoundary, GuardPolicyEngine guardPolicy, TimeSpan? tokenLifetime = null)
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

        // R3 / ADR-0020 § 3. This used to hash candidate.CandidateId alone, so
        // `candidate with { Target = ... }` produced a different action carrying the
        // same Guid and the token went on validating it. The signature now covers
        // every field that changes what the act does.
        Span<byte> intent = stackalloc byte[ActionIntentDigest.Size];
        ActionIntentDigest.Write(candidate, intent);

        var signature = new byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_gateSigningKey, intent, signature);

        token = new SafetyToken(
            candidate.CandidateId,
            _trustBoundary.CurrentTier,
            signature,
            TokenLifetime);

        return true;
    }

    /// <summary>
    /// True only for a token this gate signed <i>for this exact action</i>, and that has
    /// not yet expired.
    /// </summary>
    /// <remarks>
    /// The expiry check is load-bearing. Gate 6's copy of this method checked the
    /// signature and not the expiry, so on that path the 1500 ms lifetime both
    /// gates issued tokens with was decoration: an expired token still validated.
    /// Without the comparison an action authorised long ago could still be
    /// executed now, and that is why it must not be removed.
    /// </remarks>
    /// <param name="candidate">
    /// The action being presented for execution. Required, and this is the point of
    /// R3: the digest is recomputed from what the caller is <i>about to do</i>, so a
    /// candidate whose target was rebound after authorisation produces different bytes
    /// and fails the comparison. There is deliberately no overload that validates a
    /// token on its own — that overload was the defect, and leaving it beside the fixed
    /// method would leave every caller of it still broken.
    /// </param>
    public bool ValidateToken(SafetyToken token, ActionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(candidate);

        Span<byte> intent = stackalloc byte[ActionIntentDigest.Size];
        ActionIntentDigest.Write(candidate, intent);

        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_gateSigningKey, intent, expected);

        return CryptographicOperations.FixedTimeEquals(expected, token.Signature) &&
               token.ExpiresAtUtc >= DateTime.UtcNow;
    }
}
