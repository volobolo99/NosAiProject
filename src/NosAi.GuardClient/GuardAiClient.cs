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
/// Session contract: connect, <c>SessionHello</c>, then <c>Capabilities</c> and a
/// 32-byte <c>AuthChallenge</c>, answer with an RSA-2048/SHA-256/PKCS#1 v1.5
/// signature, receive <c>AuthResult</c> and the classified snapshot, then heartbeat
/// inside <see cref="HeartbeatTimeout"/>. Each direction is sequence-guarded
/// independently from 1.
/// </para>
/// </remarks>
public sealed class GuardAiClient : IAsyncDisposable
{
    /// <summary>Server-side heartbeat deadline. Source: GuardAiNetworkChannel.HeartbeatTimeout.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMilliseconds(2000);

    /// <summary>The snapshot contract this client understands.</summary>
    public const string SupportedContractVersion = "gate1.snapshot.v1";

    private const int ChallengeLength = 32;

    private readonly string _host;
    private readonly int _port;
    private readonly RSA _key;
    private readonly SequenceGuard _egress = new();
    private readonly SequenceGuard _ingress = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private bool _disposed;

    /// <param name="privateKey">
    /// The device key. Not disposed by this client: on a phone it is owned by the
    /// platform key store, which must outlive any single session.
    /// </param>
    public GuardAiClient(string host, int port, RSA privateKey)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("A host is required.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.KeySize != 2048)
            throw new ArgumentException("Gate 1 accepts RSA-2048 keys only.", nameof(privateKey));

        _host = host;
        _port = port;
        _key = privateKey;
    }

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
        await SendAsync(WireMessageType.SessionHello, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);

        var capabilities = await ExpectAsync(WireMessageType.Capabilities, cancellationToken).ConfigureAwait(false);
        var challenge = await ExpectAsync(WireMessageType.AuthChallenge, cancellationToken).ConfigureAwait(false);
        if (challenge.Length != ChallengeLength)
            throw new GuardProtocolException("invalid_challenge_length", challenge.Length.ToString());

        var signature = _key.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        await SendAsync(WireMessageType.AuthResponse, signature, cancellationToken).ConfigureAwait(false);

        var result = await ExpectAsync(WireMessageType.AuthResult, cancellationToken).ConfigureAwait(false);
        if (result.Length != 1 || result[0] != 1)
            throw new GuardProtocolException("authentication_refused");

        var telemetry = await ReadTelemetryAsync(cancellationToken).ConfigureAwait(false);
        return new GuardSession(Encoding.UTF8.GetString(capabilities), telemetry);
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

    private async Task SendAsync(WireMessageType type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var stream = RequireStream();
        if (payload.Length > WireHeader.MaxPayloadLength)
            throw new GuardProtocolException("payload_too_large", payload.Length.ToString());

        var sequence = _egress.Next;
        var packet = new byte[WireHeader.HeaderSize + payload.Length];
        new WireHeader(type, (ushort)payload.Length, sequence).WriteTo(packet);
        payload.Span.CopyTo(packet.AsSpan(WireHeader.HeaderSize));

        try
        {
            await stream.WriteAsync(packet, cancellationToken).ConfigureAwait(false);
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
        var headerBytes = new byte[WireHeader.HeaderSize];
        await ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        if (!WireHeader.TryRead(headerBytes, out var header, out var error))
            throw new GuardProtocolException("invalid_header", error);

        if (!_ingress.ValidateAndAdvance(header.SequenceNumber, out var sequenceError))
            throw new GuardProtocolException("sequence_violation", sequenceError);

        var payload = new byte[header.PayloadLength];
        if (payload.Length > 0)
            await ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

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
        _stream = null;
        _client = null;
    }
}
