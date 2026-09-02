using NosAi.Runtime.Autonomy;

namespace NosAi.Runtime.Contracts;

public enum ActionKind
{
    NoOp,
    Move,
    Combat,
    Recovery,
    Utility
}

public sealed record CandidateAction(
    string Id,
    ActionKind Kind,
    TrustTier RequiredTrustTier,
    double UtilityScore);

public sealed record GuardDecision(
    bool Allowed,
    TrustTier EvaluatedTier,
    string Reason);

public interface IGuardAi
{
    GuardDecision Evaluate(CandidateAction action, TrustTier maxAllowedTier);
}

public interface ISafetyGate
{
    bool Authorize(CandidateAction action, GuardDecision guardDecision);
}

public sealed record AgentStep(string Id, CandidateAction Action);

public sealed record AgentPlan(IReadOnlyList<AgentStep> Steps);

public sealed record VerificationResult(bool Passed, string Reason);

public sealed record AutonomousRuntimeOptions(
    int MaxSteps = 32,
    int MaxRetriesPerStep = 1,
    int MaxReplans = 3,
    int MaxActions = 32);

public interface IAgentPlanner
{
    AgentPlan Plan(object context);
    AgentPlan Replan(object context, int failedStepIndex, string reason);
}

public interface IAgentExecutor
{
    object Execute(CandidateAction action);
}

public interface IAgentVerifier
{
    VerificationResult Verify(CandidateAction expected, object observed);
}
