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

    /// <summary>
    /// The snapshot that knows nothing asserts nothing about the character's
    /// four collections either.
    /// </summary>
    /// <remarks>
    /// It used to. Every <see cref="WorldFact{T}"/> on the player was
    /// <c>Unknown(reason)</c> while <c>Skills</c>, <c>Cooldowns</c>,
    /// <c>Inventory</c> and <c>Equipment</c> were empty arrays -- four positive
    /// claims, in the one object whose name says it has no information: this
    /// character has no abilities, nothing on cooldown, nothing carried and
    /// nothing worn. <c>docs/adr/ADR-0027</c>.
    /// </remarks>
    [Fact]
    public void Unknown_DoesNotClaimTheCharacterHasNoSkillsOrEquipment()
    {
        var snapshot = WorldModelSnapshot.Unknown("no_fusion_cycle_yet");

        Assert.False(snapshot.Player.Skills.HasValue);
        Assert.False(snapshot.Player.Cooldowns.HasValue);
        Assert.False(snapshot.Player.Inventory.HasValue);
        Assert.False(snapshot.Player.Equipment.HasValue);

        Assert.Equal("no_fusion_cycle_yet", snapshot.Player.Skills.Reason);
        Assert.Equal("no_fusion_cycle_yet", snapshot.Player.Equipment.Reason);
    }

    /// <summary>
    /// An Unknown fact's value is <c>default(T)</c>, and for an
    /// <see cref="EquatableArray{T}"/> that is a struct whose backing
    /// <c>ImmutableArray</c> is itself default -- a state in which every one of
    /// its members used to throw. Hashing or comparing a snapshot holding one
    /// must not.
    /// </summary>
    [Fact]
    public void ASnapshotHoldingUnknownCollections_CanBeHashedAndCompared()
    {
        // Lo stesso istante per entrambe: senza, Unknown si timbra con UtcNow e
        // le due differirebbero legittimamente sul tempo, non sulle collezioni.
        var a = WorldModelSnapshot.Unknown("no_fusion_cycle_yet", Now);
        var b = WorldModelSnapshot.Unknown("no_fusion_cycle_yet", Now);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a, b);
        Assert.Empty(a.Player.Skills.Value);
        Assert.Empty(a.Player.Inventory.Value);
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
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, Now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, Now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, Now));

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
