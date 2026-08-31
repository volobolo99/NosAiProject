using System.Buffers.Binary;
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
/// What the Guard channel must refuse, driven against the real channel over a
/// real socket.
/// </summary>
/// <remarks>
/// <para>
/// Every other suite asks whether the good path works. This one asks whether the
/// bad paths are actually closed, which is the only question a security boundary
/// is judged on. The frames here are hand-built rather than produced by
/// <see cref="GuardAiClient"/>, because a correct client cannot express any of
/// them — an old version byte, a hello that skips the handshake, a signature from
/// the wrong key.
/// </para>
/// <para>
/// Each case asserts the structured reason, not merely that something failed. A
/// channel that refuses everything for the wrong reason is a channel nobody can
/// debug.
/// </para>
/// </remarks>
public sealed class GuardNegativeTests
{
    private readonly ITestOutputHelper _output;

    public GuardNegativeTests(ITestOutputHelper output) => _output = output;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private sealed record Harness(
        GuardAiNetworkChannel Channel,
        SessionAuth Auth,
        RSA TrustedKey,
        Func<string?> LastReason) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Channel.DisposeAsync();
            Auth.Dispose();
            TrustedKey.Dispose();
        }
    }

    private static Harness NewChannel()
    {
        var trusted = RSA.Create(2048);
        var auth = new SessionAuth(trusted.ExportSubjectPublicKeyInfoPem());
        var channel = new GuardAiNetworkChannel(0, auth);
        var runtime = RuntimeComposition.CreateSafe();
        var world = new NosAi.Runtime.WorldModel.WorldModel();
        var provider = new Gate1RuntimeSnapshotProvider(runtime, world, channel);
        channel.SetSnapshotSource(provider.Capture);

        string? reason = null;
        channel.OnSessionTerminated += r => reason = r;
        channel.Start();

        return new Harness(channel, auth, trusted, () => reason);
    }

    /// <summary>
    /// Builds a 12-byte header by hand, so a version this build cannot produce
    /// can still be put on the wire.
    /// </summary>
    private static byte[] Frame(byte version, WireMessageType type, uint sequence, byte[] payload, uint magic = WireHeader.ExpectedMagic)
    {
        var frame = new byte[WireHeader.HeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(0, 4), magic);
        frame[4] = version;
        frame[5] = (byte)type;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), sequence);
        payload.CopyTo(frame.AsSpan(WireHeader.HeaderSize));
        return frame;
    }

    private static byte[] Hello(EphemeralKeyExchange exchange, byte[]? nonce = null)
    {
        var hello = new byte[SessionAuth.HandshakeHelloLength];
        (nonce ?? SessionTranscript.CreateNonce()).CopyTo(hello, 0);
        exchange.PublicKey.CopyTo(hello, SessionTranscript.NonceLength);
        return hello;
    }

    private static async Task<TcpClient> ConnectAsync(GuardAiNetworkChannel channel)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, channel.LocalPort);
        return client;
    }

    /// <summary>Waits for the channel to drop everything, then returns the reason.</summary>
    private static async Task<string?> ExpectRefusalAsync(Harness harness)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (!harness.Channel.IsClientConnected && harness.LastReason() is not null)
                return harness.LastReason();
            await Task.Delay(25);
        }
        return harness.LastReason();
    }

    // ------------------------------------------------------- no wire downgrade

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    public async Task AnOlderWireVersionIsRefusedAtTheHeader(byte version)
    {
        // ADR-0009 forbids downgrade. Version 1 cannot prove the runtime to the
        // phone and version 2 sends the payload in clear, so a peer speaking
        // either must not be served — and must be refused before any handshake
        // state exists for it.
        await using var harness = NewChannel();
        using var exchange = EphemeralKeyExchange.Create();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(Frame(version, WireMessageType.SessionHello, 1, Hello(exchange)));

        Assert.Equal("invalid_header:unsupported_version", await ExpectRefusalAsync(harness));
        Assert.False(harness.Channel.IsAuthenticated);
    }

    [Fact]
    public async Task AFutureWireVersionIsRefusedToo()
    {
        // Not only older peers: anything that is not exactly this version is
        // refused, so a newer build cannot be half-served by an older runtime.
        await using var harness = NewChannel();
        using var exchange = EphemeralKeyExchange.Create();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(
            Frame((byte)(WireHeader.CurrentVersion + 1), WireMessageType.SessionHello, 1, Hello(exchange)));

        Assert.Equal("invalid_header:unsupported_version", await ExpectRefusalAsync(harness));
    }

    [Fact]
    public async Task AForeignMagicIsRefused()
    {
        await using var harness = NewChannel();
        using var exchange = EphemeralKeyExchange.Create();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(
            Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, Hello(exchange), magic: 0x4E4F5344 /* NOSD */));

        // The discovery magic in particular: a datagram meant for the unauthenticated
        // announcement must never be read as a session frame.
        Assert.Equal("invalid_header:invalid_magic", await ExpectRefusalAsync(harness));
    }

    // ------------------------------------------------------------- wrong key

    [Fact]
    public async Task ASignatureFromAnUntrustedKeyIsRefused()
    {
        await using var harness = NewChannel();
        using var intruderKey = RSA.Create(2048);

        var client = new GuardAiClient("127.0.0.1", harness.Channel.LocalPort, intruderKey, harness.Auth.RuntimePublicKeyPem);
        await using (client)
        {
            await client.ConnectAsync();
            var refused = await Assert.ThrowsAsync<GuardProtocolException>(() => client.OpenSessionAsync());
            Assert.Equal("authentication_refused", refused.Reason);
        }

        Assert.Equal("authentication_failed", await ExpectRefusalAsync(harness));
        Assert.False(harness.Channel.IsAuthenticated);
        Assert.Null(harness.Channel.ActiveSessionId);
    }

    [Fact]
    public async Task AnUntrustedPeerNeverReceivesTelemetry()
    {
        // The point of refusing it: the classified snapshot must not reach a peer
        // the runtime does not know. Asserting only "authentication failed" would
        // miss a runtime that refused and answered anyway.
        await using var harness = NewChannel();
        using var intruderKey = RSA.Create(2048);
        using var exchange = EphemeralKeyExchange.Create();
        using var peer = await ConnectAsync(harness.Channel);
        var stream = peer.GetStream();

        byte[] clientNonce = SessionTranscript.CreateNonce();
        await stream.WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, Hello(exchange, clientNonce)));

        var capabilities = await ReadFrameAsync(stream);
        var challenge = await ReadFrameAsync(stream);
        await ReadFrameAsync(stream); // ServerAuthProof
        Assert.Equal(WireMessageType.Capabilities, capabilities.Type);
        Assert.Equal(WireMessageType.AuthChallenge, challenge.Type);

        byte[] serverNonce = challenge.Payload[..SessionTranscript.NonceLength];
        byte[] serverEphemeral = challenge.Payload[SessionTranscript.NonceLength..];
        byte[] forged = SessionTranscript.Sign(
            intruderKey, HandshakeRole.Client, clientNonce, serverNonce, exchange.PublicKey, serverEphemeral);

        await stream.WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.AuthResponse, 2, forged));

        var result = await ReadFrameAsync(stream);
        Assert.Equal(WireMessageType.AuthResult, result.Type);
        Assert.Equal(0, result.Payload[0]);

        // Nothing after the refusal: no snapshot, no ack, just a closed socket.
        await Assert.ThrowsAsync<EndOfStreamException>(() => ReadFrameAsync(stream));
    }

    // ------------------------------------------------- skipping the handshake

    [Theory]
    [InlineData(WireMessageType.Heartbeat)]
    [InlineData(WireMessageType.TelemetrySnapshot)]
    [InlineData(WireMessageType.CommandRequest)]
    public async Task APlaintextFrameBeforeAuthenticationIsRefused(WireMessageType type)
    {
        // ADR-0009: past the handshake nothing is readable, and a peer that never
        // authenticated has no keys, so it cannot produce a readable frame the
        // runtime will act on.
        await using var harness = NewChannel();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(Frame(WireHeader.CurrentVersion, type, 1, Array.Empty<byte>()));

        Assert.Equal($"plaintext_after_handshake:{type}", await ExpectRefusalAsync(harness));
    }

    [Fact]
    public async Task AnAuthResponseWithoutAHelloIsRefused()
    {
        // No hello means no transcript, so there is nothing a signature could be
        // verified against. It must be refused rather than evaluated.
        await using var harness = NewChannel();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(
            Frame(WireHeader.CurrentVersion, WireMessageType.AuthResponse, 1, new byte[256]));

        Assert.Equal("auth_response_before_hello", await ExpectRefusalAsync(harness));
    }

    // ---------------------------------------------------------- malformed hello

    [Fact]
    public async Task AHelloThatIsOnlyANonceIsRefused()
    {
        // What a version 2 client would send: nonce, no ephemeral key. Refused,
        // because the key agreement has nothing to bind to.
        await using var harness = NewChannel();
        using var peer = await ConnectAsync(harness.Channel);

        await peer.GetStream().WriteAsync(
            Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, SessionTranscript.CreateNonce()));

        Assert.Equal("missing_or_malformed_client_hello", await ExpectRefusalAsync(harness));
    }

    [Fact]
    public async Task AHelloWithAnEphemeralKeyOffTheCurveIsRefused()
    {
        // An unchecked point would let the peer steer the agreement. The length is
        // right, so only actual validation catches this.
        await using var harness = NewChannel();
        using var peer = await ConnectAsync(harness.Channel);

        var hello = new byte[SessionAuth.HandshakeHelloLength];
        SessionTranscript.CreateNonce().CopyTo(hello, 0);
        hello[SessionTranscript.NonceLength] = 0x04; // uncompressed marker, zero coordinates

        await peer.GetStream().WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, hello));

        Assert.Equal("missing_or_malformed_client_hello", await ExpectRefusalAsync(harness));
    }

    // ------------------------------------------------------------------ replay

    [Fact]
    public async Task ASequenceNumberOutOfOrderIsRefused()
    {
        await using var harness = NewChannel();
        using var exchange = EphemeralKeyExchange.Create();
        using var peer = await ConnectAsync(harness.Channel);

        // Sequence starts at 1; jumping straight to 2 is a gap, and repeating one
        // is a replay. Either way the session ends rather than resynchronising.
        await peer.GetStream().WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 2, Hello(exchange)));

        Assert.Equal("sequence_violation:sequence_gap", await ExpectRefusalAsync(harness));
    }

    [Fact]
    public async Task AnAuthResponseReplayedFromAnEarlierSessionIsRefused()
    {
        // The property the session-bound transcript exists for: a signature that
        // was valid once must not be valid again, even from the trusted key.
        await using var harness = NewChannel();

        byte[] captured;
        byte[] firstClientNonce = SessionTranscript.CreateNonce();
        using var firstExchange = EphemeralKeyExchange.Create();

        using (var first = await ConnectAsync(harness.Channel))
        {
            var stream = first.GetStream();
            await stream.WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, Hello(firstExchange, firstClientNonce)));
            await ReadFrameAsync(stream); // Capabilities
            var challenge = await ReadFrameAsync(stream);
            await ReadFrameAsync(stream); // ServerAuthProof

            byte[] serverNonce = challenge.Payload[..SessionTranscript.NonceLength];
            byte[] serverEphemeral = challenge.Payload[SessionTranscript.NonceLength..];
            captured = SessionTranscript.Sign(
                harness.TrustedKey, HandshakeRole.Client, firstClientNonce, serverNonce, firstExchange.PublicKey, serverEphemeral);
        }

        // A second session: same signature, different nonces on the runtime's side.
        using var second = await ConnectAsync(harness.Channel);
        var secondStream = second.GetStream();
        await secondStream.WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.SessionHello, 1, Hello(firstExchange, firstClientNonce)));
        await ReadFrameAsync(secondStream); // Capabilities
        await ReadFrameAsync(secondStream); // AuthChallenge, with a fresh server nonce
        await ReadFrameAsync(secondStream); // ServerAuthProof
        await secondStream.WriteAsync(Frame(WireHeader.CurrentVersion, WireMessageType.AuthResponse, 2, captured));

        var result = await ReadFrameAsync(secondStream);
        Assert.Equal(WireMessageType.AuthResult, result.Type);
        Assert.Equal(0, result.Payload[0]);
        Assert.False(harness.Channel.IsAuthenticated);
    }

    // ------------------------------------------------------ the phone's side

    [Fact]
    public async Task ThePhoneRefusesARuntimeItHasNotPinned()
    {
        // Mutual: the runtime is not the only one checking. A phone pinned to a
        // different runtime must refuse this one before it signs anything.
        await using var harness = NewChannel();
        using var impostor = RSA.Create(2048);

        var client = new GuardAiClient(
            "127.0.0.1", harness.Channel.LocalPort, harness.TrustedKey, impostor.ExportSubjectPublicKeyInfoPem());
        await using (client)
        {
            await client.ConnectAsync();
            var refused = await Assert.ThrowsAsync<GuardProtocolException>(() => client.OpenSessionAsync());
            Assert.Equal("runtime_proof_rejected", refused.Reason);
        }

        // And it refused before authenticating, so the runtime never got a session.
        Assert.False(harness.Channel.IsAuthenticated);
    }

    private static async Task<(WireMessageType Type, byte[] Payload)> ReadFrameAsync(NetworkStream stream)
    {
        var header = new byte[WireHeader.HeaderSize];
        await ReadExactlyAsync(stream, header);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6, 2));
        var payload = new byte[length];
        if (length > 0)
            await ReadExactlyAsync(stream, payload);
        return ((WireMessageType)header[5], payload);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset));
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}
