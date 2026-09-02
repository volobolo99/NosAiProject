using System.Collections.Immutable;
using Xunit;
using Xunit.Abstractions;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The safety primitives shared by Gate 3 and Gate 6 after the copies were merged.
/// </summary>
/// <remarks>
/// Gate 3 and Gate 6 each carried their own version of these types. Nothing here
/// tested the difference, which is how Gate 6's <c>ValidateToken</c> came to skip
/// the expiry check unnoticed. These tests pin the merged behaviour so a future
/// copy cannot drift the same way in silence.
/// </remarks>
public sealed class AutonomyPipelineTests
{
    private readonly ITestOutputHelper _output;

    public AutonomyPipelineTests(ITestOutputHelper output) => _output = output;

    private static ActionTokenIssuer NewGate(TrustTier tier = TrustTier.Tier4_FullAutonomous) =>
        new(new TrustBoundary(tier), new GuardPolicyEngine());

    private static ActionCandidate Candidate(
        ActionType type = ActionType.UseBasicAttack,
        TrustTier required = TrustTier.Tier1_Assisted) =>
        new(Guid.NewGuid(), type, TargetFor(type), 0, required, "test");

    /// <summary>
    /// A target of the shape each action type requires. The pairing is checked by
    /// <see cref="ActionCandidate"/> itself now, so a helper that handed every
    /// action an entity would fail at construction rather than quietly building a
    /// flight aimed at a monster.
    /// </summary>
    private static ActionTarget TargetFor(ActionType type) => type switch
    {
        ActionType.UseBasicAttack or ActionType.TargetEntity or ActionType.UseSkill
            => new ActionTarget.Entity(101, new MapPoint(10, 10)),
        ActionType.MoveToPosition or ActionType.EmergencyFlee or ActionType.CollectGroundItem
            => new ActionTarget.Position(new MapPoint(10, 10)),
        ActionType.UseConsumable => new ActionTarget.InventorySlot(1),
        _ => ActionTarget.None.Instance,
    };

    private static PredictedOutcome Outcome(Guid id, float risk = 0.1f) =>
        new(id, -5, 0, 100, 0.9f, risk, "POST_HP_95_MP_50");

    // ------------------------------------------------- the expiry that was missing

    [Fact]
    public void AnExpiredTokenIsRefusedEvenThoughItsSignatureIsGenuine()
    {
        // The signature has to be real for this to test anything: a forged one is
        // rejected by the HMAC check before expiry is ever considered, which is why
        // Gate 6's missing expiry check went unnoticed for so long.
        var gate = new ActionTokenIssuer(
            new TrustBoundary(TrustTier.Tier4_FullAutonomous),
            new GuardPolicyEngine(),
            tokenLifetime: TimeSpan.FromMilliseconds(30));
        var candidate = Candidate();

        Assert.True(gate.TryAuthorize(candidate, Outcome(candidate.CandidateId),
            RuntimeMode.Normal, out SafetyToken? token, out _));
        Assert.True(gate.ValidateToken(token!, candidate));

        Evidence.Live(_output, "durataToken", gate.TokenLifetime.TotalMilliseconds + " ms");
        Evidence.Live(_output, "validoAppenaEmesso", true);

        Thread.Sleep(60);

        Evidence.Live(_output, "scaduto", token!.IsExpired);
        Evidence.Live(_output, "validoDopoLaScadenza", gate.ValidateToken(token, candidate),
            "la firma e' ancora buona: a cadere e' solo il tempo");

        // Same gate, same key, same token: only time has passed.
        Assert.True(token!.IsExpired);
        Assert.False(gate.ValidateToken(token, candidate));
        Assert.False(token.TryConsume());
    }

    [Fact]
    public void AFreshTokenFromThisGateIsAccepted()
    {
        var gate = NewGate();
        var candidate = Candidate();

        Assert.True(gate.TryAuthorize(candidate, Outcome(candidate.CandidateId),
            RuntimeMode.Normal, out SafetyToken? token, out _));
        Assert.True(gate.ValidateToken(token!, candidate));
    }

    [Fact]
    public void ATokenFromAnotherGateIsRefused()
    {
        // The signing key never leaves the gate that made it, so a token minted
        // elsewhere must not authorise anything here.
        var issuer = NewGate();
        var other = NewGate();
        var candidate = Candidate();
        issuer.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal,
            out SafetyToken? token, out _);

