using NosAi.Core.WorldModel;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-03/A5 audit item 3: black-box coverage of
/// <c>MapReconstructionSource</c>'s private <c>"map-{numericId}"</c> &lt;-&gt;
/// <c>int</c> parser (<c>TryParseNumericMapId</c>), exercised only through
/// the public <see cref="MapReconstructionSource.Resolve"/> contract --
/// exactly the same technique <c>MapReconstructionSourceTests</c>'s own
/// <c>Resolve_AMapIdThatDoesNotParse_...</c> and
/// <c>Resolve_NoRealMapIdKnown_...</c> already use for two of these cases.
/// This file adds the remaining edge cases the audit command names by name:
/// an empty numeric suffix, a negative number, a leading-zero suffix and a
/// suffix that overflows <see cref="int"/>. No grid file and no real map ever
/// exists on disk here for any of these ids: the whole point is to observe
/// what happens on the very first cycle a bogus id is presented, before any
/// I/O succeeds or fails on its own merits.
/// </summary>
/// <remarks>
/// "Fails closed" for this parser means exactly what
/// <see cref="MapReconstructionSource.Resolve"/>'s own contract already
/// promises for a map id that does not parse: the caller's own
/// <c>WorldModelSnapshot.Map</c> comes back completely unchanged (by
/// reference), and the cache is left untouched -- never an exception, and
/// never a fabricated tile from a numeric id that was never real.
/// </remarks>
public sealed class MapReconstructionSourceMapIdParsingTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    private static WorldModelSnapshot SnapshotFor(MapId mapId, DateTime nowUtc)
    {
        WorldModelSnapshot baseline = WorldModelSnapshot.Unknown("test_baseline", nowUtc);
        var map = new MapModel(
            mapId,
            WorldFact<string>.Unknown("map_name_catalog_not_available", nowUtc),
            WorldFact<MapBounds>.Unknown("map_bounds_not_yet_reconstructed", nowUtc),
            EquatableArray<Tile>.Empty,
            EquatableArray<Portal>.Empty,
            EquatableArray<Polygon>.Empty,
            Version: 0,
            nowUtc);
        return baseline with { Map = map };
    }

    /// <summary>
    /// A source with no maps-directory override that resolves to a real
    /// directory and no real labeled volume for its store: every one of
    /// these ids must be rejected by the id parser itself, before either
    /// piece of I/O is ever attempted, so neither missing dependency can be
    /// the reason the test passes.
    /// </summary>
    private static MapReconstructionSource NewSourceWithNoRealIo() =>
        new(mapsDirectoryOverride: Path.Combine(Path.GetTempPath(), "nosai-maprecon-parsing-tests-unused"));

    [Theory]
    [InlineData("map-")] // empty numeric suffix
    [InlineData("map-abc")] // non-numeric suffix
    [InlineData("map-1.5")] // not an integer
    [InlineData("map-99999999999")] // overflows int.MaxValue (2147483647)
    [InlineData("map--99999999999")] // overflows even as a negative magnitude
    [InlineData("unknown-map")] // the documented sentinel itself
    [InlineData("Map-1")] // wrong case on the literal prefix -- StartsWith is Ordinal
    [InlineData("map")] // missing the separator entirely
    public void Resolve_AMapIdThatMustNotParse_ReturnsTheSnapshotsOwnMapUnchanged_ByReference(string rawMapId)
    {
        using MapReconstructionSource source = NewSourceWithNoRealIo();
        WorldModelSnapshot snapshot = SnapshotFor(new MapId(rawMapId), T0);

        MapModel result = source.Resolve(snapshot, T0);

        Assert.Same(snapshot.Map, result);
    }

    /// <summary>
    /// "map--5" is what <c>GameplayObservationProjector.Project</c> itself
    /// would emit if the wire's own numeric map id were ever negative
    /// (<c>$"map-{id}"</c> with <c>id == -5</c> naturally concatenates to
    /// "map--5") -- so this is not an adversarial string invented here, it is
    /// the one negative-id round-trip the real producer can actually create.
    /// Unlike the ids above, this one DOES parse (integer parsing legitimately
    /// recovers -5), and no real map/grid file is expected to exist for it,
    /// so the honest outcome is a normal "grid not available" miss -- fed
    /// through the same fail-soft Merge path any other unresolvable numeric
    /// id takes, never an exception and never a fabricated tile.
    /// </summary>
    [Fact]
    public void Resolve_ANegativeNumericMapId_ParsesButFailsClosedToAnEmptyReconstruction_NeverThrows_NeverFabricatesATile()
    {
        using MapReconstructionSource source = NewSourceWithNoRealIo();
        WorldModelSnapshot snapshot = SnapshotFor(new MapId("map--5"), T0);

        MapModel result = source.Resolve(snapshot, T0);

        // It did NOT take the "no real map id" early-out (that path returns
        // snapshot.Map by reference, asserted separately above): -5 parsed,
        // so Resolve actually ran its pipeline for it and came back with an
        // honest, empty reconstruction instead.
        Assert.NotSame(snapshot.Map, result);
        Assert.Empty(result.Tiles);
        Assert.Empty(result.Portals);
    }

    [Fact]
    public void Resolve_ALeadingZeroNumericSuffix_ParsesToItsPlainDecimalValue_NotMisreadAsOctalOrRejected()
    {
        using MapReconstructionSource source = NewSourceWithNoRealIo();
        WorldModelSnapshot snapshotWithLeadingZeros = SnapshotFor(new MapId("map-007"), T0);
        WorldModelSnapshot snapshotPlain = SnapshotFor(new MapId("map-7"), T0);

        MapModel resultWithLeadingZeros = source.Resolve(snapshotWithLeadingZeros, T0);
        MapModel resultPlain = source.Resolve(snapshotPlain, T0.AddSeconds(1));

        // Both parse to the plain decimal value 7 (never interpreted as
        // base-8 "007" -- int.TryParse under NumberStyles.Integer never
        // does), so both take the "parsed, but no real grid/map-7 exists on
        // this host" path and come back with the same honest empty shape;
        // neither is silently rejected as unparsable.
        Assert.NotSame(snapshotWithLeadingZeros.Map, resultWithLeadingZeros);
        Assert.NotSame(snapshotPlain.Map, resultPlain);
        Assert.Empty(resultWithLeadingZeros.Tiles);
        Assert.Empty(resultPlain.Tiles);
    }
}
