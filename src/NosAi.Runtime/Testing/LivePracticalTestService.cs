using NosAi.Core.Testing;
using NosAi.Runtime.Gate1;

namespace NosAi.Runtime.Testing;

/// <summary>
/// Evaluates practical client-side tests against the same canonical snapshot exposed
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
                result = live && client && HasClientObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : live && client ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "gate1.snapshot.client";
                failure = result == PracticalTestResult.Pass ? string.Empty : "client_observation_not_confirmed";
                break;

            case "T2":
                result = HasScreenObservation(snapshot)
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.client";
                failure = result == PracticalTestResult.Pass ? string.Empty : "screen_observation_not_confirmed";
                break;

            case "T3":
                result = snapshot.GameObservation.Active.Value == true
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.gameObservation";
                failure = result == PracticalTestResult.Pass ? string.Empty : "network_observation_not_confirmed";
                break;

            case "T4":
                result = snapshot.GameObservation.PacketsDecoded.Value > 0
                    ? PracticalTestResult.Pass
                    : PracticalTestResult.Unknown;
                evidence = "gate1.snapshot.gameObservation";
                failure = result == PracticalTestResult.Pass ? string.Empty : "world_observation_not_confirmed";
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

    private static bool HasClientObservation(Gate1CanonicalSnapshot snapshot)
        => snapshot.Client.Attached.Value == true;

    private static bool HasScreenObservation(Gate1CanonicalSnapshot snapshot)
        // Screen capture is deliberately not inferred from gameplay packets.
        // The current canonical snapshot does not yet carry a screen-provider flag.
        => snapshot.Client.WindowDetected.Value == true
           && snapshot.Client.WindowVisible.Value == true;
}
