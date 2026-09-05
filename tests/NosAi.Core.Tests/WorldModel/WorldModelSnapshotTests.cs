using System.Collections.Immutable;
using NosAi.Core.WorldModel;
using Xunit;
using static NosAi.Core.Tests.WorldModel.WorldModelFixtures;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldModelSnapshotTests
{
    [Fact]
    public void EmptySnapshotIsAllUnknownAndValid()
    {
        var empty = WorldModelSnapshot.Empty(T0);

        Assert.Equal(WorldModelContract.SchemaVersion, empty.SchemaVersion);
        Assert.Equal(0, empty.Revision);
        Assert.Equal(T0, empty.ObservedAtUnixMillis);
        Assert.Empty(empty.Validate());
        Assert.Equal(0, empty.KnownFactCount);
        Assert.Equal(15, empty.UnknownFactCount);
        Assert.All(empty.Facts(), f =>
        {
            Assert.False(f.HasValue);
            Assert.Equal(WorldModelContract.NotObservedReason, f.Reason);
            Assert.Equal(T0, f.ObservedAtUnixMillis);
        });
        Assert.False(empty.Player.HasVitals);
        Assert.Null(empty.Player.HpRatio);
        Assert.False(empty.HasSimulatedFact);
        Assert.True(empty.IsActionable);
        Assert.False(empty.AreVitalsFreshAt(T0, 10_000));
    }

    [Fact]
    public void PopulatedFixtureIsValidAndCountsFacts()
    {
        var snapshot = Populated();

        Assert.Empty(snapshot.Validate());
        snapshot.ThrowIfInvalid();
        Assert.Equal(15, snapshot.UnknownFactCount);
        Assert.True(snapshot.KnownFactCount > 60);
        Assert.True(snapshot.Player.HasVitals);
        Assert.Equal(0.812f, snapshot.Player.HpRatio!.Value, 3);
        Assert.True(snapshot.IsActionable);
        Assert.True(snapshot.AreVitalsFreshAt(T2, 1_000));
        Assert.False(snapshot.AreVitalsFreshAt(T2, 100));
        Assert.Single(snapshot.Buffs);
        Assert.Single(snapshot.Debuffs);
    }

    [Fact]
    public void SameObservationsGiveEqualSnapshotsAndDigests()
    {
        var a = Populated();
        var b = Populated();

        Assert.NotSame(a, b);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(a.ComputeDigest(), b.ComputeDigest());
        Assert.Equal(WorldModelCanonicalText.Write(a), WorldModelCanonicalText.Write(b));
    }

    [Fact]
    public void OneChangedFactChangesEqualityAndDigest()
    {
        var a = Populated();
        var b = a with { Player = a.Player with { Hp = Net(new Vital(811, 1000)) } };

        Assert.NotEqual(a, b);
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    [Fact]
    public void ProvenanceChangeAloneChangesDigest()
    {
        var a = Populated();
        var b = a with { Player = a.Player with { Hp = a.Player.Hp.AsCached() } };

        Assert.Equal(a.Player.Hp.Value, b.Player.Hp.Value);
        Assert.NotEqual(a.ComputeDigest(), b.ComputeDigest());
    }

    [Fact]
    public void SimulatedFactMakesSnapshotNonActionableButStillValid()
    {
        var a = Populated();
        var b = a with { Player = a.Player with { Hp = WorldFact<Vital>.Simulated(new Vital(100, 1000), 1f, T1, "what-if") } };

        Assert.Empty(b.Validate());
        Assert.True(b.HasSimulatedFact);
        Assert.False(b.IsActionable);
        Assert.False(a.HasSimulatedFact);
    }

    [Fact]
    public void AdvanceIncrementsRevisionAndRefusesTimeTravel()
    {
        var a = Populated();
        var next = a.Advance(T2 + 100);

        Assert.Equal(8, next.Revision);
        Assert.Equal(T2 + 100, next.ObservedAtUnixMillis);
        Assert.Equal(a.Player, next.Player);
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Advance(T2 - 1));
        Assert.Equal(a.Revision + 1, a.Advance(T2).Revision);
    }

    [Fact]
    public void ValidateReportsSchemaMismatch()
    {
        var bad = Populated() with { SchemaVersion = WorldModelContract.SchemaVersion + 1 };

        Assert.Contains(bad.Validate(), p => p.Contains("schema version"));
        var ex = Assert.Throws<InvalidOperationException>(bad.ThrowIfInvalid);
        Assert.Contains("schema version", ex.Message);
    }

    [Fact]
    public void ValidateReportsNegativeRevision()
    {
        var bad = Populated() with { Revision = -1 };
        Assert.Contains("revision is negative", bad.Validate());
    }

    [Fact]
    public void ValidateReportsInvalidVitals()
    {
        var a = Populated();
        var badHp = a with { Player = a.Player with { Hp = Net(new Vital(1001, 1000)) } };
        var zeroMax = a with { Player = a.Player with { Mp = Net(new Vital(0, 0)) } };
        var mobHp = a with { Mobs = [a.Mobs[0] with { Hp = Net(new Vital(-1, 10)) }] };

        Assert.Contains("player hp is not a valid vital", badHp.Validate());
        Assert.Contains("player mp is not a valid vital", zeroMax.Validate());
        Assert.Contains(mobHp.Validate(), p => p.Contains("mob 377 hp"));
    }

    [Fact]
    public void ValidateReportsUnsortedOrDuplicateEntities()
    {
        var a = Populated();
        var unsorted = a with { Mobs = [a.Mobs[1], a.Mobs[0]] };
        var duplicate = a with { Mobs = [a.Mobs[0], a.Mobs[0]] };
        var duplicateNpc = a with { Npcs = [a.Npcs[0], a.Npcs[0]] };

        Assert.Contains(unsorted.Validate(), p => p.Contains("mob id sequence is not strictly ascending"));
        Assert.Contains(duplicate.Validate(), p => p.Contains("mob id sequence is not strictly ascending"));
        Assert.Contains(duplicateNpc.Validate(), p => p.Contains("npc id sequence"));
    }

    [Fact]
    public void ValidateReportsDuplicateKeys()
    {
        var a = Populated();
        var quests = a with { Quests = [a.Quests[0], a.Quests[0]] };
        var inventory = a with { Inventory = [a.Inventory[0], a.Inventory[0]] };
        var equipment = a with { Equipment = [a.Equipment[0], a.Equipment[0]] };
        var skills = a with { Skills = [a.Skills[0], a.Skills[0]] };
        var effects = a with { StatusEffects = [a.StatusEffects[0], a.StatusEffects[0]] };
        var cooldowns = a with { Cooldowns = [a.Cooldowns[0], a.Cooldowns[0]] };
        var resources = a with { Resources = [a.Resources[0], a.Resources[0]] };
        var actions = a with { Actions = [a.Actions[0], a.Actions[0]] };
        var goals = a with { Goals = [a.Goals[0], a.Goals[0]] };
        var portals = a with { Map = a.Map with { Portals = [a.Map.Portals[0], a.Map.Portals[0]] } };
        var tiles = a with { Map = a.Map with { Tiles = [a.Map.Tiles[0], a.Map.Tiles[0]] } };

        Assert.Contains(quests.Validate(), p => p.StartsWith("duplicate quest id"));
        Assert.Contains(inventory.Validate(), p => p.StartsWith("duplicate inventory slot"));
        Assert.Contains(equipment.Validate(), p => p.StartsWith("duplicate equipment slot"));
        Assert.Contains(skills.Validate(), p => p.StartsWith("duplicate skill id"));
        Assert.Contains(effects.Validate(), p => p.StartsWith("duplicate status effect"));
        Assert.Contains(cooldowns.Validate(), p => p.StartsWith("duplicate cooldown"));
        Assert.Contains(resources.Validate(), p => p.StartsWith("duplicate resource kind"));
        Assert.Contains(actions.Validate(), p => p.StartsWith("duplicate action id"));
        Assert.Contains(goals.Validate(), p => p.StartsWith("duplicate goal id"));
        Assert.Contains(portals.Validate(), p => p.StartsWith("duplicate portal id"));
        Assert.Contains(tiles.Validate(), p => p.StartsWith("duplicate tile cell"));
    }

    [Fact]
    public void ValidateReportsGeometryProblems()
    {
        var a = Populated();
        var outside = a with { Map = a.Map with { Tiles = [new TileObservation(new MapCell(120, 0), Net(TileState.Walkable))] } };
        var badBounds = a with { Map = a.Map with { Bounds = Net(new MapBounds(0, 90)) } };

        Assert.Contains(outside.Validate(), p => p.Contains("outside map bounds"));
        Assert.Contains("map bounds are not strictly positive", badBounds.Validate());
    }

    [Fact]
    public void ValidateReportsFactNewerThanSnapshot()
    {
        var a = Populated();
        var future = a with { Player = a.Player with { Level = Net(38, at: T2 + 1) } };

        Assert.Contains("a fact is observed later than the snapshot itself", future.Validate());
    }

    [Fact]
    public void ValidateReportsGoalProgressAndInventoryQuantity()
    {
        var a = Populated();
        var goal = a with { Goals = [a.Goals[0] with { Progress = Net(1.5f) }] };
        var nan = a with { Goals = [a.Goals[0] with { Progress = Net(float.NaN) }] };
        var qty = a with { Inventory = [a.Inventory[0] with { Quantity = Net(-1) }] };

        Assert.Contains(goal.Validate(), p => p.Contains("progress outside"));
        Assert.Contains(nan.Validate(), p => p.Contains("progress outside"));
        Assert.Contains(qty.Validate(), p => p.Contains("quantity is negative"));
    }

    [Fact]
    public void DefaultImmutableArraysAreTreatedAsEmpty()
    {
        var a = WorldModelSnapshot.Empty(T0) with
        {
            Mobs = default,
            Npcs = default,
            Drops = default,
            Quests = default,
            Inventory = default,
            Equipment = default,
            Skills = default,
            StatusEffects = default,
            Cooldowns = default,
            Resources = default,
            Actions = default,
            Goals = default,
            Map = MapState.NotObserved(T0) with { Tiles = default, Regions = default, Portals = default }
        };

        Assert.Empty(a.Validate());
        Assert.Empty(a.Buffs);
        Assert.Equal(15, a.Facts().Count());
        Assert.NotEqual(WorldModelSnapshot.Empty(T0), a);
        _ = a.ComputeDigest();
    }

    [Fact]
    public void MapTileLookupIsUnknownForUnobservedCells()
    {
        var map = Map();

        Assert.Equal(TileState.Blocked, map.TileAt(new MapCell(43, 17), T2).Value);
        var unseen = map.TileAt(new MapCell(0, 0), T2);
        Assert.False(unseen.HasValue);
        Assert.Equal("cell never observed", unseen.Reason);
    }

    [Fact]
    public void PolygonAreaAndDegeneracy()
    {
        var square = Map().Regions[0];
        Assert.False(square.IsDegenerate);
        Assert.Equal(2L * 4 * 3, Math.Abs(square.TwiceSignedArea()));

        var line = new MapPolygon([new MapCell(0, 0), new MapCell(1, 1)], Net(TileState.Walkable));
        Assert.True(line.IsDegenerate);
        Assert.Equal(0, line.TwiceSignedArea());
        Assert.True(new MapPolygon(default, Net(TileState.Walkable)).IsDegenerate);
    }

    [Fact]
    public void RecordsWithImmutableArraysCompareByContent()
    {
        var a = Map();
        var b = Map();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(Quests()[0], Quests()[0]);
        Assert.Equal(a.Regions[0], b.Regions[0]);
        Assert.NotEqual(a, a with { Tiles = a.Tiles.RemoveAt(0) });
    }

    [Fact]
    public void EntityHelpersAreDeterministic()
    {
        var mobs = Mobs();

        Assert.Equal(377, mobs[0].Id.Value);
        Assert.Equal(501, mobs[1].Id.Value);
        Assert.True(EntityCollections.TryFind(mobs, new EntityId(501), out var fox));
        Assert.Equal("Fox", fox.Name.Value);
        Assert.False(EntityCollections.TryFind(mobs, new EntityId(1), out _));
        Assert.False(EntityCollections.TryFind(default, new EntityId(501), out _));
        Assert.True(fox.IsVisible);
    }

    [Fact]
    public void TimeDependentHelpersReturnNullWhenUnknown()
    {
        var skills = Skills();
        Assert.True(skills[0].IsReadyAt(T2));
        Assert.False(skills[1].IsReadyAt(T2));
        Assert.True(skills[1].IsReadyAt(T1 + 5_000));

        var readyOnly = skills[0] with { ReadyAtUnixMillis = Unknown<long>("no timer") };
        Assert.True(readyOnly.IsReadyAt(T2));
        var nothing = readyOnly with { Ready = Unknown<bool>("no flag") };
        Assert.Null(nothing.IsReadyAt(T2));

        var effects = StatusEffects();
        Assert.True(effects[0].IsActiveAt(T2));
        Assert.False(effects[0].IsActiveAt(T0 + 30_000));
        Assert.Null(effects[1].IsActiveAt(T2));

        var cooldowns = Cooldowns();
        Assert.Equal(4_250, cooldowns[0].RemainingAt(T2));
        Assert.Equal(0, cooldowns[0].RemainingAt(T1 + 9_000));
        Assert.Null((cooldowns[0] with { ReadyAtUnixMillis = Unknown<long>("x") }).RemainingAt(T2));

        var objective = Quests()[0].Objectives[0];
        Assert.False(objective.IsSatisfied);
        Assert.True((objective with { Progress = Net(5) }).IsSatisfied);
        Assert.False((objective with { Required = Unknown<int>("x") }).IsSatisfied);
    }

    [Fact]
    public void VitalRatioIsNullWhenMaximumUnknown()
    {
        Assert.Null(new Vital(5, 0).Ratio);
        Assert.Equal(0.5f, new Vital(5, 10).Ratio);
        Assert.Equal(1f, new Vital(20, 10).Ratio);
        Assert.False(new Vital(20, 10).IsValid);
        Assert.True(new Vital(0, 10).IsValid);
    }

    [Fact]
    public void IdentifiersHaveExplicitNone()
    {
        Assert.True(MapId.None.IsNone);
        Assert.True(EntityId.None.IsNone);
        Assert.True(QuestId.None.IsNone);
        Assert.True(ItemId.None.IsNone);
        Assert.True(SkillId.None.IsNone);
        Assert.True(EffectId.None.IsNone);
        Assert.True(ActionId.None.IsNone);
        Assert.True(WorldGoalId.None.IsNone);
        Assert.True(default(QuestId).IsNone);
        Assert.Equal(string.Empty, default(QuestId).ToString());
        Assert.False(new MapId(1).IsNone);
    }

    [Fact]
    public void MapCellDistances()
    {
        var a = new MapCell(0, 0);
        var b = new MapCell(3, -4);

        Assert.Equal(7, a.ManhattanDistanceTo(b));
        Assert.Equal(4, a.ChebyshevDistanceTo(b));
        Assert.Equal("(3,-4)", b.ToString());
        Assert.True(new MapBounds(10, 10).Contains(new MapCell(9, 9)));
        Assert.False(new MapBounds(10, 10).Contains(new MapCell(10, 9)));
        Assert.False(new MapBounds(10, 10).Contains(new MapCell(-1, 0)));
        Assert.Equal(100, new MapBounds(10, 10).CellCount);
    }

    [Fact]
    public void ImmutableSequenceHandlesDefaultArrays()
    {
        ImmutableArray<int> d = default;

        Assert.True(ImmutableSequence.Equal(d, d));
        Assert.False(ImmutableSequence.Equal(d, ImmutableArray<int>.Empty));
        Assert.True(ImmutableSequence.Equal(ImmutableArray.Create(1, 2), ImmutableArray.Create(1, 2)));
        Assert.False(ImmutableSequence.Equal(ImmutableArray.Create(1, 2), ImmutableArray.Create(2, 1)));
        Assert.False(ImmutableSequence.Equal(ImmutableArray.Create(1), ImmutableArray.Create(1, 2)));
    }
}
