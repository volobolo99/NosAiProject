namespace NosAi.Runtime.Contracts;

public enum TrustTier
{
    Tier1 = 1,
    Tier2 = 2,
    Tier3 = 3,
    Tier4 = 4
}

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
