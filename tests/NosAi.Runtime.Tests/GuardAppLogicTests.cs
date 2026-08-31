using System.Text.Json;
using NosAi.GuardClient;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The two decisions the phone application makes, tested where they can be.
/// </summary>
/// <remarks>
/// Both live in <c>NosAi.GuardClient</c> rather than in the MAUI project on
/// purpose. The application has no test host, so logic left inside it is logic
/// only a person with the phone in hand can check — and rendering rules that are
/// never checked are how a screen ends up asserting a safety property nobody
/// verified.
/// </remarks>
public sealed class GuardAppLogicTests
{
    // ---------------------------------------------------------------- reconnect

    [Theory]
    [InlineData("authentication_refused")]
    [InlineData("runtime_proof_rejected")]
    [InlineData("unsupported_contract_version")]
    [InlineData("invalid_header")]
    [InlineData("decrypt_failed")]
    [InlineData("cipher_unavailable")]
    [InlineData("sequence_violation")]
    public void AFailureThatNeedsAPersonIsNotRetried(string reason)
    {
        // Retrying a refusal every few seconds turns the one message that needs a
        // person into a scrolling one, and the operator never sees why.
        var policy = new GuardReconnectPolicy();

        Assert.True(GuardReconnectPolicy.IsTerminal(reason));
        Assert.Equal(ReconnectDecision.Stop, policy.OnFailure(reason, out var delay));
        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Theory]
    [InlineData("connect_failed")]
    [InlineData("peer_disconnected")]
    [InlineData("receive_failed")]
    [InlineData("receive_timeout")]
    [InlineData("send_failed")]
    [InlineData("discovery_empty")]
    [InlineData(null)]
    public void AFailureThatMayPassIsRetried(string? reason)
    {
        // A runtime that has not started, a Wi-Fi handover, a reverse tunnel that
        // dropped: a phone on a desk should recover from these untouched.
        var policy = new GuardReconnectPolicy();

        Assert.False(GuardReconnectPolicy.IsTerminal(reason));
        Assert.Equal(ReconnectDecision.Retry, policy.OnFailure(reason, out var delay));
        Assert.True(delay > TimeSpan.Zero);
    }

    [Fact]
    public void TheDelayGrowsAndThenStops()
    {
        // Unbounded doubling would mean a phone left overnight finds the runtime
        // an hour after it came back.
        Assert.Equal(GuardReconnectPolicy.InitialDelay, GuardReconnectPolicy.DelayFor(1));
        Assert.True(GuardReconnectPolicy.DelayFor(2) > GuardReconnectPolicy.DelayFor(1));
        Assert.True(GuardReconnectPolicy.DelayFor(3) > GuardReconnectPolicy.DelayFor(2));
        Assert.Equal(GuardReconnectPolicy.MaxDelay, GuardReconnectPolicy.DelayFor(50));

        // And never wraps, however long the phone has been running.
        Assert.Equal(GuardReconnectPolicy.MaxDelay, GuardReconnectPolicy.DelayFor(int.MaxValue));
    }

    [Fact]
    public void TheBackoffRestartsAfterASessionOpens()
    {
        var policy = new GuardReconnectPolicy();
        policy.OnFailure("connect_failed", out _);
        policy.OnFailure("connect_failed", out var second);

        policy.OnSuccess();
        policy.OnFailure("connect_failed", out var afterSuccess);

        Assert.Equal(0, GuardReconnectPolicy.DelayFor(1).CompareTo(afterSuccess));
        Assert.True(second > afterSuccess);
        Assert.Equal(1, policy.Attempt);
    }

    // ----------------------------------------------------------------- snapshot

    private static string Snapshot(
        string executionMode = "disabled_in_gate1",
        string gameplaySource = "UNKNOWN",
        string? gameplayValue = null)
    {
        var gameplay = gameplayValue is null
            ? $$"""{"value":null,"source":"{{gameplaySource}}"}"""
            : $$"""{"value":"{{gameplayValue}}","source":"{{gameplaySource}}"}""";

        return $$"""
        {
          "contractVersion": "gate1.snapshot.v1",
          "runtimeStatus": "Healthy",
          "capturedAtUtc": "2026-08-31T09:15:00Z",
          "client": {
            "status": "attached_os_session",
            "processName": {"value":"NostaleClientX","source":"LIVE"},
            "processId": {"value":7932,"source":"LIVE"},
            "windowTitle": {"value":"Nostale","source":"LIVE"},
            "processResponding": {"value":true,"source":"LIVE"},
            "windowVisible": {"value":true,"source":"LIVE"},
            "gameplayBaseline": {{gameplay}}
          },
          "safety": {
            "executionMode": {"value":"{{executionMode}}","source":"LIVE"},
            "liveInputEnabled": {"value":false,"source":"LIVE"},
            "packetInjectionEnabled": {"value":false,"source":"LIVE"}
          }
        }
        """;
    }

