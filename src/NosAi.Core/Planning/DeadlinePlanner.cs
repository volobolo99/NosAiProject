namespace NosAi.Core.Planning;

public sealed class DeadlinePlanner : IPlanner
{
    private const float SafetyMargin = 0.80f;
    private readonly ushort _timeoutMs;
    private readonly uint _requiredScope;

    public DeadlinePlanner(ushort timeoutMs = 100, uint requiredScope = 0)
    {
        if (timeoutMs == 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
        _timeoutMs = timeoutMs;
        _requiredScope = requiredScope;
    }

    public int Plan(in OrchestrationDecision decision, in WorldState state, Span<PlanStep> steps, out FaultCode fault)
    {
        fault = FaultCode.None;
        if (decision.SelectedActionId == 0 || steps.IsEmpty)
        {
            fault = FaultCode.Timeout;
            return 0;
        }

        long remainingMs = state.UnixMillis <= 0 ? _timeoutMs : long.MaxValue;
        long budgetMs = remainingMs == long.MaxValue ? long.MaxValue : (long)(remainingMs * SafetyMargin);
        if (budgetMs < _timeoutMs)
        {
            fault = FaultCode.Timeout;
            return 0;
        }

        steps[0] = new PlanStep(decision.SelectedActionId, 0, 0, _timeoutMs, _requiredScope);
        return 1;
    }
}
