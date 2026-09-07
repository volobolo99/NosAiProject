namespace NosAi.Core.Planning;

public readonly record struct OrchestrationDecision(
    PlannerGoalId ActiveGoal,
    GoalClass Class,
    ushort SelectedActionId,
    float Confidence,
    OrchestrationReason Reason);

public enum OrchestrationReason : byte
{
    Continuation = 0,
    Preemption = 1,
    HysteresisHold = 2,
    NoViableAction = 3
}

public interface IOrchestrator
{
    OrchestrationDecision Decide(in PlannerWorldState state, ReadOnlySpan<PlannerRankedAction> ranked, long nowUnixMs);
}

/// <summary>One action id the ranking layer scored, and where it placed.</summary>
/// <remarks>
/// <b>Not <c>NosAi.Runtime.Tactical.RankedAction</c></b>, which is what the tactical
/// ranking actually produces today: that one carries a whole <c>CandidateAction</c>
/// and a <see cref="double"/> score. This one carries an id and a
/// <see cref="float"/>, so a ranked list can be passed to <see cref="IOrchestrator"/>
/// as a span over a stack buffer. The two live in different assemblies and cannot be
/// unified without giving <c>NosAi.Core</c> a dependency it does not have.
/// <para>
/// Both were called <c>RankedAction</c> until 2026-09-07. See
/// <c>PIANO_DI_RIORDINO.md § R1</c>.
/// </para>
/// </remarks>
public readonly record struct PlannerRankedAction(ushort ActionId, float Utility, byte Rank);
