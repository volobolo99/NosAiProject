using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NosAi.Runtime.Gate1;

namespace NosAi.GuardClient;

/// <summary>
/// Structured failure of the canonical Gate 1 channel.
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is a stable identifier, not prose: the phone UI has to
/// distinguish "the runtime refused this device" from "the network dropped"
/// without parsing an English sentence.
/// </remarks>
public sealed class GuardProtocolException : Exception
{
    public GuardProtocolException(string reason, string? detail = null)
        : base(detail is null ? reason : $"{reason}: {detail}")
    {
        Reason = reason;
        Detail = detail;
    }

    public string Reason { get; }
    public string? Detail { get; }
}

/// <summary>An authenticated Gate 1 session and its first telemetry snapshot.</summary>
public sealed record GuardSession(string Capabilities, string TelemetryJson);

/// <summary>
/// The phone side of the canonical PC-phone channel defined by ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// The wire primitives come from the runtime's own <c>WireProtocol.cs</c>, compiled
/// into this assembly, so the framing cannot drift from the server's.
/// </para>
/// <para>
/// The private key never leaves the client. The runtime holds only the trusted
/// public key: it can verify a signature but never produce one, so a compromised
/// PC cannot impersonate the phone.
/// </para>
/// <para>
/// Session contract (wire version 3): connect, <c>SessionHello</c> carrying the
/// phone's 32-byte nonce and its 65-byte ephemeral P-256 key, then
/// <c>Capabilities</c>, an <c>AuthChallenge</c> carrying the runtime's nonce and
/// ephemeral key, and <c>ServerAuthProof</c>. The phone verifies the proof against
/// a pinned runtime public key, answers with a transcript signature, and receives
/// <c>AuthResult</c> plus the classified snapshot. Heartbeat stays inside
/// <see cref="HeartbeatTimeout"/>. Each direction is sequence-guarded independently
/// from 1.
/// </para>
/// <para>
/// Everything after the handshake is sealed with AES-256-GCM under keys derived
/// from the ephemeral exchange, which the transcript signatures authenticate
/// (ADR-0009). A snapshot that arrives in clear is refused, not read.
/// </para>
/// </remarks>
public sealed class GuardAiClient : IAsyncDisposable
{
    /// <summary>Server-side heartbeat deadline. Source: GuardAiNetworkChannel.HeartbeatTimeout.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMilliseconds(2000);

    /// <summary>The snapshot contract this client understands.</summary>
    public const string SupportedContractVersion = "gate1.snapshot.v1";

    private readonly string _host;
    private readonly int _port;
    private readonly IDeviceSigner _signer;
    private readonly string _runtimePublicKeyPem;
    private readonly SequenceGuard _egress = new();
    private readonly SequenceGuard _ingress = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private SessionCipher? _cipher;
    private bool _disposed;

    /// <param name="privateKey">
    /// The device key. Not disposed by this client: on a phone it is owned by
    /// whatever created it and must outlive any single session.
    /// </param>
    /// <param name="runtimePublicKeyPem">
    /// The runtime's public key, pinned at pairing. Without it the phone cannot
    /// tell a genuine runtime from anything else on the network.
    /// </param>
    public GuardAiClient(string host, int port, RSA privateKey, string runtimePublicKeyPem)
        : this(host, port, new RsaDeviceSigner(privateKey), runtimePublicKeyPem)
    {
    }

