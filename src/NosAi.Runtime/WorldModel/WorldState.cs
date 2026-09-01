namespace NosAi.Runtime.WorldModel;

/// <param name="HpRatio">
/// Health as a fraction of the maximum, or null when the entity was seen without
/// it. The wire routinely reports a position with no health (an <c>mv</c> packet
/// carries nothing else), and a zero here would read as a dead mob to anything
/// that plans on this state. Screen perception, which always reads health beside
/// the box, keeps filling it in.
/// </param>
public sealed record EntityState(string Id, string Kind, double X, double Y, double? HpRatio);

public sealed record WorldState(
    long Tick,
    bool PlayerAlive,
    double PlayerHpRatio,
    IReadOnlyList<EntityState> Entities);

public interface IWorldModel
{
    WorldState Current { get; }
    void Update(WorldState state);
}

public sealed class WorldModel : IWorldModel
{
    private WorldState _current = new(0, true, 1.0, Array.Empty<EntityState>());

    public WorldState Current => Volatile.Read(ref _current);

    public void Update(WorldState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Volatile.Write(ref _current, state);
    }
}
