using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The prediction step reads which ability it is predicting for, and says so
/// when it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <c>SimulationEngine.Simulate</c> assigned <c>mpDelta = -35</c> and
/// <c>risk = currentMp &lt; 35 ? 0.90f : 0.10f</c> to every
/// <see cref="ActionType.UseSkill"/> candidate, whichever ability it was:
/// <c>SkillOrItemId</c> appeared nowhere in the file. That number was not
/// advisory. <see cref="GuardPolicyEngine"/>'s one quantitative refusal keys on
/// the risk computed from it, so an ability whose real cost is far above 35 was
/// predicted affordable, scored 0.10, and authorised by the safety gate.
/// </para>
/// <para>
/// Nothing here replaces 35 with a better guess. The cost comes from the caller
/// or it does not come, and a prediction resting on no measured cost carries the
/// reason rather than a number -- which is what lets the policy engine, and not
/// the simulation, decide what such a prediction may authorise.
/// </para>
/// </remarks>
public sealed class SimulationCostTests
{
    private static readonly ActionTarget.Entity SomeMob = new(101, new MapPoint(10, 10));

    private static ActionCandidate Skill(int skillId) => new(
        Guid.NewGuid(), ActionType.UseSkill, SomeMob, skillId, TrustTier.Tier2_SemiAutonomous, "test");

    /// <summary>
    /// With no cost source, a skill prediction reports itself unmeasured instead
    /// of reporting the affordable case's risk.
    /// </summary>
    [Fact]
    public void WithNoCostSource_ASkillPredictionIsUnmeasured()
    {
        PredictedOutcome outcome = new SimulationEngine().Simulate(Skill(201), currentHp: 1000, currentMp: 1000, maxHp: 1000);

        Assert.False(outcome.IsMeasured);
        Assert.Equal(SimulationEngine.SkillCostNotMeasuredReason, outcome.UnmeasuredReason);
        Assert.Equal(0, outcome.ExpectedMpDelta);
    }

    /// <summary>
    /// The assertion that proves the engine stopped ignoring the candidate: two
    /// abilities with different costs produce different predictions.
    /// </summary>
    /// <remarks>
    /// Before the cost source existed this test could not have been written --
    /// not because it would have failed, but because every input produced the
    /// same output, so there was nothing for it to distinguish.
    /// </remarks>
    [Fact]
    public void TwoAbilitiesWithDifferentCosts_PredictDifferently()
    {
        var engine = new SimulationEngine(id => id switch
        {
            201 => new SkillCost(MpCost: 20, CastTimeMs: 500),
            202 => new SkillCost(MpCost: 400, CastTimeMs: 2000),
            _ => null,
        });

        PredictedOutcome cheap = engine.Simulate(Skill(201), currentHp: 1000, currentMp: 100, maxHp: 1000);
        PredictedOutcome dear = engine.Simulate(Skill(202), currentHp: 1000, currentMp: 100, maxHp: 1000);

        Assert.True(cheap.IsMeasured);
        Assert.True(dear.IsMeasured);

        Assert.Equal(-20, cheap.ExpectedMpDelta);
        Assert.Equal(-400, dear.ExpectedMpDelta);
        Assert.Equal(500, cheap.ExpectedTimeMs);
        Assert.Equal(2000, dear.ExpectedTimeMs);
    }

    /// <summary>
    /// The case the old constant hid: an ability the character cannot afford.
    /// With 100 MP and a 400 MP cost the risk is high; the old model saw 100 MP
    /// against its assumed 35 and called it 0.10.
    /// </summary>
    [Fact]
    public void AnAbilityTheCharacterCannotAfford_IsPredictedRisky_NotSafe()
    {
        var engine = new SimulationEngine(_ => new SkillCost(MpCost: 400, CastTimeMs: 2000));

        PredictedOutcome outcome = engine.Simulate(Skill(202), currentHp: 1000, currentMp: 100, maxHp: 1000);

        Assert.True(outcome.RiskScore > 0.75f);
        Assert.Equal(0f, outcome.SuccessProbability);
        Assert.True(100 > SimulationEngine.UnmeasuredSkillMpAssumption,
            "the point of this test is that the old assumption would have called this affordable");
    }

    /// <summary>
    /// The safety consequence, end to end: the gate refuses a prediction that
    /// rests on no measured cost.
    /// </summary>
    [Fact]
    public void TheGuardRefusesAnUnmeasuredPrediction()
    {
        ActionCandidate candidate = Skill(201);
        PredictedOutcome outcome = new SimulationEngine().Simulate(candidate, currentHp: 1000, currentMp: 1000, maxHp: 1000);

        GuardEvaluationResult verdict = new GuardPolicyEngine().Evaluate(candidate, outcome, RuntimeMode.Normal);

        Assert.False(verdict.IsAllowedByPolicy);
        Assert.Contains(verdict.ViolatedConstraints, v => v.Contains(SimulationEngine.SkillCostNotMeasuredReason, StringComparison.Ordinal));
    }

    /// <summary>
    /// And permits the same act once the cost is known and affordable, so the
    /// refusal above is about the missing measurement and not about skills.
    /// </summary>
    [Fact]
    public void TheGuardPermitsTheSameActOnceTheCostIsMeasuredAndAffordable()
    {
        ActionCandidate candidate = Skill(201);
        var engine = new SimulationEngine(_ => new SkillCost(MpCost: 20, CastTimeMs: 500));
        PredictedOutcome outcome = engine.Simulate(candidate, currentHp: 1000, currentMp: 1000, maxHp: 1000);

        GuardEvaluationResult verdict = new GuardPolicyEngine().Evaluate(candidate, outcome, RuntimeMode.Normal);

        Assert.True(verdict.IsAllowedByPolicy);
    }

    /// <summary>
    /// The act that gets the character out is not refused for lack of data about
    /// what it costs -- the same exemption the risk threshold below it already
    /// makes.
    /// </summary>
    [Fact]
    public void AnEmergencyFleeIsNotRefusedForBeingUnmeasured()
    {
        var candidate = new ActionCandidate(
            Guid.NewGuid(), ActionType.EmergencyFlee, new ActionTarget.Position(new MapPoint(0, 0)),
            0, TrustTier.Tier1_Assisted, "flee");
        var unmeasured = new PredictedOutcome(
            candidate.CandidateId, 0, 0, 500, 0.9f, 0.1f, "SIG",
            SimulationEngine.SkillCostNotMeasuredReason);

        GuardEvaluationResult verdict = new GuardPolicyEngine().Evaluate(candidate, unmeasured, RuntimeMode.Normal);

        Assert.True(verdict.IsAllowedByPolicy);
    }

    /// <summary>
    /// Every other action type keeps predicting exactly as it did: this change
    /// is about the one quantity a safety threshold reads.
    /// </summary>
    [Theory]
    [InlineData(ActionType.MoveToPosition)]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.EmergencyFlee)]
    public void NonSkillPredictionsStayMeasured(ActionType type)
    {
        ActionTarget target = type == ActionType.UseBasicAttack
            ? SomeMob
            : new ActionTarget.Position(new MapPoint(10, 10));
        var candidate = new ActionCandidate(
            Guid.NewGuid(), type, target, 0, TrustTier.Tier1_Assisted, "test");

        PredictedOutcome outcome = new SimulationEngine().Simulate(candidate, currentHp: 1000, currentMp: 100, maxHp: 1000);

        Assert.True(outcome.IsMeasured);
    }
}
