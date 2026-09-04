namespace NosAi.Core.Planning;

public sealed class GoalStack
{
    private readonly Entry[] _entries;
    private int _count;

    public GoalStack(int capacity = 8)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new Entry[capacity];
    }

    public GoalId Active => _count == 0 ? GoalId.None : _entries[_count - 1].Id;
    public GoalClass ActiveClass => _count == 0 ? GoalClass.Opportunistic : _entries[_count - 1].Class;
    public long ActiveSinceUnixMs => _count == 0 ? 0 : _entries[_count - 1].SinceUnixMs;

    public bool TryPush(GoalId goal, GoalClass cls, long nowUnixMs)
    {
        if (goal == GoalId.None || _count >= _entries.Length) return false;
        _entries[_count++] = new Entry(goal, cls, nowUnixMs);
        return true;
    }

    public bool TryPop()
    {
        if (_count == 0) return false;
        _entries[--_count] = default;
        return true;
    }

    private readonly record struct Entry(GoalId Id, GoalClass Class, long SinceUnixMs);
}
