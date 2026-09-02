namespace NosAi.Runtime.Contracts;

/// <summary>A point on the game's map.</summary>
public readonly record struct MapPoint(int X, int Y)
{
    public double DistanceTo(MapPoint other)
    {
        long dx = X - other.X;
        long dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
