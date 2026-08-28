namespace NosAi.Runtime.Humanizer;

public readonly record struct ScreenPoint(int X, int Y);

public sealed record TargetDescriptor(ScreenPoint Point, double Width, double Height, string Description);

public interface IHumanizer
{
    Task MoveMouseHumanlikeAsync(ScreenPoint start, TargetDescriptor target, CancellationToken cancellationToken);
    Task PressKeyHumanlikeAsync(string key, CancellationToken cancellationToken);
}
