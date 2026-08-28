namespace NosAi.Runtime.WorldModel;

public sealed record EntityState(string Id, string Kind, double X, double Y, double HpRatio);

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
