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
