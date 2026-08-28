namespace NosAi.Runtime.Humanizer;

/// <summary>
/// Deterministic, testable Humanizer foundation. It produces timing/trajectory
/// plans only; live input is delegated to the fail-closed low-level boundary.
/// </summary>
public sealed class DeterministicHumanizer : IHumanizer
{
    private readonly Func<int, CancellationToken, Task> _delay;

    public DeterministicHumanizer(Func<int, CancellationToken, Task>? delay = null)
        => _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));

    public async Task MoveMouseHumanlikeAsync(ScreenPoint start, TargetDescriptor target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = BuildBezierPlan(start, target.Point);
        await _delay(35, cancellationToken);
    }

    public async Task PressKeyHumanlikeAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));
        cancellationToken.ThrowIfCancellationRequested();
        await _delay(80, cancellationToken);
    }

    public static IReadOnlyList<ScreenPoint> BuildBezierPlan(ScreenPoint start, ScreenPoint target, int segments = 8)
    {
        if (segments < 2) throw new ArgumentOutOfRangeException(nameof(segments));
        var dx = target.X - start.X;
        var dy = target.Y - start.Y;
        var control1 = new ScreenPoint(start.X + dx / 3, start.Y + dy / 3);
        var control2 = new ScreenPoint(start.X + 2 * dx / 3, start.Y + 2 * dy / 3);
        var points = new List<ScreenPoint>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var t = (double)i / segments;
            var u = 1 - t;
            var x = u * u * u * start.X + 3 * u * u * t * control1.X + 3 * u * t * t * control2.X + t * t * t * target.X;
            var y = u * u * u * start.Y + 3 * u * u * t * control1.Y + 3 * u * t * t * control2.Y + t * t * t * target.Y;
            points.Add(new ScreenPoint((int)Math.Round(x), (int)Math.Round(y)));
        }
        return points;
    }
}
