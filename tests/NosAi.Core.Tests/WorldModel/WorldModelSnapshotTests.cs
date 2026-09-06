using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldModelSnapshotTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unknown_HasNoFabricatedPlayerOrMap_AndEmptyCollections()
    {
        var snapshot = WorldModelSnapshot.Unknown("no_fusion_cycle_yet");

        Assert.False(snapshot.Player.Position.HasValue);
        Assert.False(snapshot.Player.CurrentMap.HasValue);
        Assert.False(snapshot.Map.Name.HasValue);
        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
        Assert.Empty(snapshot.Drops);
        Assert.Empty(snapshot.Quests);
        Assert.Empty(snapshot.RecentActions);
        Assert.Empty(snapshot.ActiveGoals);
        Assert.Equal(0, snapshot.Version);
    }

    private static WorldModelSnapshot BuildFusedSnapshot()
    {
        var player = new Player(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Live(new WorldPosition(12, 34), 1.0, Now),
            WorldFact<float>.Live(0f, 1.0, Now),
            WorldFact<bool>.Live(true, 1.0, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1.0, Now),
            new CombatantStatus(
                EquatableArray<Resource>.From(new[] { new Resource(ResourceKind.Health, WorldFact<double>.Live(100, 1.0, Now), WorldFact<double>.Live(100, 1.0, Now)) }),
                EquatableArray<StatusEffect>.Empty),
            EquatableArray<Skill>.Empty,
            EquatableArray<Cooldown>.Empty,
            EquatableArray<InventoryItem>.Empty,
            EquatableArray<EquipmentItem>.Empty);

        var map = new MapModel(
            new MapId("map-1"),
            WorldFact<string>.Live("Fernon Field", 1.0, Now),
            WorldFact<MapBounds>.Live(new MapBounds(new TileCoordinate(0, 0), new TileCoordinate(50, 50)), 1.0, Now),
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 3,
            Now);

        var mobs = EquatableArray<Mob>.From(new[]
        {
            new Mob(new EntityId("mob-1"), WorldFact<WorldPosition>.Live(new WorldPosition(20, 20), 0.9, Now), WorldFact<string>.Live("Wolf", 0.9, Now), WorldFact<bool>.Live(true, 0.9, Now), WorldFact<bool>.Live(true, 0.9, Now), CombatantStatus.Empty)
        });

        return new WorldModelSnapshot(
            Version: 7,
            Now,
            player,
            map,
            mobs,
            EquatableArray<Npc>.Empty,
            EquatableArray<Drop>.Empty,
            EquatableArray<Quest>.Empty,
            EquatableArray<WorldAction>.Empty,
            EquatableArray<Goal>.Empty);
    }

    [Fact]
    public void TwoSnapshotsBuiltFromTheSameFusedInputs_AreEqual()
    {
        WorldModelSnapshot a = BuildFusedSnapshot();
        WorldModelSnapshot b = BuildFusedSnapshot();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ChangingOneNestedFact_BreaksEquality()
    {
        WorldModelSnapshot a = BuildFusedSnapshot();
        WorldModelSnapshot b = a with
        {
            Mobs = EquatableArray<Mob>.From(new[]
            {
                new Mob(new EntityId("mob-1"), WorldFact<WorldPosition>.Live(new WorldPosition(99, 99), 0.9, Now), WorldFact<string>.Live("Wolf", 0.9, Now), WorldFact<bool>.Live(true, 0.9, Now), WorldFact<bool>.Live(true, 0.9, Now), CombatantStatus.Empty)
            })
        };

        Assert.NotEqual(a, b);
    }
}
