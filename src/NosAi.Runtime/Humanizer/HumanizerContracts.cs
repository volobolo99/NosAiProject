using NosAi.Runtime.LowLevel;

namespace NosAi.Runtime.Humanizer;

public readonly record struct ScreenPoint(int X, int Y);

public sealed record TargetDescriptor(ScreenPoint Point, double Width, double Height, string Description);

public interface IHumanizer
{
    Task MoveMouseHumanlikeAsync(ScreenPoint start, TargetDescriptor target, CancellationToken cancellationToken);

    /// <summary>Moves to the target and actuates a mouse button.</summary>
    Task ClickTargetAsync(TargetDescriptor target, MouseButton button, CancellationToken cancellationToken);

    Task PressKeyHumanlikeAsync(string key, CancellationToken cancellationToken);
}
