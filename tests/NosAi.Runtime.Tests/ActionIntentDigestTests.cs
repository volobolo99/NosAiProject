using System.Reflection;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// R3 / ADR-0020 § 3: the token signs the act, not the identifier.
/// </summary>
/// <remarks>
/// The defect these close was written down before it was fixed
/// (<c>docs/GATE3_PIPELINE.md</c>): the HMAC covered <c>CandidateId</c> alone, so
/// <c>candidate with { Target = ... }</c> produced a different action carrying the same
/// Guid and the token went on validating it.
/// </remarks>
public sealed class ActionIntentDigestTests
{
    private static ActionCandidate Attack(long entityId = 7, int skill = 201) => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ActionType.UseBasicAttack,
        new ActionTarget.Entity(entityId, new MapPoint(10, 20)),
        skill,
        TrustTier.Tier2_SemiAutonomous,
        "because");

    private static ActionTokenIssuer Gate() => new(
        new TrustBoundary(TrustTier.Tier4_FullAutonomous), new GuardPolicyEngine());

    private static PredictedOutcome Outcome(Guid id) => new(id, 0, -35, 200, 0.95f, 0.1f, "SIG");

    // ------------------------------------------------------------- the canonical form

    [Fact]
    public void EveryIntentIsTheSameLength()
    {
        Assert.Equal(ActionIntentDigest.Size, ActionIntentDigest.Compute(Attack()).Length);
        Assert.Equal(
            ActionIntentDigest.Size,
            ActionIntentDigest.Compute(new ActionCandidate(
                Guid.NewGuid(), ActionType.RestAndRecover, ActionTarget.None.Instance, 0,
                TrustTier.Tier0_ReadOnly, "x")).Length);
    }

    /// <summary>
    /// The anti-ambiguity property, stated as a test: with no variable-length field there
    /// is no boundary to move, so two different intents cannot concatenate to one digest.
    /// </summary>
    [Fact]
    public void TheSameIntentAlwaysProducesTheSameBytes()
    {
        Assert.Equal(ActionIntentDigest.Compute(Attack()), ActionIntentDigest.Compute(Attack()));
    }

    [Fact]
    public void TheVersionLeadsTheDigest()
    {
        Assert.Equal(ActionIntentDigest.Version, ActionIntentDigest.Compute(Attack())[0]);
    }

    /// <summary>The field the whole record is about.</summary>
    [Fact]
    public void ADifferentTargetIsADifferentIntent()
    {
        ActionCandidate original = Attack(entityId: 7);
        ActionCandidate rebound = original with { Target = new ActionTarget.Entity(9, new MapPoint(10, 20)) };

        Assert.Equal(original.CandidateId, rebound.CandidateId);
        Assert.NotEqual(ActionIntentDigest.Compute(original), ActionIntentDigest.Compute(rebound));
    }

    [Fact]
    public void MovingWhereTheTargetWasSeenIsADifferentIntent()
    {
        ActionCandidate original = Attack();
        ActionCandidate moved = original with { Target = new ActionTarget.Entity(7, new MapPoint(11, 20)) };

        Assert.NotEqual(ActionIntentDigest.Compute(original), ActionIntentDigest.Compute(moved));
    }

    /// <summary>"Seen at 0,0" and "seen nowhere" are different claims and must not agree.</summary>
    [Fact]
    public void AnUnknownPositionIsNotTheOrigin()
    {
        var nowhere = new ActionCandidate(
            Guid.Empty, ActionType.UseBasicAttack, new ActionTarget.Entity(7), 0,
            TrustTier.Tier1_Assisted, "x");
        var atOrigin = nowhere with { Target = new ActionTarget.Entity(7, new MapPoint(0, 0)) };

        Assert.NotEqual(ActionIntentDigest.Compute(nowhere), ActionIntentDigest.Compute(atOrigin));
    }

    /// <summary>
    /// The discriminator separates targets whose payloads would otherwise coincide: an
    /// inventory slot 5 and a position whose x is 5 must not hash alike.
    /// </summary>
    [Fact]
    public void TargetsOfDifferentKindsDoNotCollide()
    {
        Guid id = Guid.NewGuid();
        var slot = new ActionCandidate(
            id, ActionType.UseConsumable, new ActionTarget.InventorySlot(5), 0,
            TrustTier.Tier1_Assisted, "x");
        var place = new ActionCandidate(
            id, ActionType.MoveToPosition, new ActionTarget.Position(new MapPoint(5, 0)), 0,
            TrustTier.Tier1_Assisted, "x");

        Assert.NotEqual(ActionIntentDigest.Compute(slot), ActionIntentDigest.Compute(place));
    }

    [Fact]
    public void TheSkillAndTheTrustTierAreBothSigned()
    {
        ActionCandidate original = Attack(skill: 201);

        Assert.NotEqual(
            ActionIntentDigest.Compute(original),
            ActionIntentDigest.Compute(original with { SkillOrItemId = 202 }));

        Assert.NotEqual(
            ActionIntentDigest.Compute(original),
            ActionIntentDigest.Compute(original with { RequiredTrust = TrustTier.Tier0_ReadOnly }));
    }

    /// <summary>
    /// Deliberately outside the digest: it explains the choice to a person and changes
    /// nothing about what happens to the client. Signing it would let a reworded sentence
    /// invalidate a live token — a refusal with no safety content.
    /// </summary>
    [Fact]
    public void ThePlainEnglishReasonIsNotSigned()
    {
        ActionCandidate original = Attack();

        Assert.Equal(
            ActionIntentDigest.Compute(original),
            ActionIntentDigest.Compute(original with { Rationale = "reworded, same act" }));
    }

    // --------------------------------------------------- the four ways to be refused

    /// <summary>THE test of this record: the scenario that used to pass.</summary>
    [Fact]
    public async Task AReboundTargetIsRefusedAllTheWayThroughTheExecutor()
    {
        ActionTokenIssuer gate = Gate();
        ActionCandidate original = Attack(entityId: 7);
        Assert.True(gate.TryAuthorize(
            original, Outcome(original.CandidateId), RuntimeMode.Normal, out SafetyToken? token, out _));

        ActionCandidate rebound = original with { Target = new ActionTarget.Entity(999, new MapPoint(1, 1)) };
        var executor = new AuthorizedActionExecutor(gate, new CountingEffector());

        ExecutionResult result = await executor.ExecuteAuthorizedAsync(rebound, token!);

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("safety_token_invalid_or_forged", result.Reason);
        // Refused before consumption, so the rightful act can still be carried out.
        Assert.False(token!.IsConsumed);
    }

    [Fact]
    public async Task AForgedTokenIsRefused()
    {
        ActionTokenIssuer gate = Gate();
        ActionCandidate candidate = Attack();
        var forged = new SafetyToken(
            candidate.CandidateId, TrustTier.Tier4_FullAutonomous, new byte[32], TimeSpan.FromMinutes(1));

        var executor = new AuthorizedActionExecutor(gate, new CountingEffector());
        ExecutionResult result = await executor.ExecuteAuthorizedAsync(candidate, forged);

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal("safety_token_invalid_or_forged", result.Reason);
    }

    [Fact]
    public void AnExpiredTokenIsRefused()
    {
        ActionTokenIssuer gate = Gate();
        ActionCandidate candidate = Attack();
        Assert.True(gate.TryAuthorize(
            candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal, out SafetyToken? issued, out _));

        // The same signature, so only the clock decides.
        var expired = new SafetyToken(
            candidate.CandidateId, issued!.GrantedTier, issued.Signature, TimeSpan.FromMilliseconds(-1));

        Assert.False(gate.ValidateToken(expired, candidate));
        Assert.False(expired.TryConsume());
    }

    [Fact]
    public async Task AConsumedTokenIsRefused()
    {
        ActionTokenIssuer gate = Gate();
        ActionCandidate candidate = Attack();
        Assert.True(gate.TryAuthorize(
            candidate, Outcome(candidate.CandidateId), RuntimeMode.Normal, out SafetyToken? token, out _));

        var executor = new AuthorizedActionExecutor(gate, new CountingEffector());

        Assert.NotEqual(ExecutionState.Refused, (await executor.ExecuteAuthorizedAsync(candidate, token!)).State);

        ExecutionResult second = await executor.ExecuteAuthorizedAsync(candidate, token!);
        Assert.Equal(ExecutionState.Refused, second.State);
        Assert.Equal("safety_token_already_consumed_or_expired", second.Reason);
    }

    // -------------------------------------------- the token reaches the boundary

    /// <summary>
    /// ADR-0020 § 4, kept as a property of the types: an effector that cannot receive an
    /// authorisation cannot be composed into the pipeline. Asserted by reflection because
    /// the guarantee is about the signature, and a guarantee about a signature that is
    /// only checked by the code that happens to exist today is not one.
    /// </summary>
    [Fact]
    public void NoEffectorCanBeComposedWithoutAnAuthorisation()
    {
        MethodInfo apply = typeof(IActionEffector).GetMethod(nameof(IActionEffector.ApplyAsync))!;

        Assert.Contains(apply.GetParameters(), p => p.ParameterType == typeof(SafetyToken));

        Type[] effectors = typeof(IActionEffector).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IActionEffector).IsAssignableFrom(t))
            .ToArray();

        Assert.NotEmpty(effectors);
        foreach (Type effector in effectors)
        {
            MethodInfo? implementation = effector.GetMethods()
                .FirstOrDefault(m => m.Name == nameof(IActionEffector.ApplyAsync));

            Assert.NotNull(implementation);
            Assert.Contains(
                implementation!.GetParameters(),
                p => p.ParameterType == typeof(SafetyToken));
        }
    }

    /// <summary>
    /// What the emitting boundary can check, and does: the interval ADR-0020 § 4 named as
    /// covered by nothing. Between the executor consuming the token and the click leaving,
    /// the effector resolves a keybind and projects a coordinate.
    /// </summary>
    [Fact]
    public async Task TheEmittingBoundaryRefusesATokenForAnotherAct()
    {
        (InputActionEffector effector, RecordingInputBackend recorder) = LiveEffector();
        ActionCandidate candidate = Attack();
        var forOther = new SafetyToken(
            Guid.NewGuid(), TrustTier.Tier2_SemiAutonomous, new byte[32], TimeSpan.FromMinutes(1));

        ExecutionResult result = await effector.ApplyAsync(candidate, forOther);

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal(InputActionEffector.TokenNotBoundReason, result.Reason);
        // Refused before anything was resolved or projected, so nothing reached the desktop.
        Assert.Empty(recorder.Events);
    }

    [Fact]
    public async Task TheEmittingBoundaryRefusesAnAuthorisationThatRanOut()
    {
        (InputActionEffector effector, RecordingInputBackend recorder) = LiveEffector();
        ActionCandidate candidate = Attack();
        var expired = new SafetyToken(
            candidate.CandidateId, TrustTier.Tier2_SemiAutonomous, new byte[32], TimeSpan.FromMilliseconds(-1));

        ExecutionResult result = await effector.ApplyAsync(candidate, expired);

        Assert.Equal(ExecutionState.Refused, result.State);
        Assert.Equal(InputActionEffector.TokenExpiredAtEmissionReason, result.Reason);
        Assert.Empty(recorder.Events);
    }

    /// <summary>A real effector over a backend that records instead of touching the desktop.</summary>
    private static (InputActionEffector Effector, RecordingInputBackend Recorder) LiveEffector()
    {
        RuntimeSafetyPolicy armed = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };
        var recorder = new RecordingInputBackend();
        var effector = new InputActionEffector(
            new GatedInputBackend(recorder, () => armed), KeybindMap.Empty, () => armed);

        return (effector, recorder);
    }

    private sealed class CountingEffector : IActionEffector
    {
        public int Calls { get; private set; }
        public bool CanApply => true;
        public string? UnavailableReason => null;

        public Task<ExecutionResult> ApplyAsync(
            ActionCandidate candidate, SafetyToken token, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ExecutionResult(candidate.CandidateId, ExecutionState.Completed, 1, null));
        }
    }
}
