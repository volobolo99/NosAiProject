using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.LiveIntegration;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-05/A2: the pure bridge from real player-vitals readings
/// (<see cref="PlayerVitalsReading"/>, produced by the live memory chain
/// <c>ClientMemorySession.TryReadPlayerVitals</c>) into the canonical combat
/// evidence contract (<c>CombatExecutionEvidence</c>, AP-05/A1), without
/// <c>NosAi.Core</c> ever referencing <c>NosAi.Runtime</c>.
/// </summary>
public sealed class CombatVerificationProjectorTests
{
    private static readonly DateTime FixedInstant = DateTime.UnixEpoch;
    private static readonly EntityId Target = new("mob-1");
    private static readonly SkillId Skill = new("skill-201");

    private static CombatActionCandidate Candidate(CombatActionKind kind) => kind switch
    {
        CombatActionKind.UseSkill => new(kind, target: Target, skill: Skill),
        CombatActionKind.BasicAttack => new(kind, target: Target),
        CombatActionKind.UseConsumable => new(kind, item: new ItemId("item-1")),
        CombatActionKind.Reposition => new(kind, destination: new WorldPosition(3f, 4f)),
        CombatActionKind.Flee => new(kind),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static PlayerVitalsReading Vitals(uint hp, uint maxHp, uint mp, uint maxMp) =>
        new(hp, maxHp, mp, maxMp);

    // -- ExpectedResource: only UseSkill spends a pool -----------------------

    [Theory]
    [InlineData(CombatActionKind.BasicAttack)]
    [InlineData(CombatActionKind.UseConsumable)]
    [InlineData(CombatActionKind.Reposition)]
    [InlineData(CombatActionKind.Flee)]
    public void ExpectedResource_EveryNonUseSkillKind_IsNull(CombatActionKind kind)
    {
        Assert.Null(CombatVerificationProjector.ExpectedResource(kind));
    }

    [Fact]
    public void ExpectedResource_UseSkill_IsMana()
    {
        Assert.Equal(ResourceKind.Mana, CombatVerificationProjector.ExpectedResource(CombatActionKind.UseSkill));
    }

    // -- non-UseSkill kinds are unobservable regardless of vitals ------------

    [Theory]
    [InlineData(CombatActionKind.BasicAttack)]
    [InlineData(CombatActionKind.UseConsumable)]
    [InlineData(CombatActionKind.Reposition)]
    [InlineData(CombatActionKind.Flee)]
    public void Project_EveryNonUseSkillKind_ReturnsUnobservedWithResourceNotObservable_RegardlessOfVitals(CombatActionKind kind)
    {
        CombatActionCandidate candidate = Candidate(kind);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 50, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.Detail);
        Assert.Null(evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.Before.Reason);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.After.Reason);
    }

    [Fact]
    public void Project_NonUseSkillKind_ReturnsUnobserved_EvenWhenNoVitalsWereReadAtAll()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.BasicAttack);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before: null, after: null, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.Detail);
    }

    // -- UseSkill: missing vitals ---------------------------------------------

    [Fact]
    public void Project_UseSkillWithNoBeforeRead_ReturnsUnobservedWithVitalsNotObserved()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 80, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before: null, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Detail);
        Assert.Equal(ResourceKind.Mana, evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Before.Reason);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.After.Reason);
    }

    [Fact]
    public void Project_UseSkillWithNoAfterRead_ReturnsUnobservedWithVitalsNotObserved()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after: null, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Detail);
    }

    // -- UseSkill: the verdict on a real before/after pair --------------------

    [Fact]
    public void Project_UseSkillWithMpFalling_ReturnsResourceCostConfirmed_WithLiveFactsCarryingExactMp()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 60, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after, FixedInstant);

        Assert.True(evidence.ResourceCostConfirmed);
        Assert.Equal(CombatExecutionResult.ResourceCostConfirmed, evidence.Result);
        Assert.Equal(ResourceKind.Mana, evidence.ResourceObserved);
        Assert.Null(evidence.Detail);

        Assert.True(evidence.Before.HasValue);
        Assert.True(evidence.After.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Before.Source);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.After.Source);
        Assert.Equal(100d, evidence.Before.Value);
        Assert.Equal(60d, evidence.After.Value);
        Assert.Equal(FixedInstant, evidence.Before.ObservedAtUtc);
        Assert.Equal(FixedInstant, evidence.After.ObservedAtUtc);
    }

    [Fact]
    public void Project_UseSkillWithMpUnchanged_ReturnsNoResourceChangeObserved()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceCostConfirmed);
        Assert.Equal(100d, evidence.Before.Value);
        Assert.Equal(100d, evidence.After.Value);
    }

    [Fact]
    public void Project_UseSkillWithMpRising_ReturnsNoResourceChangeObserved()
    {
        // A rising pool is also "no cost observed" -- the act did not spend the
        // expected resource (a tick may have refilled it); only a fall is
        // evidence of a cost, never a rise.
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 80, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 95, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
    }

    [Fact]
    public void Project_CarriesTheCandidateOnTheEvidence()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseSkill);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 90, maxMp: 100);

        CombatExecutionEvidence evidence = CombatVerificationProjector.Project(candidate, before, after, FixedInstant);

        Assert.Same(candidate, evidence.Candidate);
    }

    // -- ProjectRecovery: the UseConsumable recovery verdict ------------------

    [Fact]
    public void ProjectRecovery_UseConsumableWithHpRising_ReturnsResourceGainConfirmed_WithLiveFactsCarryingExactHp()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading before = Vitals(hp: 60, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after, FixedInstant);

        Assert.True(evidence.ResourceGainConfirmed);
        Assert.Equal(CombatExecutionResult.ResourceGainConfirmed, evidence.Result);
        Assert.Equal(ResourceKind.Health, evidence.ResourceObserved);
        Assert.Null(evidence.Detail);

        Assert.True(evidence.Before.HasValue);
        Assert.True(evidence.After.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Before.Source);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.After.Source);
        Assert.Equal(60d, evidence.Before.Value);
        Assert.Equal(100d, evidence.After.Value);
        Assert.Equal(FixedInstant, evidence.Before.ObservedAtUtc);
        Assert.Equal(FixedInstant, evidence.After.ObservedAtUtc);
    }

    [Fact]
    public void ProjectRecovery_UseConsumableWithHpUnchanged_ReturnsNoResourceChangeObserved()
    {
        // Equal Health is also "no gain observed" -- only a rise is evidence of a
        // recovery, and equal fails toward under-claiming (same discipline
        // Project's cost check uses: only a fall is evidence of a cost).
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading before = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceGainConfirmed);
        Assert.Equal(ResourceKind.Health, evidence.ResourceObserved);
        Assert.Equal(100d, evidence.Before.Value);
        Assert.Equal(100d, evidence.After.Value);
    }

    [Fact]
    public void ProjectRecovery_UseConsumableWithHpFalling_ReturnsNoResourceChangeObserved()
    {
        // Falling Health is never a confirmed gain: the slot did not heal (the
        // player may have taken damage in the window). No fabricated gain.
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading before = Vitals(hp: 90, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 70, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.NoResourceChangeObserved, evidence.Result);
        Assert.False(evidence.ResourceGainConfirmed);
    }

    // -- ProjectRecovery: refusal sweeps --------------------------------------

    [Theory]
    [InlineData(CombatActionKind.BasicAttack)]
    [InlineData(CombatActionKind.UseSkill)]
    [InlineData(CombatActionKind.Reposition)]
    [InlineData(CombatActionKind.Flee)]
    public void ProjectRecovery_EveryNonUseConsumableKind_ReturnsUnobservedWithResourceNotObservable_RegardlessOfVitals(CombatActionKind kind)
    {
        CombatActionCandidate candidate = Candidate(kind);
        PlayerVitalsReading before = Vitals(hp: 60, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.Detail);
        Assert.Null(evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.Before.Reason);
        Assert.Equal(CombatVerificationProjector.ResourceNotObservableReason, evidence.After.Reason);
    }

    [Fact]
    public void ProjectRecovery_UseConsumableWithNoBeforeRead_ReturnsUnobservedWithVitalsNotObserved()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before: null, after, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Detail);
        Assert.Equal(ResourceKind.Health, evidence.ResourceObserved);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Before.Reason);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.After.Reason);
    }

    [Fact]
    public void ProjectRecovery_UseConsumableWithNoAfterRead_ReturnsUnobservedWithVitalsNotObserved()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading before = Vitals(hp: 60, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after: null, FixedInstant);

        Assert.Equal(CombatExecutionResult.Unobserved, evidence.Result);
        Assert.Equal(CombatVerificationProjector.VitalsNotObservedReason, evidence.Detail);
        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
    }

    [Fact]
    public void ProjectRecovery_CarriesTheCandidateOnTheEvidence()
    {
        CombatActionCandidate candidate = Candidate(CombatActionKind.UseConsumable);
        PlayerVitalsReading before = Vitals(hp: 60, maxHp: 100, mp: 100, maxMp: 100);
        PlayerVitalsReading after = Vitals(hp: 100, maxHp: 100, mp: 100, maxMp: 100);

        CombatExecutionEvidence evidence =
            CombatVerificationProjector.ProjectRecovery(candidate, before, after, FixedInstant);

        Assert.Same(candidate, evidence.Candidate);
    }
}