        Assert.False(other.ValidateToken(token!, candidate));
    }

    [Fact]
    public void ATamperedSignatureIsRefused()
    {
        var gate = NewGate();
        var candidate = Candidate();
        gate.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal,
            out SafetyToken? token, out _);

        token!.Signature[0] ^= 0xFF;

        Assert.False(gate.ValidateToken(token, candidate));
    }

    /// <summary>
    /// What the signature actually covers — the test that inverted when R3 landed.
    /// before changing the shape of the target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test used to assert the opposite, and that is the point of R3.</b>
    /// <c>TryAuthorize</c> signed <c>candidate.CandidateId</c> and nothing else — not
    /// the action type, not the target, not the trust required — so a candidate copied
    /// with a different target kept its id and the token went on validating it. The
    /// token authorised <i>an identifier</i>, not <i>an action</i>. It was recorded as a
    /// limit in <c>docs/GATE3_PIPELINE.md</c> and written down here as a fact rather
    /// than fixed, because widening the HMAC is a change to security behaviour and
    /// needed its own decision (ADR-0020 § 3) instead of a refactor's coat-tails.
    /// </para>
    /// <para>
    /// The decision was taken and the digest now covers every field that changes what
    /// the act does, so the same scenario refuses. The two assertions at the end are the
    /// whole difference between the two designs.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATokenDoesNotValidateACandidateWhoseTargetWasRebound()
    {
        var gate = NewGate();
        ActionCandidate candidate = Candidate(ActionType.UseBasicAttack);
        gate.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal,
            out SafetyToken? token, out _);

        // A different target, the same id: exactly what the old signature could not see.
        ActionCandidate elsewhere = candidate with
        {
            Target = new ActionTarget.Entity(999_999, new MapPoint(1, 1))
        };

        Assert.Equal(candidate.CandidateId, elsewhere.CandidateId);
        Assert.Equal(token!.CandidateId, elsewhere.CandidateId);
        Assert.NotEqual(candidate.Target, elsewhere.Target);

        // The act it was issued for still validates; the substituted one does not.
        Assert.True(gate.ValidateToken(token, candidate));
        Assert.False(gate.ValidateToken(token, elsewhere));
    }

    [Fact]
    public void TheIssuedLifetimeIsTheOneTheGateDeclares()
    {
        var gate = NewGate();
        var candidate = Candidate();
        gate.TryAuthorize(candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal,
            out SafetyToken? token, out _);

        Assert.Equal(gate.TokenLifetime, token!.ExpiresAtUtc - token.IssuedAtUtc);
        Assert.Equal(ActionTokenIssuer.DefaultTokenLifetime, gate.TokenLifetime);
    }

    // ------------------------------------------------------------ single use

    [Fact]
    public void ATokenIsSpentExactlyOnce()
    {
        var token = new SafetyToken(Guid.NewGuid(), TrustTier.Tier2_SemiAutonomous,
            new byte[32], TimeSpan.FromSeconds(5));

        Assert.True(token.TryConsume());
        Assert.False(token.TryConsume());
        Assert.True(token.IsConsumed);
    }

    [Fact]
    public void OnlyOneOfManyRacingThreadsSpendsTheToken()
    {
        // "Single use" has to hold under concurrency or it authorises twice for one
        // approval, which is the whole thing the token exists to prevent.
        var token = new SafetyToken(Guid.NewGuid(), TrustTier.Tier2_SemiAutonomous,
            new byte[32], TimeSpan.FromSeconds(5));
        int winners = 0;

        Parallel.For(0, 64, _ =>
        {
            if (token.TryConsume())
                Interlocked.Increment(ref winners);
        });

        Assert.Equal(1, winners);
    }

    [Fact]
    public void AnExpiredTokenCannotBeSpent()
    {
        var token = new SafetyToken(Guid.NewGuid(), TrustTier.Tier2_SemiAutonomous,
            new byte[32], TimeSpan.FromMilliseconds(-1));

        Assert.False(token.TryConsume());
        Assert.True(token.IsExpired);
    }

    // ---------------------------------------------------------- guard policy

    [Theory]
    [InlineData(RuntimeMode.Stopped, ActionType.MoveToPosition, 0.0f)]
    [InlineData(RuntimeMode.Cooling, ActionType.UseSkill, 0.0f)]
    [InlineData(RuntimeMode.Cooling, ActionType.UseBasicAttack, 0.0f)]
    [InlineData(RuntimeMode.Normal, ActionType.UseSkill, 0.9f)]
    public void ThePolicyRefusesWhatItAlwaysRefused(RuntimeMode mode, ActionType type, float risk)
    {
        var engine = new GuardPolicyEngine();
        var candidate = Candidate(type);

        var result = engine.Evaluate(candidate, Outcome(candidate.CandidateId, risk), mode);

        Assert.False(result.IsAllowedByPolicy);
        Assert.NotEmpty(result.ViolatedConstraints);
        Assert.NotEmpty(result.Rationale);
    }

    [Fact]
    public void FleeingIsAllowedEvenAtHighRisk()
    {
        // The one action whose whole point is that the situation is already bad.
        var engine = new GuardPolicyEngine();
        var candidate = Candidate(ActionType.EmergencyFlee);

        Assert.True(engine.Evaluate(candidate, Outcome(candidate.CandidateId, 0.99f),
            RuntimeMode.Normal).IsAllowedByPolicy);
    }

    [Theory]
    [InlineData(RuntimeMode.Stopped, ActionType.MoveToPosition, 0.0f)]
    [InlineData(RuntimeMode.Cooling, ActionType.UseSkill, 0.0f)]
    [InlineData(RuntimeMode.Normal, ActionType.UseSkill, 0.9f)]
    [InlineData(RuntimeMode.Normal, ActionType.UseSkill, 0.1f)]
    [InlineData(RuntimeMode.Normal, ActionType.EmergencyFlee, 0.99f)]
    public void BothFormsOfThePolicyAgree(RuntimeMode mode, ActionType type, float risk)
    {
        // Gate 6 used the boolean form and Gate 3 the reasoned one. They now share a
        // body, and this is what keeps them from being allowed to disagree again.
        var engine = new GuardPolicyEngine();
        var candidate = Candidate(type);
        var outcome = Outcome(candidate.CandidateId, risk);

        bool boolForm = engine.EvaluatePolicy(candidate, outcome, mode, out string? violation);
        var richForm = engine.Evaluate(candidate, outcome, mode);

        Assert.Equal(richForm.IsAllowedByPolicy, boolForm);
        Assert.Equal(boolForm, violation is null);
    }

    // ------------------------------------------------------- trust boundary

    [Fact]
    public void TrustFallsButNeverRises()
    {
        // A component that could restore its own autonomy after failing would be
        // deciding it is trustworthy again on its own say-so.
        var boundary = new TrustBoundary(TrustTier.Tier3_AutonomousRestricted);

        boundary.DowngradeTrust(TrustTier.Tier1_Assisted);
        Assert.Equal(TrustTier.Tier1_Assisted, boundary.CurrentTier);

        boundary.DowngradeTrust(TrustTier.Tier4_FullAutonomous);
        Assert.Equal(TrustTier.Tier1_Assisted, boundary.CurrentTier);
    }

    [Fact]
    public void AGateRefusesAnActionAboveTheCurrentTier()
    {
        var gate = NewGate(TrustTier.Tier1_Assisted);
        var candidate = Candidate(required: TrustTier.Tier4_FullAutonomous);

        bool ok = gate.TryAuthorize(candidate, Outcome(candidate.CandidateId),
            RuntimeMode.Normal, out SafetyToken? token, out string? reason);

        Assert.False(ok);
        Assert.Null(token);
        Assert.Contains("Trust", reason);
    }

    // ---------------------------------------------------------- the recovery

    [Fact]
    public void FailuresEscalateThroughRetryThenDegradeThenHalt()
    {
        var boundary = new TrustBoundary(TrustTier.Tier4_FullAutonomous);
        var recovery = new RecoveryController(boundary);
        var mode = RuntimeMode.Normal;

        Assert.Equal(RecoveryStrategy.Retry, recovery.HandleFailure(ref mode));
        Assert.Equal(RecoveryStrategy.Retry, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Recovery, mode);

        Assert.Equal(RecoveryStrategy.DegradedReplan, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Degraded, mode);
        Assert.Equal(TrustTier.Tier1_Assisted, boundary.CurrentTier);

        Assert.Equal(RecoveryStrategy.HaltAndAlert, recovery.HandleFailure(ref mode));
        Assert.Equal(RuntimeMode.Stopped, mode);
        Assert.Equal(TrustTier.Tier0_ReadOnly, boundary.CurrentTier);
    }

    [Fact]
    public void AStoppedRuntimeAuthorisesNothing()
    {
        // The end of the ladder has to be a real floor: once stopped, the gate must
        // refuse regardless of how safe the next action looks.
        var gate = NewGate();
        var candidate = Candidate(ActionType.MoveToPosition);

        Assert.False(gate.TryAuthorize(candidate, Outcome(candidate.CandidateId, 0.0f),
            RuntimeMode.Stopped, out _, out _));
    }

    [Fact]
    public void ResetClearsTheFailureCountUnderEitherName()
    {
        var recovery = new RecoveryController(new TrustBoundary());
        var mode = RuntimeMode.Normal;
        recovery.HandleFailure(ref mode);

        recovery.ResetFailures();
        Assert.Equal(0, recovery.ConsecutiveFailures);

        recovery.HandleFailure(ref mode);
        recovery.Reset();
        Assert.Equal(0, recovery.ConsecutiveFailures);
    }

    // ------------------------------------------------- the canonical TrustTier scale

    [Fact]
    public void TheCanonicalTrustScaleStartsAtReadOnlyAndKeepsTheNumericTiers()
    {
        Assert.Equal(0, (int)TrustTier.Tier0_ReadOnly);
        Assert.Equal(1, (int)TrustTier.Tier1_Assisted);
        Assert.Equal(2, (int)TrustTier.Tier2_SemiAutonomous);
        Assert.Equal(3, (int)TrustTier.Tier3_AutonomousRestricted);
        Assert.Equal(4, (int)TrustTier.Tier4_FullAutonomous);
    }
}
