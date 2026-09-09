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
            ClassifiedValue<WornEquipmentReading>.Unknown("no_equipment_observed_yet")));

        Assert.False(snapshot.Player.Equipment.HasValue);
    }

    [Fact]
    public void ObservedSlots_BecomeEquipmentItems()
    {
        var worn = new List<WornEquipmentSlot> { new(0, 900), new(2, 901) };

        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(worn, EquipmentWireOpcode.Equip), At)));

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
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(worn, EquipmentWireOpcode.Equip), At)));

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
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(new List<WornEquipmentSlot>(), EquipmentWireOpcode.Equip), At)));

        Assert.True(snapshot.Player.Equipment.HasValue);
        Assert.Empty(snapshot.Player.Equipment.Value);
    }

    /// <summary>
    /// <c>equip</c> numbers slots by item slot id, the same space <see cref="EquipmentSlot"/>
    /// documents, so a reading carrying that opcode projects exactly as before.
    /// </summary>
    [Fact]
    public void EquipOpcode_Projects()
    {
        var worn = new List<WornEquipmentSlot> { new(2, 221) };

        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(worn, EquipmentWireOpcode.Equip), At)));

        Assert.True(snapshot.Player.Equipment.HasValue);
        EquipmentItem item = Assert.Single(snapshot.Player.Equipment.Value);
        Assert.Equal(EquipmentSlot.Hat, item.Slot);
        Assert.Equal("221", item.Id.Value);
    }

    /// <summary>
    /// <c>eq</c> numbers slots by equipment-panel position, not by <see cref="EquipmentSlot"/>'s
    /// item-slot space, so it must not be projected: doing so would silently mislabel the item.
    /// </summary>
    [Fact]
    public void EqOpcode_DoesNotProject_AndFailsWithTheDocumentedReason()
    {
        var worn = new List<WornEquipmentSlot> { new(0, 221) };

        WorldModelSnapshot snapshot = Project(WithEquipment(
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(worn, EquipmentWireOpcode.Eq), At)));

        Assert.False(snapshot.Player.Equipment.HasValue);
        Assert.Equal("eq_panel_slot_space_not_mapped", snapshot.Player.Equipment.Reason);
    }

    /// <summary>
    /// The defect this suite pins: capture data/equip_test_20260908_131656.noscap showed the
    /// same item (vnum 221) reported as both <c>eq 0=221</c> and <c>equip 2=221</c>. Casting the
    /// index straight into <see cref="EquipmentSlot"/> regardless of opcode turned it into
    /// Weapon(0) or Hat(2) depending on which packet arrived last. With the opcode carried
    /// alongside the slots, the same index under the two opcodes must not yield the same
    /// projected slot -- here, the <c>eq</c> reading yields no slot at all.
    /// </summary>
    [Fact]
    public void SameIndexUnderTheTwoOpcodes_DoesNotProduceTheSameSlot()
    {
        WorldModelSnapshot fromEq = Project(WithEquipment(
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(new List<WornEquipmentSlot> { new(0, 221) }, EquipmentWireOpcode.Eq), At)));
        WorldModelSnapshot fromEquip = Project(WithEquipment(
            ClassifiedValue<WornEquipmentReading>.Live(
                new WornEquipmentReading(new List<WornEquipmentSlot> { new(0, 221) }, EquipmentWireOpcode.Equip), At)));

        Assert.False(fromEq.Player.Equipment.HasValue);
        Assert.True(fromEquip.Player.Equipment.HasValue);
        Assert.Equal(EquipmentSlot.Weapon, Assert.Single(fromEquip.Player.Equipment.Value).Slot);
    }

    private static WorldModelSnapshot Project(GameplayObservation observation) =>
        GameplayObservationProjector.Project(observation, new EntityId("player-1"), version: 1, At);

    private static GameplayObservation WithEquipment(
        ClassifiedValue<WornEquipmentReading> equipment) =>
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
