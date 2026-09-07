using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;
using RuntimeDataSourceKind = NosAi.Runtime.Contracts.DataSourceKind;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

public sealed class GameplayObservationProjectorTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly EntityId PlayerId = new("player-1");

    [Fact]
    public void UnobservedGameplay_ProjectsEverythingAsExplicitlyUnknown_NeverAsZeroOrEmptyDefaults()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("gameplay_provider_not_available", Now);

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Assert.False(snapshot.Player.Position.HasValue);
        Assert.False(snapshot.Player.CurrentMap.HasValue);
        Assert.False(snapshot.Player.IsAlive.HasValue);
        Assert.False(snapshot.Map.Name.HasValue);
        Assert.Equal(GameplayObservationProjector.UnknownMapSentinelId, snapshot.Map.Id.Value);
        Assert.Empty(snapshot.Player.Inventory);
        Assert.Empty(snapshot.Drops);
        Assert.Empty(snapshot.Player.Cooldowns);
    }

    [Fact]
    public void KnownVitalsAndPosition_ProjectIntoPlayerState_WithSourceAndTimestampPreserved()
    {
        GameplayObservation observation = new(
            Hp: ClassifiedValue<int>.Live(80, Now),
            MaxHp: ClassifiedValue<int>.Live(100, Now),
            Mp: ClassifiedValue<int>.Live(40, Now),
            MaxMp: ClassifiedValue<int>.Live(50, Now),
            HasTarget: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
            InCombat: ClassifiedValue<bool>.Unknown("not_published_by_provider"),
            EntitiesInView: ClassifiedValue<int>.Unknown("no_entities_reported"),
            ObservedAtUtc: Now)
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(10, 20), Now),
            MapId = ClassifiedValue<int>.Live(5, Now),
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 3, Now);

        Assert.True(snapshot.Player.Position.HasValue);
        Assert.Equal(new WorldPosition(10, 20), snapshot.Player.Position.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, snapshot.Player.Position.Source);

        Assert.True(snapshot.Player.CurrentMap.HasValue);
        Assert.Equal("map-5", snapshot.Player.CurrentMap.Value.Value);
        Assert.Equal("map-5", snapshot.Map.Id.Value);

        Assert.True(snapshot.Player.IsAlive.HasValue);
        Assert.True(snapshot.Player.IsAlive.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, snapshot.Player.IsAlive.Source);

        Resource health = Assert.Single(snapshot.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(80d, health.Current.Value);
        Assert.Equal(100d, health.Maximum.Value);
    }

    [Fact]
    public void ZeroHp_DerivesNotAlive()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Hp = ClassifiedValue<int>.Live(0, Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Assert.True(snapshot.Player.IsAlive.HasValue);
        Assert.False(snapshot.Player.IsAlive.Value);
    }

    [Fact]
    public void InventorySlots_ProjectIntoInventoryItems_WithUnknownNameButRealVnumAndQuantity()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            Inventory = ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Live(
                new[] { new InventorySlotReading(InventoryKind: 2, Slot: 0, Vnum: 1234, Amount: 5, Rarity: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        InventoryItem item = Assert.Single(snapshot.Player.Inventory);
        Assert.Equal("1234", item.Id.Value);
        Assert.False(item.Name.HasValue);
        Assert.Equal(5, item.Quantity.Value);
        Assert.Equal(0, item.SlotIndex.Value);
    }

    [Fact]
    public void GroundItems_ProjectIntoDrops()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            GroundItems = ClassifiedValue<IReadOnlyList<GroundItem>>.Live(
                new[] { new GroundItem(Vnum: 42, DropId: 99, X: 3, Y: 4, Amount: 1, OwnerId: 0, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Drop drop = Assert.Single(snapshot.Drops);
        Assert.Equal("42", drop.Item.Value);
        Assert.Equal(new WorldPosition(3, 4), drop.Position.Value);
        Assert.Equal(1, drop.Quantity.Value);
    }

    [Fact]
    public void SkillsReady_ProjectIntoZeroRemainingCooldowns()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            SkillsReady = ClassifiedValue<IReadOnlyList<SkillReady>>.Live(
                new[] { new SkillReady(Slot: 2, Now, RuntimeDataSourceKind.Live) },
                Now)
        };

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Cooldown cooldown = Assert.Single(snapshot.Player.Cooldowns);
        Assert.Equal("2", cooldown.SkillId.Value);
        Assert.False(cooldown.IsActive);
    }

    /// <summary>
    /// Quests, equipment and skills have no observation channel at all; Mobs
    /// and Npcs now have one, but it needs both an observed entity list and a
    /// catalogue classifier, and this observation supplies neither. The
    /// classified cases live in <c>GameplayObservationEntityProjectionTests</c>.
    /// </summary>
    [Fact]
    public void MobsAndNpcsAndQuests_StayEmpty_WhenNothingIsObservedAndNoClassifierIsSupplied()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now);

        WorldModelSnapshot snapshot = GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);

        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
        Assert.Empty(snapshot.Quests);
        Assert.Empty(snapshot.Player.Equipment);
        Assert.Empty(snapshot.Player.Skills);
    }

    [Fact]
    public void TwoProjectionsFromTheSameObservation_AreEqual()
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(1, 1), Now),
            MapId = ClassifiedValue<int>.Live(7, Now),
        };

        WorldModelSnapshot a = GameplayObservationProjector.Project(observation, PlayerId, version: 2, Now);
        WorldModelSnapshot b = GameplayObservationProjector.Project(observation, PlayerId, version: 2, Now);

        Assert.Equal(a, b);
    }
}
