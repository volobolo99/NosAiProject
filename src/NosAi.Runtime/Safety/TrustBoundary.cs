using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

/// <summary>
/// The runtime's current autonomy level, which may fall but never rise here.
/// </summary>
/// <remarks>
/// <see cref="DowngradeTrust"/> is one-way by construction: recovery from a
/// degraded state is a decision for whoever is watching, not something the
/// failing component grants itself.
/// </remarks>
public sealed class TrustBoundary
{
    private TrustTier _currentTrust;
    private readonly object _lock = new();

    public TrustTier CurrentTier
    {
        get
        {
            lock (_lock)
                return _currentTrust;
        }
    }

    public TrustBoundary(TrustTier initialTier = TrustTier.Tier2_SemiAutonomous) =>
        _currentTrust = initialTier;

    public bool IsAuthorized(TrustTier requiredTier)
    {
        lock (_lock)
            return _currentTrust >= requiredTier;
    }

    public void DowngradeTrust(TrustTier newTier)
    {
        lock (_lock)
        {
            if (newTier < _currentTrust)
                _currentTrust = newTier;
        }
    }
}
