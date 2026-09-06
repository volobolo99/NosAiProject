using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Combat;

public sealed class CombatExecutionEvidenceTests
{
    private static readonly EntityId Target = new("mob-1");
    private static readonly SkillId ExampleSkill = new("skill-1");

    private static readonly CombatActionCandidate SkillCandidate =
        new(CombatActionKind.UseSkill, target: Target, skill: ExampleSkill);

    private static readonly CombatActionCandidate BasicAttackCandidate =
        new(CombatActionKind.BasicAttack, target: Target);

    [Fact]
    public void NotAttempted_ResultIsAborted()
    {
        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "guard_refused");

        Assert.Equal(CombatExecutionResult.Aborted, evidence.Result);
    }

    [Fact]
    public void NotAttempted_BeforeAndAfterAreUnknown()
    {
        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "guard_refused");

        Assert.False(evidence.Before.HasValue);
        Assert.False(evidence.After.HasValue);
    }

    [Fact]
    public void NotAttempted_ResourceObservedIsNull()
    {
        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "guard_refused");

        Assert.Null(evidence.ResourceObserved);
    }

    [Fact]
    public void NotAttempted_DetailCarriesTheReason()
    {
        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "guard_refused");

        Assert.Equal("guard_refused", evidence.Detail);
    }

    [Fact]
    public void NotAttempted_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "reason", fixedInstant);

        Assert.Equal(fixedInstant, evidence.ObservedAtUtc);
        Assert.Equal(fixedInstant, evidence.Before.ObservedAtUtc);
        Assert.Equal(fixedInstant, evidence.After.ObservedAtUtc);
    }

    [Fact]
    public void NotAttempted_ResourceCostConfirmedIsFalse()
    {
        CombatExecutionEvidence evidence = CombatExecutionEvidence.NotAttempted(SkillCandidate, "reason");

        Assert.False(evidence.ResourceCostConfirmed);
    }

    [Fact]
    public void ResourceCostConfirmed_TrueOnlyForThatResult()
    {
        DateTime now = DateTime.UnixEpoch;
        var confirmed = new CombatExecutionEvidence(
            SkillCandidate,
            ResourceKind.Mana,
            WorldFact<double>.Live(40d, confidence: 1d, now),
            WorldFact<double>.Live(25d, confidence: 1d, now),
            CombatExecutionResult.ResourceCostConfirmed,
            Detail: null,
            now);

        var noChange = confirmed with { Result = CombatExecutionResult.NoResourceChangeObserved };

        Assert.True(confirmed.ResourceCostConfirmed);
        Assert.False(noChange.ResourceCostConfirmed);
    }

    [Fact]
    public void ResourceGainConfirmed_TrueOnlyForThatResult()
    {
        DateTime now = DateTime.UnixEpoch;
        var gained = new CombatExecutionEvidence(
            SkillCandidate,
            ResourceKind.Health,
            WorldFact<double>.Live(20d, confidence: 1d, now),
            WorldFact<double>.Live(45d, confidence: 1d, now),
            CombatExecutionResult.ResourceGainConfirmed,
            Detail: null,
            now);

        var noChange = gained with { Result = CombatExecutionResult.NoResourceChangeObserved };

        Assert.True(gained.ResourceGainConfirmed);
        Assert.False(noChange.ResourceGainConfirmed);
        Assert.False(gained.ResourceCostConfirmed);
    }

    [Fact]
    public void BasicAttack_HasNoObservableResource_ResourceObservedIsNull()
    {
        DateTime now = DateTime.UnixEpoch;
        var evidence = new CombatExecutionEvidence(
            BasicAttackCandidate,
            ResourceObserved: null,
            WorldFact<double>.Unknown("resource_kind_not_observable_for_kind", now),
            WorldFact<double>.Unknown("resource_kind_not_observable_for_kind", now),
            CombatExecutionResult.Unobserved,
            "resource_kind_not_observable_for_kind",
            now);

        Assert.Null(evidence.ResourceObserved);
        Assert.False(evidence.ResourceCostConfirmed);
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        WorldFact<double> before = WorldFact<double>.Live(40d, 1d, fixedInstant);
        WorldFact<double> after = WorldFact<double>.Live(25d, 1d, fixedInstant);

        var first = new CombatExecutionEvidence(
            SkillCandidate, ResourceKind.Mana, before, after, CombatExecutionResult.ResourceCostConfirmed, null, fixedInstant);
        var second = new CombatExecutionEvidence(
            SkillCandidate, ResourceKind.Mana, before, after, CombatExecutionResult.ResourceCostConfirmed, null, fixedInstant);

        Assert.Equal(first, second);
    }
}
