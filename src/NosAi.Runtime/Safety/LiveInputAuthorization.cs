using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;

namespace NosAi.Runtime.Safety;

public sealed class LiveInputAuthorization
{
    private readonly RuntimeSafetyPolicy _policy;

    public LiveInputAuthorization(RuntimeSafetyPolicy policy)
        => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public bool CanExecute(CandidateAction action, GuardDecision guardDecision, bool clientHealthy)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!_policy.LiveInputEnabled) return false;
        if (_policy.RequireClientHealthy && !clientHealthy) return false;
        if (_policy.RequireGuardApproval && !guardDecision.Allowed) return false;
        return true;
    }
}
