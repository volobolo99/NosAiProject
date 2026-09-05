using System.Collections.Immutable;
using System.Globalization;

namespace NosAi.Core.WorldModel;

/// <summary>A cell on the map grid. Integer client coordinates, origin top-left.</summary>
public readonly record struct MapCell(int X, int Y)
{
    /// <summary>Manhattan distance — the movement metric of a 4-connected grid.</summary>
    public int ManhattanDistanceTo(MapCell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    /// <summary>Chebyshev distance — the metric of an 8-connected grid.</summary>
    public int ChebyshevDistanceTo(MapCell other) => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X},{Y})");
}

/// <summary>Observed bounds of a map. Width/height may be estimates; provenance travels with the enclosing fact.</summary>
public readonly record struct MapBounds(int Width, int Height)
{
    /// <summary>Whether the bounds describe a real area. Bounds inside a known fact must be valid; see <see cref="WorldModelSnapshot.Validate"/>.</summary>
    public bool IsValid => Width > 0 && Height > 0;

    public bool Contains(MapCell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    public long CellCount => (long)Width * Height;
}

/// <summary>Walkability of a cell as far as the model knows.</summary>
public enum TileState : byte
{
    Unknown = 0,
    Walkable = 1,
    Blocked = 2
}

/// <summary>One cell's walkability with provenance. Unknown cells stay UNKNOWN; they are never assumed blocked or walkable.</summary>
public readonly record struct TileObservation(MapCell Cell, WorldFact<TileState> State);

/// <summary>
/// A closed polygon on the grid (e.g. a walkable region reconstructed by
/// AP-03). Vertices are ordered; the last vertex connects back to the first.
/// </summary>
public sealed record MapPolygon(ImmutableArray<MapCell> Vertices, WorldFact<TileState> State)
{
    public bool IsDegenerate => Vertices.IsDefaultOrEmpty || Vertices.Length < 3;

    /// <summary>Twice the signed area (shoelace). Zero for degenerate polygons.</summary>
    public long TwiceSignedArea()
    {
        if (IsDegenerate)
        {
            return 0;
        }

        long sum = 0;
        for (var i = 0; i < Vertices.Length; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % Vertices.Length];
            sum += (long)a.X * b.Y - (long)b.X * a.Y;
        }

        return sum;
    }

    public bool Equals(MapPolygon? other)
        => other is not null
           && State.Equals(other.State)
           && Vertices.AsSpan().SequenceEqual(other.Vertices.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(State);
        if (!Vertices.IsDefault)
        {
            foreach (var v in Vertices)
            {
                hash.Add(v);
            }
        }

        return hash.ToHashCode();
    }
}

/// <summary>A portal between maps as observed. The destination may be UNKNOWN until traversed.</summary>
public sealed record PortalState(
    EntityId Id,
    WorldFact<MapCell> Cell,
    WorldFact<MapId> DestinationMap,
    WorldFact<MapCell> DestinationCell);

/// <summary>
/// The spatial model of the current map: identity, bounds, observed tiles,
/// reconstructed regions and portals. Immutable; a new observation yields a new
/// record via <c>with</c>.
/// </summary>
public sealed record MapState(
    WorldFact<MapId> Id,
    WorldFact<string> Name,
    WorldFact<MapBounds> Bounds,
    ImmutableArray<TileObservation> Tiles,
    ImmutableArray<MapPolygon> Regions,
    ImmutableArray<PortalState> Portals)
{
    public static MapState NotObserved(long atUnixMillis) => new(
        WorldFact<MapId>.NotObserved(atUnixMillis),
        WorldFact<string>.NotObserved(atUnixMillis),
        WorldFact<MapBounds>.NotObserved(atUnixMillis),
        ImmutableArray<TileObservation>.Empty,
        ImmutableArray<MapPolygon>.Empty,
        ImmutableArray<PortalState>.Empty);

    /// <summary>Looks a cell up among the observed tiles; UNKNOWN when no tile was ever observed there.</summary>
    public WorldFact<TileState> TileAt(MapCell cell, long atUnixMillis)
    {
        if (!Tiles.IsDefaultOrEmpty)
        {
            foreach (var tile in Tiles)
            {
                if (tile.Cell == cell)
                {
                    return tile.State;
                }
            }
        }

        return WorldFact<TileState>.Unknown("cell never observed", atUnixMillis);
    }

    public bool Equals(MapState? other)
        => other is not null
           && Id.Equals(other.Id)
           && Name.Equals(other.Name)
           && Bounds.Equals(other.Bounds)
           && ImmutableSequence.Equal(Tiles, other.Tiles)
           && ImmutableSequence.Equal(Regions, other.Regions)
           && ImmutableSequence.Equal(Portals, other.Portals);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Bounds);
        ImmutableSequence.AddTo(ref hash, Tiles);
        ImmutableSequence.AddTo(ref hash, Regions);
        ImmutableSequence.AddTo(ref hash, Portals);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Element-wise equality for <see cref="ImmutableArray{T}"/> members of records.
/// Records compare immutable arrays by reference, which would make two
/// snapshots built from the same observations unequal and break replay checks.
/// </summary>
public static class ImmutableSequence
{
    public static bool Equal<T>(ImmutableArray<T> left, ImmutableArray<T> right)
    {
        if (left.IsDefault || right.IsDefault)
        {
            return left.IsDefault == right.IsDefault;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < left.Length; i++)
        {
            if (!comparer.Equals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static void AddTo<T>(ref HashCode hash, ImmutableArray<T> items)
    {
        if (items.IsDefault)
        {
            hash.Add(-1);
            return;
        }

        hash.Add(items.Length);
        foreach (var item in items)
        {
            hash.Add(item);
        }
    }
}
