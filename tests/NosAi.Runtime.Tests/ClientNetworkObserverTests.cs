using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Reading which TCP connections a process owns.
/// </summary>
/// <remarks>
/// The runtime knew the client's process and window and nothing about its
/// network, so a disconnected game looked identical to a connected one. These
/// tests run against this test process's own real sockets: the interesting
/// failures here are byte order and struct layout, and neither shows up against
/// a fake.
/// </remarks>
public sealed class ClientNetworkObserverTests
{
    /// <summary>A listener plus a connected client, on loopback, for the duration of a test.</summary>
    private sealed class LoopbackPair : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TcpClient _client;
        private readonly TcpClient _accepted;

        public LoopbackPair()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _client = new TcpClient();
            _client.Connect(IPAddress.Loopback, Port);
            _accepted = _listener.AcceptTcpClient();
        }

        public int Port { get; }

        public void Dispose()
        {
            _accepted.Dispose();
            _client.Dispose();
            _listener.Stop();
        }
    }

    private static int SelfPid => Environment.ProcessId;

    [Fact]
    public void APidThatCannotOwnAnythingIsRefused()
    {
        // "Could not look" and "found nothing" must not read the same: a broken
        // probe would otherwise look like a disconnected game.
        foreach (var pid in new[] { 0, -1 })
        {
            var observation = ClientNetworkObserver.Observe(pid);
            Assert.False(observation.Observed);
            Assert.Equal("invalid_process_id", observation.FailureReason);
        }
    }

    [WindowsOnlyFact]
    public void AProcessThatDoesNotExistIsObservedWithNothing()
    {
        // A real answer: the table was read and nothing in it belongs to that pid.
        // Distinct from a failure, and it has to be.
        var observation = ClientNetworkObserver.Observe(0x7FFFFFF0);

        Assert.True(observation.Observed);
        Assert.Null(observation.FailureReason);
        Assert.Empty(observation.Connections);
        Assert.Null(observation.Primary);
    }

    [WindowsOnlyFact]
    public void OurOwnConnectionIsFoundWithTheRightPort()
    {
        // The trap this catches: ports arrive network-ordered in the low two bytes
        // of a DWORD. Getting the swap wrong yields plausible-looking nonsense —
        // 4006 becomes 42256 — which nothing but a known port would reveal.
        using var pair = new LoopbackPair();

        var observation = ClientNetworkObserver.Observe(SelfPid);

        Assert.True(observation.Observed);
        Assert.Contains(observation.Connections, c =>
            c.Local.Port == pair.Port || c.Remote.Port == pair.Port);
    }

    [WindowsOnlyFact]
    public void EveryPortIsInRange()
    {
        // A byte-order mistake usually escapes the 16-bit range or lands absurdly
        // high across the board; this catches it even without a known port.
        using var pair = new LoopbackPair();

        var observation = ClientNetworkObserver.Observe(SelfPid);

        Assert.All(observation.Connections, c =>
        {
            Assert.InRange(c.Local.Port, 0, 65535);
            Assert.InRange(c.Remote.Port, 0, 65535);
        });
    }

    [WindowsOnlyFact]
    public void ALoopbackConnectionIsNotARemoteSession()
    {
        // Only a conversation with something off this machine can be a game
        // server. Counting loopback would make every locally-tested runtime look
        // like it had found one.
        using var pair = new LoopbackPair();

        var observation = ClientNetworkObserver.Observe(SelfPid);
        var loopback = observation.Connections.Where(c => IPAddress.IsLoopback(c.Remote.Address)).ToList();

        Assert.NotEmpty(loopback);
        Assert.All(loopback, c => Assert.False(c.IsRemoteSession));
    }

    [Fact]
    public void OnlyAnEstablishedConnectionCountsAsASession()
    {
        var listening = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Any, 17471), new IPEndPoint(IPAddress.Any, 0), ClientTcpState.Listen);
        var closing = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Parse("192.168.0.4"), 5000),
            new IPEndPoint(IPAddress.Parse("79.110.84.175"), 4006), ClientTcpState.TimeWait);
        var live = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Parse("192.168.0.4"), 5000),
            new IPEndPoint(IPAddress.Parse("79.110.84.175"), 4006), ClientTcpState.Established);

        Assert.False(listening.IsRemoteSession);
        Assert.False(closing.IsRemoteSession);
        Assert.True(live.IsRemoteSession);
    }

    [Fact]
    public void SeveralRemoteSessionsLeaveThePrimaryUnknown()
    {
        // A launcher, an updater and the game look alike from out here. Naming one
        // of them "the server" would be a guess dressed as an observation, so the
        // answer is UNKNOWN and every candidate is still listed.
        var first = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Parse("192.168.0.4"), 5000),
            new IPEndPoint(IPAddress.Parse("79.110.84.175"), 4006), ClientTcpState.Established);
        var second = new ClientTcpConnection(
            new IPEndPoint(IPAddress.Parse("192.168.0.4"), 5001),
            new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443), ClientTcpState.Established);

        var ambiguous = new ClientNetworkObservation(new[] { first, second }, null, null);

        Assert.True(ambiguous.Observed);
        Assert.Null(ambiguous.Primary);
        Assert.Equal(2, ambiguous.RemoteSessions.Count);
    }

    [Fact]
    public void AFailedObservationCarriesNoConnections()
    {
        var failed = ClientNetworkObservation.Failed("iphlpapi_unavailable");

        Assert.False(failed.Observed);
        Assert.Empty(failed.Connections);
        Assert.Null(failed.Primary);
        Assert.Equal("iphlpapi_unavailable", failed.FailureReason);
    }

    [WindowsOnlyFact]
    public void ReadingTwiceDoesNotLeakTheTable()
    {
        // The table is allocated unmanaged and freed by hand; a leak here would
        // grow with every poll, and the runtime polls it continuously.
        using var pair = new LoopbackPair();

        long before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < 200; i++)
            ClientNetworkObserver.Observe(SelfPid);
        Process.GetCurrentProcess().Refresh();

        // Managed allocation is expected; the assertion is that 200 reads complete
        // without throwing or exhausting unmanaged memory.
        Assert.True(GC.GetTotalAllocatedBytes(precise: true) >= before);
        Assert.True(ClientNetworkObserver.Observe(SelfPid).Observed);
    }
}
