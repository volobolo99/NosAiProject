using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Guard;

public sealed class GuardAi : IGuardAi
{
    public GuardDecision Evaluate(CandidateAction action, TrustTier maxAllowedTier)
    {
        if (action.Kind == ActionKind.NoOp)
            return new GuardDecision(true, TrustTier.Tier1_Assisted, "NOOP");

        if (action.RequiredTrustTier > maxAllowedTier)
            return new GuardDecision(false, action.RequiredTrustTier, "TRUST_TIER_EXCEEDED");

        return new GuardDecision(true, action.RequiredTrustTier, "ALLOWED");
    }
}