    /// <param name="signer">
    /// Whatever holds the device key. A key inside the platform key store cannot
    /// be loaded into memory (ADR-0010), so the client asks for a signature rather
    /// than for the key.
    /// </param>
    public GuardAiClient(string host, int port, IDeviceSigner signer, string runtimePublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("A host is required.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        ArgumentNullException.ThrowIfNull(signer);
        if (string.IsNullOrWhiteSpace(runtimePublicKeyPem))
            throw new ArgumentException("A pinned runtime public key is required; mutual authentication is fail-closed.", nameof(runtimePublicKeyPem));

        _host = host;
        _port = port;
        _signer = signer;
        _runtimePublicKeyPem = runtimePublicKeyPem;
    }

    /// <summary>Where this device's private key lives, for the operator to see.</summary>
    public DeviceKeyCustody KeyCustody => _signer.Custody;

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client is not null)
            throw new GuardProtocolException("already_connected");

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new GuardProtocolException("connect_failed", ex.SocketErrorCode.ToString());
        }

        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>
    /// Runs the handshake and returns the first snapshot.
    /// </summary>
    /// <remarks>
    /// A refused signature throws rather than returning an unauthenticated session:
    /// the runtime terminates the connection immediately afterwards, so handing back
    /// an object would invite the caller to keep using a dead socket.
    /// </remarks>
    public async Task<GuardSession> OpenSessionAsync(CancellationToken cancellationToken = default)
    {
        var clientNonce = SessionTranscript.CreateNonce();
        using var exchange = EphemeralKeyExchange.Create();
        var clientEphemeral = exchange.PublicKey;

        var hello = new byte[SessionTranscript.NonceLength + SessionTranscript.EphemeralKeyLength];
        clientNonce.CopyTo(hello, 0);
        clientEphemeral.CopyTo(hello, SessionTranscript.NonceLength);
        await SendAsync(WireMessageType.SessionHello, hello, cancellationToken).ConfigureAwait(false);

        var capabilities = await ExpectAsync(WireMessageType.Capabilities, cancellationToken).ConfigureAwait(false);
        var challenge = await ExpectAsync(WireMessageType.AuthChallenge, cancellationToken).ConfigureAwait(false);
        if (challenge.Length != hello.Length)
            throw new GuardProtocolException("invalid_challenge_length", challenge.Length.ToString());

        var serverNonce = challenge[..SessionTranscript.NonceLength];
        var serverEphemeral = challenge[SessionTranscript.NonceLength..];

        var serverProof = await ExpectAsync(WireMessageType.ServerAuthProof, cancellationToken).ConfigureAwait(false);

        // Verify before deriving anything. The proof is what says the ephemeral key
        // just received belongs to the runtime this phone was paired with, and not
        // to whatever answered on the network.
        if (!VerifyRuntimeProof(clientNonce, serverNonce, clientEphemeral, serverEphemeral, serverProof))
            throw new GuardProtocolException("runtime_proof_rejected");

        byte[] material;
        try
        {
            var binding = SessionTranscript.ComputeBinding(clientNonce, serverNonce, clientEphemeral, serverEphemeral);
            material = exchange.DeriveSessionMaterial(serverEphemeral, binding);
        }
        catch (CryptographicException ex)
        {
            throw new GuardProtocolException("invalid_server_ephemeral_key", ex.GetType().Name);
        }

        var cipher = SessionCipher.ForPhone(material);
        CryptographicOperations.ZeroMemory(material);
        _cipher?.Dispose();
        _cipher = cipher;

        // Signed over the transcript message, not a digest computed here: a key in
        // a hardware store hashes the message itself. The bytes are identical
        // either way, which SessionTranscriptTests pins.
        var signature = _signer.Sign(
            SessionTranscript.Message(HandshakeRole.Client, clientNonce, serverNonce, clientEphemeral, serverEphemeral));
        await SendAsync(WireMessageType.AuthResponse, signature, cancellationToken).ConfigureAwait(false);

        var result = await ExpectAsync(WireMessageType.AuthResult, cancellationToken).ConfigureAwait(false);
        if (result.Length != 1 || result[0] != 1)
            throw new GuardProtocolException("authentication_refused");

        var telemetry = await ReadTelemetryAsync(cancellationToken).ConfigureAwait(false);
        return new GuardSession(Encoding.UTF8.GetString(capabilities), telemetry);
    }

    private bool VerifyRuntimeProof(
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral,
        ReadOnlySpan<byte> proof)
    {
        using var runtime = RSA.Create();
        try
        {
            runtime.ImportFromPem(_runtimePublicKeyPem);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return false;
        }

        if (runtime.KeySize != 2048)
            return false;

        return SessionTranscript.Verify(
            runtime, HandshakeRole.Server, clientNonce, serverNonce, clientEphemeral, serverEphemeral, proof);
    }

    /// <summary>Sends one heartbeat and returns the snapshot that follows the ack.</summary>
    public async Task<string> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(WireMessageType.Heartbeat, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        await ExpectAsync(WireMessageType.HeartbeatAck, cancellationToken).ConfigureAwait(false);
        return await ReadTelemetryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ReadTelemetryAsync(CancellationToken cancellationToken)
    {
        var payload = await ExpectAsync(WireMessageType.TelemetrySnapshot, cancellationToken).ConfigureAwait(false);
        string json;
        string? version;
        try
        {
            json = Encoding.UTF8.GetString(payload);
            using var document = JsonDocument.Parse(json);
            version = document.RootElement.TryGetProperty("contractVersion", out var element)
                ? element.GetString()
                : null;
        }
        catch (JsonException ex)
        {
            throw new GuardProtocolException("invalid_telemetry", ex.Message);
        }

        // An unrecognised contract version is not a snapshot this client can read.
        // Fail closed rather than render fields whose meaning is no longer promised.
        if (version != SupportedContractVersion)
            throw new GuardProtocolException("unsupported_contract_version", version ?? "missing");

        return json;
    }

    /// <summary>
    /// Writes one frame: handshake messages in clear, everything else sealed.
    /// </summary>
    /// <remarks>
    /// A non-handshake message with no cipher is refused rather than sent in
    /// clear. The client would otherwise leak exactly what ADR-0009 exists to
    /// hide, at the moment the session is already in an unexpected state.
    /// </remarks>
    private async Task SendAsync(WireMessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var stream = RequireStream();
        bool handshake = WireMessageTypes.IsHandshake(type);
        int limit = handshake ? WireHeader.MaxPayloadLength : SessionCipher.MaxPlaintextLength;
        if (payload.Length > limit)
            throw new GuardProtocolException("payload_too_large", payload.Length.ToString());

        var cipher = _cipher;
        if (!handshake && cipher is null)
            throw new GuardProtocolException("cipher_unavailable", type.ToString());

        var sequence = _egress.Next;
        int frameLength = handshake ? WireHeader.HeaderSize + payload.Length : SessionCipher.FrameLength(payload.Length);
        using var frame = PooledWireBuffer.Rent(frameLength);

        if (handshake)
        {
            new WireHeader(type, (ushort)payload.Length, sequence).WriteTo(frame.Span);
            payload.Span.CopyTo(frame.Span[WireHeader.HeaderSize..]);
        }
        else
        {
            cipher!.SealFrameInto(frame.Span, type, sequence, payload.Span);
        }

        try
        {
            await stream.WriteAsync(frame.Memory, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new GuardProtocolException("send_failed", ex.GetType().Name);
        }

        // Advance only once the bytes are away: burning a sequence number on a
        // failed write would make the server see a gap on the next attempt.
        _egress.ValidateAndAdvance(sequence, out _);
    }

    private async Task<byte[]> ExpectAsync(WireMessageType expected, CancellationToken cancellationToken)
    {
        var (type, payload) = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (type != expected)
            throw new GuardProtocolException("unexpected_message_type", $"expected {expected}, received {type}");
        return payload;
    }

    private async Task<(WireMessageType Type, byte[] Payload)> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var headerBuffer = PooledWireBuffer.Rent(WireHeader.HeaderSize);
        await ReadExactlyAsync(headerBuffer.Memory, cancellationToken).ConfigureAwait(false);
        if (!WireHeader.TryRead(headerBuffer.Span, out var header, out var error))
            throw new GuardProtocolException("invalid_header", error);

        if (!_ingress.ValidateAndAdvance(header.SequenceNumber, out var sequenceError))
            throw new GuardProtocolException("sequence_violation", sequenceError);

        using var payloadBuffer = PooledWireBuffer.Rent(header.PayloadLength);
        if (header.PayloadLength > 0)
            await ReadExactlyAsync(payloadBuffer.Memory, cancellationToken).ConfigureAwait(false);

        // ADR-0009: past the handshake nothing is readable. A frame that arrives in
        // clear, or that fails its tag, is refused rather than interpreted.
        byte[] payload;
        if (!WireMessageTypes.IsHandshake(header.MessageType))
        {
            var cipher = _cipher ?? throw new GuardProtocolException("plaintext_after_handshake", header.MessageType.ToString());
            if (!cipher.TryOpenFrame(headerBuffer.Span, payloadBuffer.Span, out var opened, out var openError))
                throw new GuardProtocolException("decrypt_failed", openError);
            payload = opened;
        }
        else
        {
            // Handshake payloads are small and rare; the owned copy hands off to
            // the caller while the rented buffer above returns to the pool here.
            payload = payloadBuffer.Span.ToArray();
        }

        return (header.MessageType, payload);
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var stream = RequireStream();
        var read = 0;
        while (read < buffer.Length)
        {
            int received;
            try
            {
                received = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new GuardProtocolException("receive_failed", ex.GetType().Name);
            }

            if (received == 0)
                throw new GuardProtocolException("peer_disconnected");
            read += received;
        }
    }

    private NetworkStream RequireStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _stream ?? throw new GuardProtocolException("not_connected");
    }

    /// <summary>
    /// Announces the disconnect when it still can, then drops the socket.
    /// </summary>
    /// <remarks>
    /// A failed announcement is swallowed: the peer notices the closed socket
    /// regardless, and throwing from disposal would mask the real error that
    /// usually caused it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_stream is not null && IsConnected)
        {
            try
            {
                await SendAsync(WireMessageType.Disconnect, ReadOnlyMemory<byte>.Empty, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is GuardProtocolException or IOException or ObjectDisposedException)
            {
            }
        }

        _disposed = true;
        _stream?.Dispose();
        _client?.Dispose();
        _cipher?.Dispose();
        _stream = null;
        _client = null;
        _cipher = null;
    }
}
