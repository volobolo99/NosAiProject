using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Combat;

public sealed class CombatActionCandidateTests
{
    private static readonly EntityId Target = new("mob-1");
    private static readonly SkillId ExampleSkill = new("skill-1");
    private static readonly ItemId ExampleItem = new("item-1");
    private static readonly WorldPosition SomePosition = new(3f, 4f);

    [Fact]
    public void BasicAttack_RequiresTarget()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(CombatActionKind.BasicAttack));
    }

    [Fact]
    public void BasicAttack_WithTarget_Constructs()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: Target);

        Assert.Equal(Target, candidate.Target);
        Assert.Null(candidate.Skill);
    }

    [Fact]
    public void BasicAttack_RejectsSkill()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(
            CombatActionKind.BasicAttack, target: Target, skill: ExampleSkill));
    }

    [Fact]
    public void UseSkill_RequiresSkill()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(CombatActionKind.UseSkill));
    }

    [Fact]
    public void UseSkill_TargetIsOptional_ForSelfOrAoeSkills()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, skill: ExampleSkill);

        Assert.Null(candidate.Target);
        Assert.Equal(ExampleSkill, candidate.Skill);
    }

    [Fact]
    public void UseSkill_WithTarget_Constructs()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.UseSkill, target: Target, skill: ExampleSkill);

        Assert.Equal(Target, candidate.Target);
    }

    [Fact]
    public void UseSkill_RejectsItem()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(
            CombatActionKind.UseSkill, skill: ExampleSkill, item: ExampleItem));
    }

    [Fact]
    public void UseConsumable_RequiresItem()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(CombatActionKind.UseConsumable));
    }

    [Fact]
    public void UseConsumable_RejectsTarget()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(
            CombatActionKind.UseConsumable, target: Target, item: ExampleItem));
    }

    [Fact]
    public void Reposition_RequiresDestination()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(CombatActionKind.Reposition));
    }

    [Fact]
    public void Reposition_WithDestination_Constructs()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.Reposition, destination: SomePosition);

        Assert.Equal(SomePosition, candidate.Destination);
    }

    [Fact]
    public void Flee_DestinationIsOptional()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.Flee);

        Assert.Null(candidate.Destination);
    }

    [Fact]
    public void Flee_DestinationMayBeGiven()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.Flee, destination: SomePosition);

        Assert.Equal(SomePosition, candidate.Destination);
    }

    [Fact]
    public void Flee_RejectsTarget()
    {
        Assert.Throws<ArgumentException>(() => new CombatActionCandidate(CombatActionKind.Flee, target: Target));
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var first = new CombatActionCandidate(CombatActionKind.BasicAttack, target: Target);
        var second = new CombatActionCandidate(CombatActionKind.BasicAttack, target: Target);

        Assert.Equal(first, second);
    }
}

public sealed class CombatConstraintCheckTests
{
    private static readonly CombatActionCandidate Candidate =
        new(CombatActionKind.UseSkill, skill: new SkillId("skill-1"));

    [Fact]
    public void Allowed_HasNoViolations()
    {
        CombatConstraintCheck check = CombatConstraintCheck.Allowed(Candidate);

        Assert.True(check.IsAllowed);
        Assert.Empty(check.ViolatedConstraints);
    }

    [Fact]
    public void Violated_CarriesTheGivenReasons()
    {
        EquatableArray<string> reasons = EquatableArray<string>.From(new[] { "skill_on_cooldown" });

        CombatConstraintCheck check = CombatConstraintCheck.Violated(Candidate, reasons);

        Assert.False(check.IsAllowed);
        Assert.Equal(reasons, check.ViolatedConstraints);
    }

    [Fact]
    public void Violated_WithNoReasons_Throws()
    {
        Assert.Throws<ArgumentException>(() => CombatConstraintCheck.Violated(Candidate, EquatableArray<string>.Empty));
    }
}

public sealed class ComboPlanTests
{
    [Fact]
    public void Unviable_HasNoSteps()
    {
        ComboPlan plan = ComboPlan.Unviable("no_viable_combo");

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void Unviable_IsViableIsUnknown()
    {
        ComboPlan plan = ComboPlan.Unviable("no_viable_combo");

        Assert.False(plan.IsViable.HasValue);
    }

    [Fact]
    public void Unviable_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        ComboPlan plan = ComboPlan.Unviable("reason", fixedInstant);

        Assert.Equal(fixedInstant, plan.ObservedAtUtc);
        Assert.Equal(fixedInstant, plan.IsViable.ObservedAtUtc);
    }

    [Fact]
    public void RecordEquality_ComparesStepsStructurally()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: new EntityId("mob-1"));
        var steps = EquatableArray<ComboStep>.From(new[]
        {
            new ComboStep(candidate, TimeSpan.FromMilliseconds(500)),
        });

        var first = new ComboPlan(steps, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);
        var second = new ComboPlan(steps, WorldFact<bool>.Derived(true, 1d, fixedInstant), fixedInstant);

        Assert.Equal(first, second);
    }
}

public sealed class CombatSimulationResultTests
{
    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: new EntityId("mob-1"));

        var first = new CombatSimulationResult(candidate, 10d, 5d, TimeSpan.FromMilliseconds(400), 0d, 0d, 0.8, 0d, null);
        var second = new CombatSimulationResult(candidate, 10d, 5d, TimeSpan.FromMilliseconds(400), 0d, 0d, 0.8, 0d, null);

        Assert.Equal(first, second);
    }

    [Fact]
    public void PredictedPositionAfter_IsNullForNonPositionalActs()
    {
        var candidate = new CombatActionCandidate(CombatActionKind.BasicAttack, target: new EntityId("mob-1"));

        var result = new CombatSimulationResult(candidate, 10d, 5d, TimeSpan.FromMilliseconds(400), 0d, 0d, 0.8, 0d, null);

        Assert.Null(result.PredictedPositionAfter);
    }

    [Fact]
    public void PredictedPositionAfter_CanCarryAPositionForReposition()
    {
        var destination = new WorldPosition(1f, 2f);
        var candidate = new CombatActionCandidate(CombatActionKind.Reposition, destination: destination);

        var result = new CombatSimulationResult(candidate, 0d, 0d, TimeSpan.FromMilliseconds(300), 0d, 0d, 0.9, 0d, destination);

        Assert.Equal(destination, result.PredictedPositionAfter);
    }
}
