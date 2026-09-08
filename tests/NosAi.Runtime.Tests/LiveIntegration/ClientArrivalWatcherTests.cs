using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Drives <see cref="ClientArrivalWatcher"/> through its injected seams, so the whole
/// suite runs headless: no game client, no real process enumeration, no TCP connection
/// table and no wall-clock dependency.
/// </summary>
public sealed class ClientArrivalWatcherTests
{
    [Fact]
    public void Poll_NoProcess_WaitsForProcess()
    {
        var watcher = new ClientArrivalWatcher(processLookup: Pids());
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.WaitingForProcess, status.Stage);
        Assert.Null(status.Endpoint);
        Assert.False(status.Identified);
    }

    [Fact]
    public void Poll_SeveralProcesses_IsAmbiguous()
    {
        var watcher = new ClientArrivalWatcher(processLookup: Pids(10, 20));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.SessionAmbiguous, status.Stage);
        Assert.Null(status.Endpoint);
        Assert.False(status.Identified);
    }

    [Fact]
    public void Poll_SingleProcessNoRemoteSession_IsProcessWithoutSession()
    {
        var watcher = new ClientArrivalWatcher(processLookup: Pids(10), networkLookup: _ => Obs());
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.ProcessWithoutSession, status.Stage);
        Assert.Equal(10, status.ProcessId);
        Assert.Null(status.Endpoint);
    }

    [Fact]
    public void Poll_SingleProcessSeveralRemoteSessions_IsAmbiguous()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => Obs(Remote("79.110.84.175", 4006), Remote("79.110.84.176", 4006)));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.SessionAmbiguous, status.Stage);
        Assert.Null(status.Endpoint);
        Assert.Equal(2, status.RemoteSessionCount);
    }

    [Fact]
    public void Poll_SingleProcessSingleRemoteSession_IdentifiesSession()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => Obs(Remote("79.110.84.175", 4006)));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.SessionIdentified, status.Stage);
        Assert.True(status.Identified);
        Assert.Equal("79.110.84.175", status.Endpoint!.Address.ToString());
        Assert.Equal(4006, status.Endpoint!.Port);
        Assert.Equal(10, status.ProcessId);
        Assert.Null(status.Reason);
    }

    [Fact]
    public void Poll_ProcessLookupThrows_ReportsObservationFailed()
    {
        var watcher = new ClientArrivalWatcher(processLookup: () => throw new InvalidOperationException("boom"));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.ObservationFailed, status.Stage);
        Assert.NotNull(status.Reason);
    }

    [Fact]
    public void Poll_NetworkLookupThrows_ReportsObservationFailed()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => throw new InvalidOperationException("boom"));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.ObservationFailed, status.Stage);
        Assert.Equal(10, status.ProcessId);
        Assert.NotNull(status.Reason);
    }

    [Fact]
    public void Poll_NetworkObservationFailed_ReportsObservationFailed()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => ClientNetworkObservation.Failed("tabella non leggibile"));
        var status = watcher.Poll();
        Assert.Equal(ClientArrivalStage.ObservationFailed, status.Stage);
        Assert.NotNull(status.Reason);
    }

    [Fact]
    public void Poll_StampsObservedUtcFromInjectedClock()
    {
        var fixedUtc = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var watcher = new ClientArrivalWatcher(processLookup: Pids(), clock: FixedClock(fixedUtc));
        var status = watcher.Poll();
        Assert.Equal(fixedUtc, status.ObservedUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositivePollInterval_Throws(int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ClientArrivalWatcher(pollInterval: TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void WaitForClient_NonPositiveTimeout_Throws()
    {
        var watcher = new ClientArrivalWatcher(processLookup: Pids());
        Assert.Throws<ArgumentOutOfRangeException>(() => watcher.WaitForClient(TimeSpan.Zero));
    }

    [Fact]
    public void WaitForClient_SessionAlreadyIdentified_ReturnsImmediately()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => Obs(Remote("79.110.84.175", 4006)),
            pollInterval: TimeSpan.FromMilliseconds(10));
        var status = watcher.WaitForClient(TimeSpan.FromSeconds(1));
        Assert.True(status.Identified);
        Assert.Equal(ClientArrivalStage.SessionIdentified, status.Stage);
    }

    [Fact]
    public void WaitForClient_CancelledToken_ReturnsLastStatusWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var watcher = new ClientArrivalWatcher(processLookup: Pids(), pollInterval: TimeSpan.FromMilliseconds(10));
        var status = watcher.WaitForClient(TimeSpan.FromSeconds(5), cts.Token);
        Assert.NotNull(status);
        Assert.False(status.Identified);
    }

    [Fact]
    public void WaitForClient_InvokesOnStatusWithFirstStatus()
    {
        var seen = new List<ClientArrivalStatus>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var watcher = new ClientArrivalWatcher(processLookup: Pids(), pollInterval: TimeSpan.FromMilliseconds(10));
        watcher.WaitForClient(TimeSpan.FromSeconds(5), cts.Token, seen.Add);
        Assert.NotEmpty(seen);
    }

    [Fact]
    public void WaitForClient_NullOnStatus_DoesNotThrow()
    {
        var watcher = new ClientArrivalWatcher(
            processLookup: Pids(10),
            networkLookup: _ => Obs(Remote("79.110.84.175", 4006)),
            pollInterval: TimeSpan.FromMilliseconds(10));
        var status = watcher.WaitForClient(TimeSpan.FromSeconds(1), CancellationToken.None, onStatus: null);
        Assert.True(status.Identified);
    }

    [Fact]
    public void WaitForClient_StopsPollingOnceIdentified_DoesNotPollAgain()
    {
        int calls = 0;
        Func<IReadOnlyList<int>> lookup = () => { calls++; return new[] { 10 }; };
        var watcher = new ClientArrivalWatcher(
            processLookup: lookup,
            networkLookup: _ => Obs(Remote("79.110.84.175", 4006)),
            pollInterval: TimeSpan.FromMilliseconds(10));
        var status = watcher.WaitForClient(TimeSpan.FromSeconds(1));
        Assert.True(status.Identified);
        Assert.Equal(1, calls);
    }

    private static ClientTcpConnection Remote(string ip, int port) =>
        new(new IPEndPoint(IPAddress.Parse("192.168.1.10"), 5000),
            new IPEndPoint(IPAddress.Parse(ip), port),
            ClientTcpState.Established);

    private static ClientNetworkObservation Obs(params ClientTcpConnection[] connections) =>
        new(connections, connections.Length == 1 ? connections[0] : null, null);

    private static Func<IReadOnlyList<int>> Pids(params int[] ids) => () => ids;

    private static Func<DateTime> FixedClock(DateTime utc) => () => utc;
}
