namespace NosAi.Core.Planning;

public readonly record struct OrchestrationDecision(
    GoalId ActiveGoal,
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
    OrchestrationDecision Decide(in WorldState state, ReadOnlySpan<RankedAction> ranked, long nowUnixMs);
}

public readonly record struct RankedAction(ushort ActionId, float Utility, byte Rank);
