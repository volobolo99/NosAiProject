using NosAi.ControlPanel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Configuration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Hardware;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class GameObservationPanelTests
{
    [Fact]
    public void Observe_game_round_trips_through_host_options()
    {
        var settings = new OperatorSettings
        {
            DashboardPort = 8766,
            GuardPort = 17471,
            ObserveGame = "79.110.84.175:4002"
        };

        Assert.True(OperatorSettings.TryValidate(
            settings.DashboardPort, settings.GuardPort, settings.OperationTimeoutMs,
            settings.ClientProcessName, settings.ObserveGame, out var error));
        Assert.Equal("", error);

        Gate1HostOptions options = settings.ToHostOptions();
        Assert.Equal("79.110.84.175", options.ObserveGame!.Host);
        Assert.Equal(4002, options.ObserveGame.Port);
    }

    [Fact]
    public void Empty_observe_game_does_not_pass_the_flag()
    {
        var settings = new OperatorSettings { ObserveGame = "" };
        Gate1HostOptions options = settings.ToHostOptions();
        Assert.Null(options.ObserveGame);
    }

    [Fact]
    public void Malformed_observe_game_is_rejected_before_save()
    {
        Assert.False(OperatorSettings.TryValidate(8766, 17471, 5000, "NostaleClientX", "noport", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Network_inspect_shows_configured_endpoint_or_unknown()
    {
        var off = new OperatorSettings { ObserveGame = "" };
        IReadOnlyList<DisplayField> offFields = NetworkInspect.Inspect(off, SessionKind.Idle, null, null, null, null);
        DisplayField offEndpoint = offFields.Single(f => f.Label == "Endpoint osservazione gioco");
        Assert.Equal("UNKNOWN", offEndpoint.Source);
        Assert.Contains("observation_not_configured", offEndpoint.Value);

        var on = new OperatorSettings { ObserveGame = "127.0.0.1:4002" };
        IReadOnlyList<DisplayField> onFields = NetworkInspect.Inspect(on, SessionKind.Idle, null, null, null, null);
        DisplayField onEndpoint = onFields.Single(f => f.Label == "Endpoint osservazione gioco");
        Assert.Equal("DERIVED", onEndpoint.Source);
        Assert.Equal("127.0.0.1:4002", onEndpoint.Value);
    }

    [Fact]
    public void Empty_snapshot_does_not_invent_observation_counts()
    {
        SnapshotView view = SnapshotView.Empty("offline");
        Assert.All(view.GameObservation, f => Assert.Equal("UNKNOWN", f.Source));
        Assert.All(view.GameObservation, f => Assert.Contains("UNKNOWN", f.Value, StringComparison.Ordinal));
        Assert.DoesNotContain(view.GameObservation, f => f.Value == "0");
    }

    [Fact]
    public void Attached_snapshot_without_observation_block_is_unknown_with_reason()
    {
        var json = """
            {
              "contractVersion": "gate1.snapshot.v1",
              "runtimeStatus": "Degraded"
            }
            """;

        SnapshotView view = AttachedSnapshot.Parse(json);
        Assert.Contains(view.GameObservation, f => f.Label == "Canale osservazione gioco");
        Assert.All(view.GameObservation, f =>
        {
            Assert.Equal("UNKNOWN", f.Source);
            Assert.Contains("game_observation_absent", f.Value);
        });
    }

    [Fact]
    public void Attached_snapshot_renders_classified_observation_fields()
    {
        var json = """
            {
              "contractVersion": "gate1.snapshot.v1",
              "runtimeStatus": "Degraded",
              "gameObservation": {
                "active": { "value": false, "source": "DERIVED", "hasObservedValue": true },
                "endpoint": { "value": "79.110.84.175:4002", "source": "DERIVED", "hasObservedValue": true },
                "packetsObserved": { "source": "UNKNOWN", "failureReason": "windivert_dll_not_found" },
                "packetsDecoded": { "source": "UNKNOWN", "failureReason": "windivert_dll_not_found" },
                "packetsUndecodable": { "source": "UNKNOWN", "failureReason": "windivert_dll_not_found" },
                "lastHp": { "source": "UNKNOWN", "failureReason": "gameplay_provider_not_available" },
                "lastMaxHp": { "source": "UNKNOWN", "failureReason": "gameplay_provider_not_available" },
                "lastMp": { "source": "UNKNOWN", "failureReason": "gameplay_provider_not_available" },
                "lastVitalsAtUtc": { "source": "UNKNOWN", "failureReason": "gameplay_provider_not_available" }
              }
            }
            """;

        SnapshotView view = AttachedSnapshot.Parse(json);
        DisplayField packets = view.GameObservation.Single(f => f.Label == "Pacchetti osservati");
        Assert.Equal("UNKNOWN", packets.Source);
        Assert.Contains("windivert_dll_not_found", packets.Value);
        Assert.DoesNotContain(view.GameObservation, f => f.Value == "0");
        DisplayField endpoint = view.GameObservation.Single(f => f.Label == "Endpoint osservato");
        Assert.Equal("DERIVED", endpoint.Source);
        Assert.Contains("79.110.84.175:4002", endpoint.Value);
    }

    [Fact]
    public void Snapshot_view_from_not_configured_keeps_unknown_reasons()
    {
        var view = SnapshotView.From(Gate1SnapshotFactory.Create(
            RuntimeHealthStatus.Degraded,
            "test",
            new LiveHardwareTelemetry(new FallbackHardwareProbe()).Capture().View,
            new ClientBaselineSnapshot(
                ProcessDetected: false,
                WindowDetected: false,
                ClientAttached: false,
                ProcessId: null,
                WindowHandle: IntPtr.Zero,
                Source: "test",
                ObservedAtUtc: DateTime.UtcNow,
                Availability: ClientBaselineAvailability.Unavailable,
                Status: "client_unavailable",
                Warning: null,
                FailureReason: "connector_not_bound"),
            new Gate1ConnectionSnapshot(string.Empty, false, false, default, null),
            NosAi.Runtime.Safety.RuntimeSafetyPolicy.SafeDefault));

        Assert.Contains(view.GameObservation, f =>
            f.Label == "Endpoint osservato" && f.Source == "UNKNOWN" && f.Value.Contains("observation_not_configured"));
    }
}
