namespace NosAi.Core.Planning;

public sealed class LexicographicOrchestrator : IOrchestrator
{
    private const long MinimumActiveMs = 750;
    private const float Hysteresis = 0.15f;
    private readonly GoalStack _goals;

    public LexicographicOrchestrator(GoalStack goals)
    {
        _goals = goals ?? throw new ArgumentNullException(nameof(goals));
    }

    public OrchestrationDecision Decide(in WorldState state, ReadOnlySpan<RankedAction> ranked, long nowUnixMs)
    {
        if (ranked.IsEmpty || _goals.Active == GoalId.None)
            return new OrchestrationDecision(_goals.Active, _goals.ActiveClass, 0, 0f, OrchestrationReason.NoViableAction);

        RankedAction best = ranked[0];
        for (int i = 1; i < ranked.Length; i++)
        {
            var candidate = ranked[i];
            if (candidate.Utility > best.Utility ||
                (candidate.Utility == best.Utility && candidate.ActionId < best.ActionId))
                best = candidate;
        }

        long activeFor = Math.Max(0, nowUnixMs - _goals.ActiveSinceUnixMs);
        bool hold = activeFor < MinimumActiveMs;
        var reason = hold ? OrchestrationReason.HysteresisHold : OrchestrationReason.Continuation;
        return new OrchestrationDecision(_goals.Active, _goals.ActiveClass, best.ActionId, best.Utility, reason);
    }
}
