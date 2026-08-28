using NosAi.Runtime.LowLevel;

namespace NosAi.Runtime.Humanizer;

/// <summary>
/// Humanizer that converts high-level movement/key requests into deterministic
/// timing and trajectory plans. The low-level backend remains an explicit dependency.
/// </summary>
public sealed class DeterministicHumanizer : IHumanizer
{
    private readonly IInputBackend? _input;
    private readonly Func<int, CancellationToken, Task> _delay;

    public DeterministicHumanizer(IInputBackend? input = null,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _input = input;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    public async Task MoveMouseHumanlikeAsync(ScreenPoint start, TargetDescriptor target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = BuildBezierPlan(start, target.Point);
        for (var i = 1; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dx = plan[i].X - plan[i - 1].X;
            var dy = plan[i].Y - plan[i - 1].Y;
            if (_input is not null && ! _input.MoveRelative(dx, dy))
                throw new InvalidOperationException("Low-level mouse input was rejected.");
            await _delay(35, cancellationToken);
        }
    }

    public async Task PressKeyHumanlikeAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryResolveVirtualKey(key, out var virtualKey))
            throw new ArgumentException($"Unsupported virtual key: {key}", nameof(key));

        if (_input is not null && !_input.KeyPress(virtualKey, 80))
            throw new InvalidOperationException("Low-level keyboard input was rejected.");
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

    private static bool TryResolveVirtualKey(string key, out ushort value)
    {
        value = 0;
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) { value = c; return true; }
        }
        return key.ToUpperInvariant() switch
        {
            "SPACE" => Set(0x20, out value),
            "ENTER" => Set(0x0D, out value),
            "ESC" or "ESCAPE" => Set(0x1B, out value),
            "TAB" => Set(0x09, out value),
            _ => false
        };
    }

    private static bool Set(ushort key, out ushort value) { value = key; return true; }
}
