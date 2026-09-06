using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Loadout;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Loadout;

public sealed class LoadoutActionCandidateTests
{
    private static readonly ItemId Item = new("sword-1");

    [Fact]
    public void Equip_RequiresItemAndSlot()
    {
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Equip));
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Equip, item: Item));
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Equip, slot: EquipmentSlot.Weapon));
    }

    [Fact]
    public void Equip_WithItemAndSlot_Constructs()
    {
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: Item, slot: EquipmentSlot.Weapon);

        Assert.Equal(Item, candidate.Item);
        Assert.Equal(EquipmentSlot.Weapon, candidate.Slot);
    }

    [Fact]
    public void Unequip_RequiresSlotOnly()
    {
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Unequip));
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Unequip, item: Item, slot: EquipmentSlot.Weapon));
    }

    [Fact]
    public void Unequip_WithSlot_Constructs()
    {
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Unequip, slot: EquipmentSlot.Helmet);

        Assert.Null(candidate.Item);
        Assert.Equal(EquipmentSlot.Helmet, candidate.Slot);
    }

    [Fact]
    public void Upgrade_RequiresItemOnly()
    {
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Upgrade));
        Assert.Throws<ArgumentException>(() => new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: Item, slot: EquipmentSlot.Weapon));
    }

    [Fact]
    public void Upgrade_WithItem_Constructs()
    {
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: Item);

        Assert.Equal(Item, candidate.Item);
        Assert.Null(candidate.Slot);
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var first = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: Item, slot: EquipmentSlot.Weapon);
        var second = new LoadoutActionCandidate(LoadoutActionKind.Equip, item: Item, slot: EquipmentSlot.Weapon);

        Assert.Equal(first, second);
    }
}

public sealed class LoadoutConstraintCheckTests
{
    private static readonly LoadoutActionCandidate Candidate =
        new(LoadoutActionKind.Unequip, slot: EquipmentSlot.Weapon);

    [Fact]
    public void Allowed_HasNoViolations()
    {
        LoadoutConstraintCheck check = LoadoutConstraintCheck.Allowed(Candidate);

        Assert.True(check.IsAllowed);
        Assert.Empty(check.ViolatedConstraints);
    }

    [Fact]
    public void Violated_CarriesTheGivenReasons()
    {
        EquatableArray<string> reasons = EquatableArray<string>.From(new[] { "slot_already_empty" });

        LoadoutConstraintCheck check = LoadoutConstraintCheck.Violated(Candidate, reasons);

        Assert.False(check.IsAllowed);
        Assert.Equal(reasons, check.ViolatedConstraints);
    }

    [Fact]
    public void Violated_WithNoReasons_Throws()
    {
        Assert.Throws<ArgumentException>(() => LoadoutConstraintCheck.Violated(Candidate, EquatableArray<string>.Empty));
    }
}

public sealed class LoadoutEvaluationTests
{
    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        var candidate = new LoadoutActionCandidate(LoadoutActionKind.Upgrade, item: new ItemId("sword-1"));

        var first = new LoadoutEvaluation(candidate, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var second = new LoadoutEvaluation(candidate, 1, 2, 3, 4, 5, 6, 7, 8, 9);

        Assert.Equal(first, second);
    }
}
