namespace NosAi.Core.Planning;

/// <summary>The planner's own handle for a goal: a number, not a name.</summary>
/// <remarks>
/// <b>Not <c>NosAi.Core.WorldModel.GoalId</c>.</b> That one wraps a non-empty
/// <see cref="string"/> and identifies a strategic goal the World Model carries;
/// this one is a <see cref="uint"/> so the planning layer can compare, store and
/// pass goals without allocating, and so <c>default</c> can mean
/// <see cref="None"/>. Both were called <c>GoalId</c> until 2026-09-07, in two
/// namespaces of the same assembly, which meant a <c>using NosAi.Core.Planning;</c>
/// silently took this one. See <c>PIANO_DI_RIORDINO.md § R1</c>.
/// </remarks>
public readonly record struct PlannerGoalId(uint Value)
{
    /// <summary>No goal. Distinct from goal zero, which cannot be pushed.</summary>
    public static PlannerGoalId None => default;

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}

/// <summary>What kind of goal it is, in order of the claim it makes on the runtime.</summary>
public enum GoalClass : byte
{
    /// <summary>Worth doing if nothing else is asking.</summary>
    Opportunistic = 0,

    /// <summary>What the runtime was asked to accomplish.</summary>
    Objective = 1,

    /// <summary>Staying alive, which outranks the objective.</summary>
    Survival = 2,

    /// <summary>Not doing harm, which outranks staying alive.</summary>
    Safety = 3
}
