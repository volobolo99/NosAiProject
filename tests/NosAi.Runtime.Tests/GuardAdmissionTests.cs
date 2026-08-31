using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using NosAi.GuardClient;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Orchestration;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Who may hold the single Guard session slot (ADR-0011).
/// </summary>
/// <remarks>
/// The channel serves one phone. Over USB the cable bounded who could connect;
/// on a LAN anything can, so two rules have to be enforced rather than assumed:
/// an unauthenticated peer is a candidate and not the owner, and a peer that
/// cannot authenticate must not be able to keep the paired phone out.
/// </remarks>
public sealed class GuardAdmissionTests
{
    private readonly ITestOutputHelper _output;

    public GuardAdmissionTests(ITestOutputHelper output) => _output = output;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>A channel that can complete a handshake and answer with a snapshot.</summary>
    private static GuardAiNetworkChannel NewChannel(SessionAuth auth)
    {
        var channel = new GuardAiNetworkChannel(0, auth);
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, channel);
        channel.SetSnapshotSource(provider.Capture);
        return channel;
    }

    private static async Task<GuardAiClient> AuthenticateAsync(GuardAiNetworkChannel channel, RSA deviceKey, SessionAuth auth)
    {
        var client = new GuardAiClient("127.0.0.1", channel.LocalPort, deviceKey, auth.RuntimePublicKeyPem);
        await client.ConnectAsync();
        await client.OpenSessionAsync();
        return client;
    }

    // ------------------------------------------------------------- the deadline

    [Fact]
    public void TheAdmissionDeadlineIsIndependentOfTheHeartbeatBudget()
    {
        // The regression this pins is a silent one: before ADR-0011 an
        // unauthenticated peer was evicted only because the heartbeat watchdog
        // happened to cover it, so raising the heartbeat for a flaky network
        // would have widened the squatter's window with nothing to say so.
        Assert.True(
            GuardAiNetworkChannel.AuthenticationDeadline < GuardAiNetworkChannel.HeartbeatTimeout,
            "an unauthenticated peer must not get the budget meant for a live session");

        // Ten times the worst handshake measured against the real runtime
        // (151 ms over loopback). Cutting this near the measurement would drop a
        // slow phone part-way through authenticating.
        Assert.True(
            GuardAiNetworkChannel.AuthenticationDeadline >= TimeSpan.FromMilliseconds(1000),
            "too tight to complete a real handshake on a slow device");
    }

    [Fact]
    public async Task ACandidateThatNeverAuthenticatesIsDroppedAndSaysWhy()
    {
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);

        string? reason = null;
        channel.OnSessionTerminated += r => reason = r;
        channel.Start();

        using var squatter = new TcpClient();
        await squatter.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
        await WaitUntilAsync(() => channel.IsClientConnected, Patience);
        Assert.True(channel.IsClientConnected);
        // A candidate is not the session: nothing is authenticated and no session
        // id exists until someone wins the slot.
        Assert.False(channel.IsAuthenticated);
        Assert.Null(channel.ActiveSessionId);

        // Wait for the reason, not just for the socket. Drop() disposes the
        // connection before it raises OnSessionTerminated, so !IsClientConnected
        // is already true while reason is still null -- a window wide enough to
        // fail under the load of the full suite. Waiting on the observable being
        // asserted keeps the assertion exactly as strict.
        await WaitUntilAsync(() => !channel.IsClientConnected && reason is not null,
            GuardAiNetworkChannel.AuthenticationDeadline + Patience);

        Assert.False(channel.IsClientConnected);
        // Naming it a heartbeat timeout would send the reader after a network
        // problem instead of a peer that never authenticated.
        Assert.Equal("authentication_deadline_exceeded", reason);
    }

    [Fact]
    public async Task ASilentPeerIsDroppedBeforeTheHeartbeatBudgetWouldHaveExpired()
    {
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        using var squatter = new TcpClient();
        await squatter.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
        await WaitUntilAsync(() => channel.IsClientConnected, Patience);

        var started = DateTime.UtcNow;
        await WaitUntilAsync(() => !channel.IsClientConnected, GuardAiNetworkChannel.AuthenticationDeadline + Patience);
        var held = DateTime.UtcNow - started;

        Assert.False(channel.IsClientConnected);
        Assert.True(
            held < GuardAiNetworkChannel.HeartbeatTimeout,
            $"held a slot for {held.TotalMilliseconds:F0} ms, which is the heartbeat budget, not the admission one");
    }

    // ---------------------------------------------------------- the whole point

    [Fact]
    public async Task ASilentSquatterDoesNotKeepThePairedPhoneOut()
    {
        // The property ADR-0011 exists for. Before concurrent admission this was
        // false: the squatter held the only connection and the phone was refused
        // outright until the squatter's deadline expired.
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        using var squatter = new TcpClient();
        await squatter.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
        await WaitUntilAsync(() => channel.IsClientConnected, Patience);

        // No waiting for any deadline: the phone authenticates alongside it.
        var started = DateTime.UtcNow;
        await using var phone = await AuthenticateAsync(channel, deviceKey, auth);
        var took = DateTime.UtcNow - started;

        Assert.True(channel.IsAuthenticated);
        Assert.NotNull(channel.ActiveSessionId);
        Assert.True(
            took < GuardAiNetworkChannel.AuthenticationDeadline,
            $"the phone waited {took.TotalMilliseconds:F0} ms, so it was queued behind the squatter rather than admitted alongside it");
    }

    [Fact]
    public async Task SquattersFillingEverySlotDoNotKeepThePairedPhoneOut()
    {
        // The bound is what makes admission safe; it must not become the new way
        // to exclude the phone. A silent candidate is evicted before one that is
        // actually mid-handshake, so filling the slots with silence achieves
        // nothing.
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        var squatters = new List<TcpClient>();
        try
        {
            for (var i = 0; i < GuardAiNetworkChannel.MaxPendingConnections; i++)
            {
                var squatter = new TcpClient();
                await squatter.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
                squatters.Add(squatter);
            }

            await WaitUntilAsync(() => channel.IsClientConnected, Patience);

            await using var phone = await AuthenticateAsync(channel, deviceKey, auth);

            Assert.True(channel.IsAuthenticated);
        }
        finally
        {
            foreach (var squatter in squatters)
                squatter.Dispose();
        }
    }

    [Fact]
    public async Task AnAuthenticatedSessionIsNotDisplacedByANewConnection()
    {
        // The reverse attack. Displacement on arrival would turn "first to
        // authenticate wins" into "last to connect wins", which is worse than
        // where this started.
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        await using var phone = await AuthenticateAsync(channel, deviceKey, auth);
        var sessionId = channel.ActiveSessionId;
        Assert.NotNull(sessionId);

        using var intruder = new TcpClient();
        await intruder.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
        await Task.Delay(300);

        Assert.True(channel.IsAuthenticated);
        Assert.Equal(sessionId, channel.ActiveSessionId);

        // And the session still works: the intruder did not disturb its sequence
        // numbers or its cipher.
        var snapshot = await phone.HeartbeatAsync();
        Assert.Contains("gate1.snapshot.v1", snapshot);
    }

    [Fact]
    public async Task OnlyOneOfTwoValidPeersBecomesTheSession()
    {
        // Both hold the trusted key, so both could authenticate. Exactly one may
        // hold the session: the channel serves one phone.
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        await using var winner = await AuthenticateAsync(channel, deviceKey, auth);
        var sessionId = channel.ActiveSessionId;

        var second = new GuardAiClient("127.0.0.1", channel.LocalPort, deviceKey, auth.RuntimePublicKeyPem);
        await using (second)
        {
            await second.ConnectAsync();
            // The channel refuses a newcomer outright while a session is held, so
            // the second peer never gets a handshake at all.
            await Assert.ThrowsAnyAsync<GuardProtocolException>(() => second.OpenSessionAsync());
        }

        Assert.Equal(sessionId, channel.ActiveSessionId);
        Assert.True(channel.IsAuthenticated);
    }

    [Fact]
    public async Task TheSlotComesFreeForTheNextPeerAfterASquatterIsEvicted()
    {
        using var deviceKey = RSA.Create(2048);
        using var auth = new SessionAuth(deviceKey.ExportSubjectPublicKeyInfoPem());
        await using var channel = NewChannel(auth);
        channel.Start();

        using (var squatter = new TcpClient())
        {
            await squatter.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
            await WaitUntilAsync(() => channel.IsClientConnected, Patience);
            await WaitUntilAsync(() => !channel.IsClientConnected, GuardAiNetworkChannel.AuthenticationDeadline + Patience);
        }

        await using var phone = await AuthenticateAsync(channel, deviceKey, auth);

        Assert.True(channel.IsAuthenticated);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
    }
}