    [Fact]
    public void TheSnapshotIsReadIntoClassifiedFields()
    {
        var view = GuardSnapshotView.Parse(Snapshot());

        Assert.Equal("Healthy", view.RuntimeStatus);
        Assert.Equal("attached_os_session", view.ClientStatus);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 9, 15, 0, TimeSpan.Zero), view.CapturedAtUtc);

        var process = Assert.Single(view.Client, f => f.Name == "Processo");
        Assert.Equal("NostaleClientX [LIVE]", process.Display);
    }

    [Fact]
    public void UnknownIsRenderedAsUnknownAndNeverAsAValue()
    {
        // The invariant the whole classification exists for: on a phone, a zero or
        // a dash is indistinguishable from a real measurement.
        var view = GuardSnapshotView.Parse(Snapshot());
        var gameplay = Assert.Single(view.Client, f => f.Name == "Gameplay");

        Assert.False(gameplay.IsKnown);
        Assert.Null(gameplay.Value);
        Assert.Equal("UNKNOWN", gameplay.Display);
    }

    [Fact]
    public void AValueWithoutASourceIsNotTreatedAsAReading()
    {
        // An unlabelled reading is exactly what the classification prevents, so it
        // is collapsed to UNKNOWN rather than shown bare.
        const string json = """
        {"client":{"processName":{"value":"Something"}},"safety":{}}
        """;

        var view = GuardSnapshotView.Parse(json);
        var process = Assert.Single(view.Client, f => f.Name == "Processo");

        Assert.False(process.IsKnown);
        Assert.Equal("UNKNOWN", process.Display);
    }

    [Fact]
    public void TheExecutionModeIsReadFromTheSnapshot()
    {
        // The screen used to assert that input and injection were off. It is a
        // property only the runtime is authoritative for (ADR-0003), so it is read.
        Assert.True(GuardSnapshotView.Parse(Snapshot()).ExecutionDisabled);
        Assert.False(GuardSnapshotView.Parse(Snapshot(executionMode: "enabled")).ExecutionDisabled);
    }

    [Fact]
    public void AnAbsentSafetySectionDoesNotMeanSafe()
    {
        // Silence about a safety property is not a statement that it holds. Null,
        // not true — a snapshot that says nothing must not read as "execution off".
        var view = GuardSnapshotView.Parse("""{"client":{},"safety":{}}""");

        Assert.Null(view.ExecutionDisabled);
        Assert.All(view.Safety, field => Assert.False(field.IsKnown));
    }

    [Fact]
    public void AMissingClientSectionYieldsUnknownFieldsRatherThanThrowing()
    {
        // A snapshot the runtime shortened must degrade to UNKNOWN, not crash the
        // screen: the operator needs to see the link, even a poor one.
        var view = GuardSnapshotView.Parse("""{"runtimeStatus":"Degraded"}""");

        Assert.Equal("Degraded", view.RuntimeStatus);
        Assert.All(view.Client, field => Assert.Equal("UNKNOWN", field.Display));
        Assert.Null(view.ExecutionDisabled);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void SomethingThatIsNotASnapshotIsRefused(string payload)
    {
        var refused = Assert.Throws<GuardProtocolException>(() => GuardSnapshotView.Parse(payload));
        Assert.Equal("invalid_telemetry", refused.Reason);
    }

    [Fact]
    public void TheParsedFieldNamesMatchWhatTheRuntimeActuallyPublishes()
    {
        // Guards against the quiet failure: a renamed wire field would turn every
        // reading into UNKNOWN, and UNKNOWN is a legitimate answer, so nothing
        // would look broken.
        using var document = JsonDocument.Parse(Snapshot());
        var client = document.RootElement.GetProperty("client");
        var safety = document.RootElement.GetProperty("safety");

        foreach (var name in new[] { "processName", "processId", "windowTitle", "processResponding", "windowVisible", "gameplayBaseline" })
            Assert.True(client.TryGetProperty(name, out _), name);
        foreach (var name in new[] { "executionMode", "liveInputEnabled", "packetInjectionEnabled" })
            Assert.True(safety.TryGetProperty(name, out _), name);

        var view = GuardSnapshotView.Parse(Snapshot());
        Assert.All(view.Safety, field => Assert.True(field.IsKnown));
    }
}
