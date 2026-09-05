using System.Runtime.InteropServices;

namespace NosAi.Core.WorldModel;

/// <summary>Posizione in coordinate tile della mappa corrente. Le mappe del client sono &lt; 32768 tile per lato.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MapPosition(short X, short Y)
{
    /// <summary>Distanza di Chebyshev: il numero minimo di passi con movimento a 8 direzioni.</summary>
    public int ChebyshevDistanceTo(MapPosition other)
        => Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    public int ManhattanDistanceTo(MapPosition other)
        => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public double EuclideanDistanceTo(MapPosition other)
    {
        long dx = X - other.X;
        long dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public override string ToString() => $"({X},{Y})";
}

/// <summary>Velocità stimata in tile/secondo lungo i due assi.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct Velocity2(float Vx, float Vy)
{
    public static Velocity2 Zero => default;
    public float Magnitude => MathF.Sqrt(Vx * Vx + Vy * Vy);
}

/// <summary>Direzione a 8 vie del client (0 = nord, in senso orario).</summary>
public enum Direction8 : byte
{
    North = 0,
    NorthEast = 1,
    East = 2,
    SouthEast = 3,
    South = 4,
    SouthWest = 5,
    West = 6,
    NorthWest = 7
}

/// <summary>Limiti osservati di una mappa. Inclusivi su entrambi gli estremi.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MapBounds
{
    public short MinX { get; }
    public short MinY { get; }
    public short MaxX { get; }
    public short MaxY { get; }

    public MapBounds(short minX, short minY, short maxX, short maxY)
    {
        if (maxX < minX) throw new ArgumentOutOfRangeException(nameof(maxX), maxX, "maxX must be >= minX");
        if (maxY < minY) throw new ArgumentOutOfRangeException(nameof(maxY), maxY, "maxY must be >= minY");
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public int Width => MaxX - MinX + 1;
    public int Height => MaxY - MinY + 1;
    public long Area => (long)Width * Height;

    public bool Contains(MapPosition position)
        => position.X >= MinX && position.X <= MaxX && position.Y >= MinY && position.Y <= MaxY;

    /// <summary>I limiti minimi che contengono entrambi: come cresce la stima man mano che si esplora.</summary>
    public MapBounds Union(MapBounds other)
        => new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public MapBounds Extend(MapPosition position)
        => new(Math.Min(MinX, position.X), Math.Min(MinY, position.Y), Math.Max(MaxX, position.X), Math.Max(MaxY, position.Y));
}

/// <summary>Classe di percorribilità di un tile. <see cref="Unknown"/> è un tile mai osservato, non un tile libero.</summary>
public enum TileKind : byte
{
    Unknown = 0,
    Walkable = 1,
    Blocked = 2,
    Water = 3,
    PortalTrigger = 4
}

/// <summary>Un tile osservato: posizione + fatto sulla sua classe.</summary>
public readonly record struct TileState(MapPosition Position, Fact<TileKind> Kind) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Kind);
        return summary;
    }
}

/// <summary>
/// Regione poligonale della mappa con percorribilità uniforme. I vertici sono in
/// ordine (orario o antiorario) e il poligono è chiuso implicitamente.
/// </summary>
/// <param name="Vertices">Almeno 3 vertici quando <see cref="Walkable"/> ha un valore.</param>
public sealed record PolygonRegion(
    ReadOnlyMemory<MapPosition> Vertices,
    Fact<bool> Walkable,
    Fact<float> Coverage) : IFactCarrier
{
    /// <summary>Test punto-in-poligono (ray casting) sui vertici osservati. Falso con meno di 3 vertici.</summary>
    public bool Contains(MapPosition point)
    {
        ReadOnlySpan<MapPosition> v = Vertices.Span;
        if (v.Length < 3) return false;

        bool inside = false;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
        {
            int xi = v[i].X, yi = v[i].Y;
            int xj = v[j].X, yj = v[j].Y;
            bool crosses = (yi > point.Y) != (yj > point.Y);
            if (!crosses) continue;
            double xCross = (double)(xj - xi) * (point.Y - yi) / (yj - yi) + xi;
            if (point.X < xCross) inside = !inside;
        }

        return inside;
    }

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Walkable);
        summary.Add(Coverage);
        return summary;
    }
}

/// <summary>Modalità con cui un portale si attraversa.</summary>
public enum PortalKind : byte
{
    Unknown = 0,
    Walk = 1,
    Interact = 2,
    Timed = 3,
    Locked = 4
}

/// <summary>Portale osservato o ricordato. La destinazione è un fatto separato: si può vedere un portale senza sapere dove porta.</summary>
public sealed record PortalState(
    PortalId Id,
    Fact<MapPosition> Position,
    Fact<PortalKind> Kind,
    Fact<MapId> DestinationMap,
    Fact<MapPosition> DestinationPosition,
    Fact<bool> Traversable) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Position);
        summary.Add(Kind);
        summary.Add(DestinationMap);
        summary.Add(DestinationPosition);
        summary.Add(Traversable);
        return summary;
    }

    public static PortalState Unknown(PortalId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<PortalKind>.Unknown(reason, observedAtUnixMillis),
        Fact<MapId>.Unknown(reason, observedAtUnixMillis),
        Fact<MapPosition>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>
/// Stato di una mappa: identità, limiti osservati, tile e poligoni ricostruiti,
/// portali. <see cref="Revision"/> cresce a ogni aggiornamento persistito così
/// una mappa parzialmente esplorata può essere salvata, aggiornata e ripresa.
/// </summary>
public sealed record MapState(
    Fact<MapId> Id,
    long Revision,
    Fact<MapBounds> Bounds,
    Fact<float> ExploredRatio,
    ReadOnlyMemory<TileState> Tiles,
    ReadOnlyMemory<PolygonRegion> Polygons,
    ReadOnlyMemory<PortalState> Portals) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Id);
        summary.Add(Bounds);
        summary.Add(ExploredRatio);
        summary.AddAll(Tiles.Span);
        summary.AddAll(Polygons.Span);
        summary.AddAll(Portals.Span);
        return summary;
    }

    /// <summary>Classe del tile in <paramref name="position"/>, o UNKNOWN(<see cref="UnknownReasons.NotObserved"/>) se nessun tile è stato osservato lì.</summary>
    public Fact<TileKind> TileAt(MapPosition position)
    {
        ReadOnlySpan<TileState> tiles = Tiles.Span;
        for (int i = 0; i < tiles.Length; i++)
            if (tiles[i].Position == position)
                return tiles[i].Kind;
        return Fact<TileKind>.Unknown(UnknownReasons.NotObserved);
    }

    public static MapState Unknown(string reason, long observedAtUnixMillis = 0) => new(
        Fact<MapId>.Unknown(reason, observedAtUnixMillis),
        0,
        Fact<MapBounds>.Unknown(reason, observedAtUnixMillis),
        Fact<float>.Unknown(reason, observedAtUnixMillis),
        ReadOnlyMemory<TileState>.Empty,
        ReadOnlyMemory<PolygonRegion>.Empty,
        ReadOnlyMemory<PortalState>.Empty);
}
