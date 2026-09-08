using System;
using System.Collections.Generic;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The wire decoded the worn set all along, but nothing carried it to the World Model, so
/// <c>Player.Equipment</c> was permanently Unknown. These tests pin the route that now exists,
/// and above all the distinction the whole namespace is built on: a set read and found empty
/// is not the same fact as a set never read.
/// </summary>
public sealed class EquipmentProjectionTests
{
    private static readonly DateTime At = DateTime.UnixEpoch;

    [Fact]
    public void EquipmentNeverPublished_StaysUnknownOnThePlayer()
    {
        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<IReadOnlyList<WornEquipmentSlot>>.Unknown("no_equipment_observed_yet")));

        Assert.False(snapshot.Player.Equipment.HasValue);
    }

    [Fact]
    public void ObservedSlots_BecomeEquipmentItems()
    {
        var worn = new List<WornEquipmentSlot> { new(0, 900), new(2, 901) };

        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<IReadOnlyList<WornEquipmentSlot>>.Live(worn, At)));

        Assert.True(snapshot.Player.Equipment.HasValue);
        Assert.Equal(2, snapshot.Player.Equipment.Value.Count);
        Assert.Contains(snapshot.Player.Equipment.Value, i => i.Slot == EquipmentSlot.Weapon && i.Id.Value == "900");
        Assert.Contains(snapshot.Player.Equipment.Value, i => i.Slot == EquipmentSlot.Hat && i.Id.Value == "901");
    }

    /// <summary>
    /// The positional index to slot mapping is a reading of the packet, not something the
    /// protocol document confirms, so an index the enum does not know produces no item rather
    /// than a guessed one.
    /// </summary>
    [Fact]
    public void ASlotOutsideTheEnum_ProducesNoItem()
    {
        var worn = new List<WornEquipmentSlot> { new(0, 900), new(99, 902) };

        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<IReadOnlyList<WornEquipmentSlot>>.Live(worn, At)));

        Assert.True(snapshot.Player.Equipment.HasValue);
        Assert.Single(snapshot.Player.Equipment.Value);
    }

    /// <summary>
    /// A character observed wearing nothing is a fact; a character nobody looked at is not.
    /// Collapsing the two would let a planner conclude the build needs no attention.
    /// </summary>
    [Fact]
    public void AnObservedEmptySet_IsReadNotUnread()
    {
        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<IReadOnlyList<WornEquipmentSlot>>.Live(new List<WornEquipmentSlot>(), At)));

        Assert.True(snapshot.Player.Equipment.HasValue);
        Assert.Empty(snapshot.Player.Equipment.Value);
    }

    private static WorldModelSnapshot Project(GameplayObservation observation) =>
        GameplayObservationProjector.Project(observation, new EntityId("player-1"), version: 1, At);

    private static GameplayObservation WithEquipment(
        ClassifiedValue<IReadOnlyList<WornEquipmentSlot>> equipment) =>
        new(
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<int>.Derived(0, At),
            At)
        {
            Equipment = equipment,
        };
}
