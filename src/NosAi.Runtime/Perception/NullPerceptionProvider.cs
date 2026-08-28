namespace NosAi.Runtime.Perception;

public sealed class NullPerceptionProvider : IPerceptionProvider
{
    public PerceptionSnapshot Capture() =>
        new(0, true, 1.0, Array.Empty<WorldModel.EntityState>());
}
