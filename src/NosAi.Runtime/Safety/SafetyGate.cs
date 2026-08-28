using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

public sealed class SafetyGate : ISafetyGate
{
    public bool Authorize(CandidateAction action, GuardDecision guardDecision)
    {
        if (!guardDecision.Allowed)
            return false;

        // Runtime foundation is fail-closed: authorization is represented,
        // but no live game/client execution is performed here yet.
        return false;
    }
}
