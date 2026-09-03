using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NosAi.Host;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="EmbeddedControlCenterServer"/> wraps <see cref="System.Net.HttpListener"/>
/// the same trusting way <c>Gate1OperatorServer</c> used to: <c>TryStart</c> treated
/// <c>HttpListener.Start()</c> not throwing as proof a client could connect. These hold
/// it to requiring a real round trip before it reports success, not just a clean API
/// call — see the remarks on <see cref="EmbeddedControlCenterServer"/>.
/// </summary>
public sealed class EmbeddedControlCenterServerTests
{
    // EmbeddedControlCenterServer takes a fixed port and has no ephemeral-port (0)
    // support the way Gate1OperatorServer does, so each test claims its own literal
    // port to stay independent of the others.
    private const int ReachablePort = 8850;
    private const int UnreachablePort = 8851;
    private const int ReleasedPort = 8852;
    private const int BusyPort = 8853;

    private static MasterSystemTelemetry BuildTelemetry() => new(
        SessionId: "test-session",
        TotalTicksCount: 0,
        HostStatus: MasterHostStatus.Running,
        ActiveTrustTier: TrustTier.Tier2_SemiAutonomous,
        GpuTemperatureCelsius: null,
        GpuTemperatureSource: "UNKNOWN",
        CpuUsagePercentage: null,
        CpuUsageSource: "UNKNOWN",
        RamWorkingSetMb: null,
        RamWorkingSetSource: "UNKNOWN",
        VramUsageMb: null,
        VramUsageSource: "UNKNOWN",
        IsGameClientHooked: null,
        GameClientSource: "UNKNOWN",
        IsGuardPhoneConnected: null,
        GuardPhoneSource: "UNKNOWN",
        ActiveMonstersTracked: null,
        ActiveMonstersSource: "UNKNOWN",
        TotalGoldTracked: null,
        TotalGoldSource: "UNKNOWN",
        SnapshotTimestampUtc: DateTime.UtcNow);

    [Fact]
    public async Task ASuccessfulStartIsActuallyReachable()
    {
        await using var server = new EmbeddedControlCenterServer(ReachablePort, BuildTelemetry, _ => { });

        Assert.True(server.TryStart(out var failureReason));
        Assert.Null(failureReason);
        Assert.Equal(ReachablePort, server.BoundPort);

        // The real assertion: not that TryStart says yes, but that a separate
        // client can actually complete a request against the bound port.
        using var client = new HttpClient();
        using var response = await client.GetAsync($"http://127.0.0.1:{server.BoundPort}/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AListenerThatStartsButCannotBeReachedIsReportedNotSilentlyAccepted()
    {
        await using var server = new EmbeddedControlCenterServer(
            UnreachablePort, BuildTelemetry, _ => { }, verifyReachable: _ => false);

        Assert.False(server.TryStart(out var failureReason));
        Assert.NotNull(failureReason);
        Assert.StartsWith("control_center_bind_unreachable:", failureReason);
        Assert.Null(server.BoundPort);
    }

    [Fact]
    public async Task AFailedVerificationActuallyReleasesThePort()
    {
        // A port reported as "not bound" must not still be quietly listening in
        // the background afterwards: the failure has to be real, not just a
        // relabelled success.
        await using var server = new EmbeddedControlCenterServer(
            ReleasedPort, BuildTelemetry, _ => { }, verifyReachable: _ => false);

        Assert.False(server.TryStart(out var failureReason));
        Assert.Equal($"control_center_bind_unreachable:{ReleasedPort}", failureReason);

        // Whether the released port then refuses fast or simply never answers is
        // an OS/network-stack detail this test does not pin down. Either way
        // proves the same thing DefaultVerifyReachable itself relies on: the
        // request does not complete, so nothing is really being served.
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetAsync($"http://127.0.0.1:{ReleasedPort}/"));
    }

    [Fact]
    public async Task ABusyFixedPortStillReportsInUseWithoutAttemptingVerification()
    {
        // Guards the pre-existing failure path: a real bind conflict must still
        // be reported as one, rather than being reclassified as unreachable now
        // that TryStart also verifies.
        await using var blocker = new EmbeddedControlCenterServer(BusyPort, BuildTelemetry, _ => { });
        Assert.True(blocker.TryStart(out _));

        await using var server = new EmbeddedControlCenterServer(BusyPort, BuildTelemetry, _ => { });
        Assert.False(server.TryStart(out var failureReason));
        Assert.Equal($"control_center_port_in_use:{BusyPort}", failureReason);
        Assert.Null(server.BoundPort);
    }
}
