using Xunit;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Tests;

/// <summary>
/// What an action can be aimed at, and the pairings the type refuses to express.
/// </summary>
/// <remarks>
/// <para>
/// F2-1. The target used to be a string and two integers: <c>"TARGET_MOB_01"</c>
/// at a constant <c>125, 85</c>, <c>"WAYPOINT_A"</c> at <c>130, 90</c>. Nothing
/// checked them, every caller read them its own way, and an effector connected to
/// that would have acted on targets that do not exist.
/// </para>
/// <para>
/// These pin the two things the type is for: a candidate cannot be built with a
/// target its action cannot use, and an entity that was never identified says so
/// rather than passing for a real one.
/// </para>
/// </remarks>
public sealed class ActionTargetTests
{
    private static ActionCandidate Build(ActionType type, ActionTarget target) => new(
        Guid.NewGuid(), type, target, 0, TrustTier.Tier1_Assisted, "test");

    // ------------------------------------------------- the pairings that hold

    [Theory]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.TargetEntity)]
    [InlineData(ActionType.UseSkill)]
    public void An_action_against_an_entity_takes_an_entity(ActionType type)
        => Assert.IsType<ActionTarget.Entity>(
            Build(type, new ActionTarget.Entity(101, new MapPoint(10, 10))).Target);

    [Theory]
    [InlineData(ActionType.MoveToPosition)]
    [InlineData(ActionType.EmergencyFlee)]
    public void An_action_that_goes_somewhere_takes_a_place(ActionType type)
        => Assert.IsType<ActionTarget.Position>(
            Build(type, new ActionTarget.Position(new MapPoint(130, 90))).Target);

    [Fact]
    public void A_consumable_takes_the_slot_it_sits_in()
        => Assert.Equal(
            4,
            Assert.IsType<ActionTarget.InventorySlot>(
                Build(ActionType.UseConsumable, new ActionTarget.InventorySlot(4)).Target).Slot);

    // -------------------------------------------- the pairings it will not build

    /// <summary>
    /// The candidate this card exists to make unbuildable. It used to be one
    /// string away.
    /// </summary>
    [Theory]
    [InlineData(ActionType.UseBasicAttack)]
    [InlineData(ActionType.TargetEntity)]
    [InlineData(ActionType.UseSkill)]
    public void An_attack_on_nothing_cannot_be_constructed(ActionType type)
        => Assert.Throws<ArgumentException>(() => Build(type, ActionTarget.None.Instance));

    /// <summary>
    /// An entity is not a destination: it moves, and the point clicked would be
    /// where it used to be.
    /// </summary>
    [Fact]
    public void A_move_cannot_be_aimed_at_an_entity()
        => Assert.Throws<ArgumentException>(
            () => Build(ActionType.MoveToPosition, new ActionTarget.Entity(101)));

    [Fact]
    public void An_attack_cannot_be_aimed_at_an_inventory_slot()
        => Assert.Throws<ArgumentException>(
            () => Build(ActionType.UseBasicAttack, new ActionTarget.InventorySlot(1)));

    [Fact]
    public void A_consumable_cannot_be_aimed_at_a_place()
        => Assert.Throws<ArgumentException>(
            () => Build(ActionType.UseConsumable, new ActionTarget.Position(new MapPoint(1, 1))));

    [Fact]
    public void A_null_target_is_refused_rather_than_treated_as_none()
        => Assert.Throws<ArgumentNullException>(
            () => Build(ActionType.RestAndRecover, null!));

    // --------------------------------------------------- identified or not

    /// <summary>
    /// The planner knows that there is a target and not which one: ADR-0018
    /// establishes the flag from the screen, and choosing the entity is F2-2. The
    /// difference is visible in the type rather than hidden in a placeholder
    /// string that reads like a real name.
    /// </summary>
    [Fact]
    public void An_unidentified_entity_says_so()
    {
        ActionTarget.Entity target = ActionTarget.Entity.Unidentified;

        Assert.False(target.IsResolved);
        Assert.Null(target.At);
    }

    /// <summary>
    /// Negative so it cannot collide with a real id from the wire, and never
    /// zero, which is the controlled player by the channel's convention.
    /// </summary>
    [Fact]
    public void The_unresolved_id_cannot_collide_with_a_real_entity()
    {
        Assert.True(ActionTarget.Entity.Unresolved < 0);
        Assert.NotEqual(0, ActionTarget.Entity.Unresolved);
        Assert.True(new ActionTarget.Entity(0).IsResolved);
        Assert.True(new ActionTarget.Entity(313816).IsResolved);
    }

    /// <summary>
    /// An entity can be known without its position, for the reason
    /// <c>EntitySighting.HpRatio</c> can be absent: the wire routinely reports one
    /// half of an entity without the other.
    /// </summary>
    [Fact]
    public void An_entity_may_be_identified_without_a_position()
    {
        var target = new ActionTarget.Entity(313816);

        Assert.True(target.IsResolved);
        Assert.Null(target.At);
    }
}
