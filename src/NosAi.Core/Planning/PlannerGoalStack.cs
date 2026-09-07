namespace NosAi.Core.Planning;

/// <summary>
/// The planner's goal stack: fixed capacity, no allocation, identities only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <c>NosAi.Runtime.Autonomy.GoalStack</c></b>, which is the one
/// <c>Gate3ExecutionOrchestrator</c> actually composes. That one holds whole
/// <c>Goal</c> objects — the vnums to look for, the rationale to log — and grows on
/// demand; this one holds <see cref="PlannerGoalId"/> plus <see cref="GoalClass"/>
/// and a timestamp in a pre-sized array, because the planning layer is meant to run
/// without touching the heap.
/// </para>
/// <para>
/// Both were called <c>GoalStack</c> until 2026-09-07, and this is the namespace
/// <c>ModuleReachability</c> declares unreached: writing <c>using</c> on it and
/// naming <c>GoalStack</c> compiled, ran, and quietly used the copy nothing drives.
/// See <c>PIANO_DI_RIORDINO.md § R1</c>.
/// </para>
/// </remarks>
public sealed class PlannerGoalStack
{
    private readonly Entry[] _entries;
    private int _count;

    /// <summary>Creates a stack that can hold that many goals at once.</summary>
    public PlannerGoalStack(int capacity = 8)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new Entry[capacity];
    }

    /// <summary>The goal in force, or <see cref="PlannerGoalId.None"/> when there is none.</summary>
    public PlannerGoalId Active => _count == 0 ? PlannerGoalId.None : _entries[_count - 1].Id;

    /// <summary>The class of the goal in force. Meaningless when the stack is empty.</summary>
    public GoalClass ActiveClass => _count == 0 ? GoalClass.Opportunistic : _entries[_count - 1].Class;

    /// <summary>When the goal in force was pushed, or 0 when the stack is empty.</summary>
    public long ActiveSinceUnixMs => _count == 0 ? 0 : _entries[_count - 1].SinceUnixMs;

    /// <summary>Puts a goal in force. False when it is <see cref="PlannerGoalId.None"/> or the stack is full.</summary>
    public bool TryPush(PlannerGoalId goal, GoalClass cls, long nowUnixMs)
    {
        if (goal == PlannerGoalId.None || _count >= _entries.Length) return false;
        _entries[_count++] = new Entry(goal, cls, nowUnixMs);
        return true;
    }

    /// <summary>Takes the goal in force out. False when there was none.</summary>
    public bool TryPop()
    {
        if (_count == 0) return false;
        _entries[--_count] = default;
        return true;
    }

    private readonly record struct Entry(PlannerGoalId Id, GoalClass Class, long SinceUnixMs);
}
