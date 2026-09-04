namespace NosAi.Core.Navigation;

/// <summary>Deterministic bounded A* over a rectangular walkability grid.</summary>
public sealed class DeterministicGridNavigationPlanner : INavigationPlanner
{
    private readonly bool[,] _walkable;
    private readonly int _maxNodes;

    public DeterministicGridNavigationPlanner(bool[,] walkable, int maxNodes = 4096)
    {
        _walkable = walkable ?? throw new ArgumentNullException(nameof(walkable));
        _maxNodes = maxNodes > 0 ? maxNodes : throw new ArgumentOutOfRangeException(nameof(maxNodes));
    }

    public bool TryFindPath(in NavigationPoint start, in NavigationPoint goal, Span<NavigationPoint> output, out int count)
    {
        count = 0;
        var sx = (int)MathF.Round(start.X); var sy = (int)MathF.Round(start.Y);
        var gx = (int)MathF.Round(goal.X); var gy = (int)MathF.Round(goal.Y);
        if (!Inside(sx, sy) || !Inside(gx, gy) || !_walkable[sx, sy] || !_walkable[gx, gy] || output.IsEmpty) return false;

        var open = new PriorityQueue<Node, int>();
        var best = new Dictionary<(int X, int Y), int>();
        var nodes = 0;
        open.Enqueue(new Node(sx, sy, null, 0), Heuristic(sx, sy, gx, gy));
        best[(sx, sy)] = 0;

        while (open.TryDequeue(out var current, out _) && nodes++ < _maxNodes)
        {
            if (current.X == gx && current.Y == gy)
            {
                var reverse = new List<NavigationPoint>();
                for (Node? n = current; n is not null; n = n.Parent) reverse.Add(new NavigationPoint(n.X, n.Y));
                reverse.Reverse();
                if (reverse.Count > output.Length) return false;
                reverse.CopyTo(output);
                count = reverse.Count;
                return true;
            }

            foreach (var (dx, dy) in Directions)
            {
                var nx = current.X + dx; var ny = current.Y + dy;
                if (!Inside(nx, ny) || !_walkable[nx, ny]) continue;
                var cost = current.Cost + 1;
                if (best.TryGetValue((nx, ny), out var known) && known <= cost) continue;
                best[(nx, ny)] = cost;
                open.Enqueue(new Node(nx, ny, current, cost), cost + Heuristic(nx, ny, gx, gy));
            }
        }
        return false;
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _walkable.GetLength(0) && y < _walkable.GetLength(1);
    private static int Heuristic(int x, int y, int gx, int gy) => Math.Abs(x - gx) + Math.Abs(y - gy);
    private static readonly (int X, int Y)[] Directions = [(1, 0), (0, 1), (-1, 0), (0, -1)];
    private sealed record Node(int X, int Y, Node? Parent, int Cost);
}
