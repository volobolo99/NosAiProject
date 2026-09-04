namespace NosAi.Core.Perception;

public enum PerceptionSource : byte { Network, Memory, Screen, Local }

public readonly record struct Observation<T>(
    T Value,
    PerceptionSource Source,
    float Confidence,
    long ObservedAtUnixMillis)
{
    public bool IsUsable => Confidence is >= 0f and <= 1f;
}

public interface IPerceptionAdapter<T>
{
    bool TryObserve(out Observation<T> observation);
}
