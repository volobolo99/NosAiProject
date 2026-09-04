namespace NosAi.Core.Navigation;

public readonly record struct NavigationPoint(float X, float Y);

public readonly record struct NavigationPath(ReadOnlyMemory<NavigationPoint> Points)
{
    public bool IsValid => !Points.IsEmpty;
}

public interface INavigationPlanner
{
    bool TryFindPath(in NavigationPoint start, in NavigationPoint goal, Span<NavigationPoint> output, out int count);
}
