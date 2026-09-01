using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// How a client patch invalidates the extracted grids without any code that watches
/// for patches.
/// </summary>
/// <remarks>
/// The grids are <c>CACHED</c> data with provenance "client file": true only while
/// the build they came from is the build that is running. A wrong grid is worse than
/// a missing one — it walks the character into a wall that moved, confidently — so
/// the invariant cannot be "remember to re-extract". These are the cases that make it
/// a fact the runtime checks instead.
/// </remarks>
public sealed class MapGridSetIdentityTests
{
    private static MapGridFile File(int mapId, string content) =>
        new(mapId, MapGridSetIdentity.HashFile(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public void TheGridsAreCachedClientDataAndNeverLive()
    {
        Assert.Equal(DataSourceKind.Cached, MapGridSetIdentity.Classification);
        Assert.Equal("client-file", MapGridSetIdentity.Provenance);
    }

    [Fact]
    public void TheSameFilesAndTheSameClientGiveTheSameIdentity()
    {
        MapGridFile[] files = [File(1, "a"), File(2, "b")];

        MapGridSetIdentity first = MapGridSetIdentity.Compute(files, "client-abc");
        MapGridSetIdentity second = MapGridSetIdentity.Compute(files, "client-abc");

        Assert.Equal(first.SetHash, second.SetHash);
        Assert.True(MapGridSetIdentity.MayLoad(first, second, out string? reason));
        Assert.Null(reason);
    }

    /// <summary>
    /// An extractor that walks a directory in a different order must not produce a
    /// different identity for the same content.
    /// </summary>
    [Fact]
    public void TheOrderTheFilesArriveInDoesNotChangeTheIdentity()
    {
        MapGridSetIdentity ascending = MapGridSetIdentity.Compute(
            [File(1, "a"), File(2, "b"), File(3, "c")], "client-abc");

        MapGridSetIdentity shuffled = MapGridSetIdentity.Compute(
            [File(3, "c"), File(1, "a"), File(2, "b")], "client-abc");

        Assert.Equal(ascending.SetHash, shuffled.SetHash);
        Assert.Equal([1, 2, 3], ascending.Files.Select(f => f.MapId));
    }

    /// <summary>The half that is easy to leave out: the client is not the only input.</summary>
    [Fact]
    public void EditingOneGridFileInvalidatesTheSetWithTheClientUntouched()
    {
        MapGridSetIdentity recorded = MapGridSetIdentity.Compute(
            [File(1, "a"), File(2, "b")], "client-abc");

        MapGridSetIdentity current = MapGridSetIdentity.Compute(
            [File(1, "a"), File(2, "b-edited")], "client-abc");

        Assert.False(MapGridSetIdentity.MayLoad(recorded, current, out string? reason));
        Assert.StartsWith("map_grid_set_changed", reason);
    }

    [Fact]
    public void APatchedClientInvalidatesTheSetWithTheFilesUntouched()
    {
        MapGridFile[] files = [File(1, "a"), File(2, "b")];

        MapGridSetIdentity recorded = MapGridSetIdentity.Compute(files, "client-abc");
        MapGridSetIdentity current = MapGridSetIdentity.Compute(files, "client-def");

        Assert.False(MapGridSetIdentity.MayLoad(recorded, current, out string? reason));
        Assert.StartsWith("client_build_changed", reason);
    }

    [Fact]
    public void AddingOrRemovingAMapInvalidatesTheSet()
    {
        MapGridSetIdentity two = MapGridSetIdentity.Compute([File(1, "a"), File(2, "b")], "c");
        MapGridSetIdentity three = MapGridSetIdentity.Compute([File(1, "a"), File(2, "b"), File(3, "c")], "c");

        Assert.False(MapGridSetIdentity.MayLoad(two, three, out _));
        Assert.False(MapGridSetIdentity.MayLoad(three, two, out _));
    }

    /// <summary>
    /// The separators have to make the fold unambiguous, or a change moves from one
    /// field into its neighbour and the hash never notices.
    /// </summary>
    [Fact]
    public void TwoDifferentSetsCannotFoldToTheSameHash()
    {
        // Same characters, different boundaries: map 1 with hash "aa" and map 11 with
        // hash "a" would collide under a naive concatenation.
        MapGridSetIdentity first = MapGridSetIdentity.Compute([new MapGridFile(1, "aa")], "c");
        MapGridSetIdentity second = MapGridSetIdentity.Compute([new MapGridFile(11, "a")], "c");

        Assert.NotEqual(first.SetHash, second.SetHash);
    }

    /// <summary>Fails closed on every ambiguity. There is no "probably the same" here.</summary>
    [Fact]
    public void AMissingIdentityOnEitherSideRefusesToLoad()
    {
        MapGridSetIdentity known = MapGridSetIdentity.Compute([File(1, "a")], "c");

        Assert.False(MapGridSetIdentity.MayLoad(null, known, out string? noRecord));
        Assert.Equal("map_grids_no_recorded_identity", noRecord);

        Assert.False(MapGridSetIdentity.MayLoad(known, null, out string? noCurrent));
        Assert.Equal("map_grids_current_identity_unknown", noCurrent);

        Assert.False(MapGridSetIdentity.MayLoad(null, null, out _));
    }

    /// <summary>
    /// Two grids for one map is not a tie to break: picking one would make the
    /// identity depend on which was seen first.
    /// </summary>
    [Fact]
    public void ADuplicateMapIdIsRefusedRatherThanResolved()
    {
        Assert.Throws<ArgumentException>(() =>
            MapGridSetIdentity.Compute([File(1, "a"), File(1, "b")], "c"));
    }

    /// <summary>
    /// An identity computed over malformed inputs still compares equal to itself,
    /// which is exactly how a broken extraction survives the check it exists to fail.
    /// </summary>
    [Fact]
    public void MalformedInputsAreRefusedAtComputationTime()
    {
        Assert.Throws<ArgumentException>(() =>
            MapGridSetIdentity.Compute([new MapGridFile(1, "not-hex!")], "c"));

        Assert.Throws<ArgumentException>(() =>
            MapGridSetIdentity.Compute([new MapGridFile(1, "")], "c"));

        Assert.Throws<ArgumentException>(() =>
            MapGridSetIdentity.Compute([File(1, "a")], "   "));
    }

    [Fact]
    public void AFileHashIsLowercaseHexOverTheExactBytes()
    {
        string hash = MapGridSetIdentity.HashFile(new byte[] { 0x00, 0x01, 0x02 });

        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
        Assert.NotEqual(hash, MapGridSetIdentity.HashFile(new byte[] { 0x00, 0x01, 0x03 }));

        // A byte the current parser ignores still counts as a change.
        Assert.NotEqual(
            MapGridSetIdentity.HashFile(new byte[] { 0x02, 0x00, 0x01, 0x00, 0x20 }),
            MapGridSetIdentity.HashFile(new byte[] { 0x02, 0x00, 0x01, 0x00, 0x00 }));
    }

    /// <summary>
    /// The end of the chain, and the reason the two navigation types fail closed on
    /// a default instance: a refused identity means no grid, and no grid means
    /// planning stops rather than running over an open map.
    /// </summary>
    [Fact]
    public void ARefusedIdentityLeavesAGridThatBlocksTheWholeMap()
    {
        MapGridSetIdentity recorded = MapGridSetIdentity.Compute([File(1, "a")], "client-abc");
        MapGridSetIdentity current = MapGridSetIdentity.Compute([File(1, "a")], "client-patched");

        MapGrid grid = MapGridSetIdentity.MayLoad(recorded, current, out _)
            ? new MapGrid(1, 2, 2, new byte[4])
            : default;

        Assert.False(grid.IsLoaded);
        Assert.False(grid.IsWalkable(0, 0));
        Assert.True(grid.BlocksAttack(0, 0));
        Assert.Equal(
            NosAi.Navigation.Pathfinding.TileType.Unobserved,
            StaticGeometryLayer.Compose(grid, 0, 0, DynamicOccupancy.Clear));
    }
}
