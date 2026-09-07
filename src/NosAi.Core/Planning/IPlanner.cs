namespace NosAi.Core.Planning;

public interface IPlanner
{
    int Plan(in OrchestrationDecision decision, in PlannerWorldState state, Span<PlanStep> steps, out FaultCode fault);
}
