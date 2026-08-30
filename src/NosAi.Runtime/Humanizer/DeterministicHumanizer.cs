// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Humanizer — Pianificazione deterministica di traiettorie e pressioni tasto
// ============================================================================
//
// I tempi sono FISSI e deterministici, non randomizzati: servono a rendere il
// movimento riproducibile e verificabile, non a nascondere l'automazione. Il
// progetto esclude esplicitamente l'automazione evasiva verso i sistemi
// anti-cheat (docs/PERSISTENZA_SQLITE_E_SHARED_MEMORY.md), e introdurre jitter
// casuale a fini di non rilevabilità ricadrebbe in quel divieto.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.LowLevel;

namespace NosAi.Runtime.Humanizer;

/// <summary>
/// Turns high-level movement/key requests into deterministic trajectory and
/// timing plans, then hands them to the input backend.
/// </summary>
public sealed class DeterministicHumanizer : IHumanizer
{
    /// <summary>Milliseconds between two points of a mouse trajectory.</summary>
    public const int StepDelayMs = 35;

    /// <summary>How long a key is held down.</summary>
    public const int KeyHoldMs = 80;

    private readonly IInputBackend? _input;
    private readonly Func<int, CancellationToken, Task> _delay;

    public DeterministicHumanizer(IInputBackend? input = null,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _input = input;
        _delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
    }

    /// <summary>
    /// Moves the cursor to the target along a cubic Bézier path.
    /// </summary>
    /// <remarks>
    /// The starting point is read from the backend, not assumed. The previous
    /// version was handed a hard-coded (400,300) by the adapter and applied
    /// relative deltas from it, so unless the cursor happened to sit exactly
    /// there the whole path landed somewhere else entirely.
    /// </remarks>
    public async Task MoveMouseHumanlikeAsync(ScreenPoint start, TargetDescriptor target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        ScreenPoint origin = start;
        if (_input is not null && _input.TryGetCursorPosition(out int cursorX, out int cursorY))
            origin = new ScreenPoint(cursorX, cursorY);

        IReadOnlyList<ScreenPoint> plan = BuildBezierPlan(origin, target.Point);
        for (int i = 1; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_input is not null)
            {
                // Absolute positioning removes the drift that accumulates when a
                // relative step is clamped at a screen edge or lost.
                if (!_input.MoveAbsolute(plan[i].X, plan[i].Y))
                    throw new InvalidOperationException("Low-level mouse input was rejected.");
            }
            await _delay(StepDelayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Moves to the target and clicks the given button.</summary>
    public async Task ClickTargetAsync(TargetDescriptor target, MouseButton button, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        await MoveMouseHumanlikeAsync(target.Point, target, cancellationToken).ConfigureAwait(false);
        if (_input is not null && !_input.Click(button))
            throw new InvalidOperationException($"Low-level mouse click ({button}) was rejected.");
        await _delay(StepDelayMs, cancellationToken).ConfigureAwait(false);
    }

    public async Task PressKeyHumanlikeAsync(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key is required.", nameof(key));
        cancellationToken.ThrowIfCancellationRequested();

        if (!VirtualKeys.TryResolve(key, out KeyChord chord))
            throw new ArgumentException($"Unsupported virtual key: {key}", nameof(key));

        if (_input is not null && !_input.KeyPress(chord.VirtualKey, KeyHoldMs, chord.Modifiers.AsSpan()))
            throw new InvalidOperationException("Low-level keyboard input was rejected.");
        await _delay(KeyHoldMs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the cubic Bézier trajectory between two points. Pure and
    /// deterministic: the same endpoints always produce the same path.
    /// </summary>
    public static IReadOnlyList<ScreenPoint> BuildBezierPlan(ScreenPoint start, ScreenPoint target, int segments = 8)
    {
        if (segments < 2) throw new ArgumentOutOfRangeException(nameof(segments));
        int dx = target.X - start.X;
        int dy = target.Y - start.Y;
        var control1 = new ScreenPoint(start.X + dx / 3, start.Y + dy / 3);
        var control2 = new ScreenPoint(start.X + 2 * dx / 3, start.Y + 2 * dy / 3);

        var points = new List<ScreenPoint>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double u = 1 - t;
            double x = u * u * u * start.X + 3 * u * u * t * control1.X + 3 * u * t * t * control2.X + t * t * t * target.X;
            double y = u * u * u * start.Y + 3 * u * u * t * control1.Y + 3 * u * t * t * control2.Y + t * t * t * target.Y;
            points.Add(new ScreenPoint((int)Math.Round(x), (int)Math.Round(y)));
        }
        return points;
    }
}
