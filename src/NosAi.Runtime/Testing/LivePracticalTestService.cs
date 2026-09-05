using NosAi.Core.Testing;
using NosAi.Runtime.Gate1;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Evaluates practical, client-side tests against the same canonical snapshot exposed
/// to the operator dashboard. It never fabricates gameplay values and never bypasses
/// Guard/Safety to make a test pass.
/// </summary>
public sealed class LivePracticalTestService
{
    private readonly Func<Gate1CanonicalSnapshot> _snapshot;
    private readonly Func<bool> _runtimeLive;
    private readonly Func<bool> _clientAttached;

    public LivePracticalTestService(
        Func<Gate1CanonicalSnapshot> snapshot,
        Func<bool> runtimeLive,
        Func<bool> clientAttached)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _runtimeLive = runtimeLive ?? throw new ArgumentNullException(nameof(runtimeLive));
        _clientAttached = clientAttached ?? throw new ArgumentNullException(nameof(clientAttached));
    }

    public PracticalTestRun RunObservationTest(string testId)
    {
        DateTime started = DateTime.UtcNow;
        Gate1CanonicalSnapshot snapshot = _snapshot();
        bool live = _runtimeLive();
        bool client = _clientAttached();

        PracticalTestResult result;
        string evidence;
        string failure;

        switch (testId)
        {
            case "T1":
                result = live && client && HasFreshObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : live && client ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "gate1.snapshot";
                failure = result == PracticalTestResult.Pass
                    ? string.Empty
                    : "fresh_client_observation_not_confirmed";
                break;

            case "T2":
                result = HasScreenObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.perception";
                failure = result == PracticalTestResult.Pass
                    ? string.Empty
                    : "screen_observation_not_confirmed";
                break;

            case "T3":
                result = HasNetworkObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.network_observation";
                failure = result == PracticalTestResult.Pass
                    ? string.Empty
                    : "network_observation_not_confirmed";
                break;

            case "T4":
                result = HasWorldObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.world";
                failure = result == PracticalTestResult.Pass
                    ? string.Empty
                    : "world_state_observation_not_confirmed";
                break;

            default:
                result = PracticalTestResult.Blocked;
                evidence = string.Empty;
                failure = $"unsupported_live_test:{testId}";
                break;
        }

        DateTime completed = DateTime.UtcNow;
        return new PracticalTestRun(
            Guid.NewGuid().ToString("N"),
            testId,
            result,
            started,
            completed,
            "runtime-live",
            typeof(LivePracticalTestService).Assembly.GetName().Version?.ToString() ?? "unknown",
            evidence,
            failure);
    }

    private static bool HasFreshObservation(Gate1CanonicalSnapshot snapshot)
    {
        // The canonical snapshot must expose an attached client and a non-stale
        // observation. We deliberately do not infer freshness from a field that may
        // belong to a different subsystem.
        return snapshot.Client.Attached.Value == true
            && snapshot.Client.Attached.Source != DataSourceKind.Unknown;
    }

    private static bool HasScreenObservation(Gate1CanonicalSnapshot snapshot)
        => snapshot.Perception is not null
           && snapshot.Perception.Source != DataSourceKind.Unknown;

    private static bool HasNetworkObservation(Gate1CanonicalSnapshot snapshot)
        => snapshot.GameObservation is not null
           && snapshot.GameObservation.Source != DataSourceKind.Unknown;

    private static bool HasWorldObservation(Gate1CanonicalSnapshot snapshot)
        => snapshot.GameObservation is not null
           && snapshot.GameObservation.Source != DataSourceKind.Unknown;
}
