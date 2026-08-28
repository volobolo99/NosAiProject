using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Perception;

public sealed record PerceptionSnapshot(
    long Tick,
    bool PlayerAlive,
    double PlayerHpRatio,
    IReadOnlyList<EntityState> Entities);

public interface IPerceptionProvider
{
    PerceptionSnapshot Capture();
}

public interface IPerceptionWorldAdapter
{
    WorldState ToWorldState(PerceptionSnapshot snapshot);
}

public sealed class PerceptionWorldAdapter : IPerceptionWorldAdapter
{
    public WorldState ToWorldState(PerceptionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new WorldState(snapshot.Tick, snapshot.PlayerAlive, snapshot.PlayerHpRatio, snapshot.Entities.ToArray());
    }
}
