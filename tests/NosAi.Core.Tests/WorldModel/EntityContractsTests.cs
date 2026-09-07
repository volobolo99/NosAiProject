using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class EntityContractsTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CombatantStatus_Empty_HasNoResourcesOrEffects()
    {
        Assert.Empty(CombatantStatus.Empty.Resources);
        Assert.Empty(CombatantStatus.Empty.ActiveEffects);
    }

    [Fact]
    public void Mob_UnobservedFields_AreExplicitlyUnknown_NeverFriendlyByDefault()
    {
        var mob = new Mob(
            new EntityId("mob-1"),
            WorldFact<WorldPosition>.Unknown("out_of_screen_capture_bounds"),
            WorldFact<string>.Unknown("species_not_identified"),
            WorldFact<bool>.Unknown("hostility_not_observed"),
            WorldFact<bool>.Unknown("alive_state_not_observed"),
            CombatantStatus.Empty);

        Assert.False(mob.IsHostile.HasValue);
        Assert.False(mob.IsAlive.HasValue);
    }

    [Fact]
    public void TwoPlayersBuiltFromIdenticalInputs_AreEqual()
    {
        Player Build() => new(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(1, 2), 1.0, Now),
            WorldFact<float>.Live(0f, 1.0, Now),
            WorldFact<bool>.Live(true, 1.0, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1.0, Now),
            new CombatantStatus(
                EquatableArray<Resource>.From(new[] { new Resource(ResourceKind.Health, WorldFact<double>.Live(100, 1.0, Now), WorldFact<double>.Live(100, 1.0, Now)) }),
                EquatableArray<StatusEffect>.Empty),
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, Now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, Now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, Now));

        Assert.Equal(Build(), Build());
    }

    [Fact]
    public void Drop_CarriesItemIdentityAndQuantity()
    {
        var drop = new Drop(new EntityId("drop-1"), new ItemId("potion"), WorldFact<WorldPosition>.Live(new WorldPosition(5, 5), 1.0, Now), WorldFact<int>.Live(3, 1.0, Now));

        Assert.Equal(new ItemId("potion"), drop.Item);
        Assert.Equal(3, drop.Quantity.Value);
    }
}
