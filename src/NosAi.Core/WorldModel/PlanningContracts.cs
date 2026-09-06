namespace NosAi.Core.WorldModel;

/// <summary>
/// Whether an issued <see cref="WorldAction"/> has been verified to have
/// succeeded. Has no "Unknown" member by design (see
/// <see cref="TileTraversability"/>'s remarks): an action whose outcome has
/// not yet been observed is expressed by wrapping this enum in
/// <see cref="WorldFact{T}.Unknown"/> instead, distinct from
/// <see cref="InProgress"/> (outcome not yet observable because the action
/// has not finished) and from <see cref="Failed"/> (observed and did not
/// succeed).
/// </summary>
public enum ActionOutcome
{
    InProgress = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// One action recorded by the World Model -- planned, executed and/or
/// verified. This is the World Model's record of "what happened", not the
/// planner itself: HTN/GOAP decomposition and candidate scoring belong to
/// AP-08's Strategic Autonomy planning algorithms, which produce and
/// consume these records rather than being defined by them.
/// </summary>
public sealed record WorldAction(
    ActionId Id,
    WorldFact<string> Kind,
    WorldFact<DateTime> IssuedAtUtc,
    WorldFact<ActionOutcome> Outcome);

/// <summary>
/// One active strategic objective (docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md
/// S:4.8, e.g. "survive > recover > complete urgent quest > progress
/// build"). <see cref="Priority"/> is a relative rank among simultaneously
/// active goals, not an absolute score; the Strategic Orchestrator (AP-08)
/// owns how priorities are computed and reconciled.
/// </summary>
public sealed record Goal(
    GoalId Id,
    WorldFact<string> Description,
    WorldFact<int> Priority);
