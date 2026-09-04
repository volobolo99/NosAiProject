namespace NosAi.Core.Planning;

public interface IPlanner
{
    int Plan(in OrchestrationDecision decision, in WorldState state, Span<PlanStep> steps, out FaultCode fault);
}
