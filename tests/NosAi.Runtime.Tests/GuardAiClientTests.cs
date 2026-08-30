using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using NosAi.GuardClient;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.Orchestration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The Guard AI client driven against the real <see cref="GuardAiNetworkChannel"/>.
/// </summary>
/// <remarks>
/// ADR-0006 makes this channel the only canonical PC-phone contract, and the Guard
/// AI application is built on this client. These tests run both ends over a real
/// loopback socket, so a change to either side that breaks the phone fails the
/// build instead of shipping.
/// </remarks>
public sealed class GuardAiClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private sealed class Channel : IAsyncDisposable
    {
        public required GuardAiNetworkChannel Server { get; init; }
        public required SessionAuth Auth { get; init; }
        public required RSA TrustedKey { get; init; }
        public int Port => Server.LocalPort;

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            Auth.Dispose();
            TrustedKey.Dispose();
        }
    }

    private static Channel StartChannel()
    {
        var key = RSA.Create(2048);
        var auth = new SessionAuth(key.ExportRSAPublicKeyPem());
        var server = new GuardAiNetworkChannel(0, auth);
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        server.SetSnapshotSource(new Gate1RuntimeSnapshotProvider(runtime, world, server).Capture);
        server.Start();
        return new Channel { Server = server, Auth = auth, TrustedKey = key };
    }

    private static CancellationTokenSource Deadline() => new(Timeout);

    [Fact]
    public async Task TrustedClientCompletesTheHandshakeAndReadsClassifiedTelemetry()
    {
        await using var channel = StartChannel();
        using var cts = Deadline();

        await using var client = new GuardAiClient("127.0.0.1", channel.Port, channel.TrustedKey);
        await client.ConnectAsync(cts.Token);
        var session = await client.OpenSessionAsync(cts.Token);

        Assert.Contains("auth=rsa2048-sha256", session.Capabilities);
        Assert.Contains("heartbeat=2000", session.Capabilities);
        // The phone must be able to read that execution is off, not assume it.
        Assert.Contains("execution=disabled", session.Capabilities);

        using var snapshot = JsonDocument.Parse(session.TelemetryJson);
        Assert.Equal(
            Gate1SnapshotContract.Version,
            snapshot.RootElement.GetProperty("contractVersion").GetString());

        // An authenticated phone still gets UNKNOWN gameplay: authentication grants
        // access to the snapshot, never a value the runtime has not observed.
        var gameplay = snapshot.RootElement.GetProperty("client").GetProperty("gameplayBaseline");
        Assert.Equal("UNKNOWN", gameplay.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, gameplay.GetProperty("value").ValueKind);
        Assert.True(channel.Server.IsAuthenticated);
    }

    [Fact]
    public async Task HeartbeatsKeepTheSessionAlignedAcrossSeveralExchanges()
    {
        await using var channel = StartChannel();
        using var cts = Deadline();

        await using var client = new GuardAiClient("127.0.0.1", channel.Port, channel.TrustedKey);
        await client.ConnectAsync(cts.Token);
        await client.OpenSessionAsync(cts.Token);

        // Three round trips: a sequence guard that drifts by one shows up here and
        // not in a single-exchange test.
        for (var i = 0; i < 3; i++)
        {
            using var snapshot = JsonDocument.Parse(await client.HeartbeatAsync(cts.Token));
            Assert.Equal(
                Gate1SnapshotContract.Version,
                snapshot.RootElement.GetProperty("contractVersion").GetString());
        }
    }

    [Fact]
    public async Task UntrustedKeyIsRefusedFailClosed()
    {
        await using var channel = StartChannel();
        using var cts = Deadline();
        using var intruder = RSA.Create(2048);

        await using var client = new GuardAiClient("127.0.0.1", channel.Port, intruder);
        await client.ConnectAsync(cts.Token);

        var refused = await Assert.ThrowsAsync<GuardProtocolException>(() => client.OpenSessionAsync(cts.Token));
        Assert.Equal("authentication_refused", refused.Reason);
        Assert.False(channel.Server.IsAuthenticated);
    }

    [Fact]
    public async Task ClientRejectsAnUnknownContractVersionInsteadOfRenderingIt()
    {
        // The phone must fail closed on a snapshot whose meaning is no longer
        // promised, exactly like the operator dashboard. Served by a stub so the
        // check does not depend on the runtime ever emitting a bad version.
        using var listener = new StubChannel(contractVersion: "gate1.snapshot.v99");
        using var cts = Deadline();
        using var key = RSA.Create(2048);

        await using var client = new GuardAiClient("127.0.0.1", listener.Port, key);
        await client.ConnectAsync(cts.Token);

        var rejected = await Assert.ThrowsAsync<GuardProtocolException>(() => client.OpenSessionAsync(cts.Token));
        Assert.Equal("unsupported_contract_version", rejected.Reason);
        Assert.Equal("gate1.snapshot.v99", rejected.Detail);
    }

    [Fact]
    public async Task ConnectingToANonListeningPortReportsAStructuredReason()
    {
        var deadPort = FreePort();
        using var key = RSA.Create(2048);
        await using var client = new GuardAiClient("127.0.0.1", deadPort, key);

        var failed = await Assert.ThrowsAsync<GuardProtocolException>(() => client.ConnectAsync(CancellationToken.None));
        Assert.Equal("connect_failed", failed.Reason);
    }

    [Fact]
    public void KeysOtherThanRsa2048AreRejectedBeforeAnyTraffic()
    {
        using var weak = RSA.Create(1024);
        Assert.Throws<ArgumentException>(() => new GuardAiClient("127.0.0.1", 17471, weak));
    }

    private static int FreePort()
    {
        var probe = new TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>
    /// A minimal server that performs the handshake but answers with a chosen
    /// contract version, so the client's fail-closed path can be exercised without
    /// making the real runtime lie.
    /// </summary>
    private sealed class StubChannel : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();

        public StubChannel(string contractVersion)
        {
            _listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
            _ = ServeAsync(contractVersion, _cts.Token);
        }

        public int Port { get; }

        private async Task ServeAsync(string contractVersion, CancellationToken token)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(token);
                await using var stream = client.GetStream();
                uint egress = 1;

                await ReadFrameAsync(stream, token); // SessionHello
                await WriteFrameAsync(stream, WireMessageType.Capabilities,
                    System.Text.Encoding.UTF8.GetBytes("gate1;auth=rsa2048-sha256"), egress++, token);
                await WriteFrameAsync(stream, WireMessageType.AuthChallenge, new byte[32], egress++, token);

                await ReadFrameAsync(stream, token); // AuthResponse
                await WriteFrameAsync(stream, WireMessageType.AuthResult, new byte[] { 1 }, egress++, token);
                await WriteFrameAsync(stream, WireMessageType.TelemetrySnapshot,
                    System.Text.Encoding.UTF8.GetBytes($"{{\"contractVersion\":\"{contractVersion}\"}}"), egress, token);

                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            catch (Exception)
            {
                // The client closes as soon as it rejects the version; that is the
                // expected end of this stub's life, not a failure.
            }
        }

        private static async Task WriteFrameAsync(
            NetworkStream stream, WireMessageType type, byte[] payload, uint sequence, CancellationToken token)
        {
            var packet = new byte[WireHeader.HeaderSize + payload.Length];
            new WireHeader(type, (ushort)payload.Length, sequence).WriteTo(packet);
            payload.CopyTo(packet.AsSpan(WireHeader.HeaderSize));
            await stream.WriteAsync(packet, token);
            await stream.FlushAsync(token);
        }

        private static async Task ReadFrameAsync(NetworkStream stream, CancellationToken token)
        {
            var header = new byte[WireHeader.HeaderSize];
            var read = 0;
            while (read < header.Length)
            {
                var received = await stream.ReadAsync(header.AsMemory(read), token);
                if (received == 0) return;
                read += received;
            }

            WireHeader.TryRead(header, out var parsed, out _);
            var payload = new byte[parsed.PayloadLength];
            read = 0;
            while (read < payload.Length)
            {
                var received = await stream.ReadAsync(payload.AsMemory(read), token);
                if (received == 0) return;
                read += received;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
