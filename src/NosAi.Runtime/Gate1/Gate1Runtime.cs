using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Hardware;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Safety;
using NosAi.Runtime.WorldModel;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// Verifies the phone and lets the runtime prove itself in return.
/// </summary>
/// <remarks>
/// <para>
/// Both signatures now cover a <see cref="SessionTranscript"/> rather than a raw
/// challenge. That closes three holes at once: the phone is no longer a signing
/// oracle for bytes a peer chooses, a signature is bound to one session, and the
/// role byte stops a phone's signature being replayed back as the runtime's proof.
/// </para>
/// <para>
/// The nonces are held per session and consumed once. A session that has not sent
/// its hello has no nonces, so no signature can verify against it.
/// </para>
/// <para>
/// Under ADR-0009 the handshake also carries an ephemeral P-256 key from each
/// side, covered by the same signatures. The session material derived from them
/// is held here and released only when the phone's signature verifies, so an
/// unauthenticated peer never obtains a usable cipher.
/// </para>
/// </remarks>
public sealed class SessionAuth : IDisposable
{
    private readonly RSA _verifier;
    private readonly RuntimeIdentity _identity;
    private readonly bool _ownsIdentity;
    private readonly object _sync = new();
    private byte[]? _clientNonce;
    private byte[]? _serverNonce;
    private byte[]? _clientEphemeral;
    private byte[]? _serverEphemeral;
    private byte[]? _sessionMaterial;

    /// <param name="identity">
    /// The runtime's own key. Created in memory when omitted, which is fine for
    /// tests but means a restart looks like an impostor to a paired phone; the
    /// bootstrap host passes the persisted one.
    /// </param>
    public SessionAuth(string trustedPublicKeyPem, RuntimeIdentity? identity = null)
    {
        if (string.IsNullOrWhiteSpace(trustedPublicKeyPem))
            throw new ArgumentException("A trusted public key is required; authentication is fail-closed.", nameof(trustedPublicKeyPem));
        _verifier = RSA.Create();
        _verifier.ImportFromPem(trustedPublicKeyPem);
        if (_verifier.KeySize != 2048)
            throw new CryptographicException("Only RSA-2048 trusted keys are accepted by Gate 1.");

        _ownsIdentity = identity is null;
        _identity = identity ?? RuntimeIdentity.CreateEphemeral();
    }

    /// <summary>The runtime's public key, for the phone to pin.</summary>
    public string RuntimePublicKeyPem => _identity.PublicKeyPem;

    /// <summary>
    /// Length of the hello each side sends: nonce followed by ephemeral key.
    /// </summary>
    public const int HandshakeHelloLength = SessionTranscript.NonceLength + SessionTranscript.EphemeralKeyLength;

    /// <summary>
    /// Starts a handshake: records the phone's hello and returns the runtime's.
    /// </summary>
    /// <remarks>
    /// A hello that is not a well-formed nonce plus a valid P-256 point is refused
    /// here rather than later. A peer that will not commit to a nonce cannot be
    /// given a session-bound proof, and an unchecked ephemeral point would let it
    /// steer the key agreement.
    /// </remarks>
    public bool TryBeginHandshake(ReadOnlySpan<byte> clientHello, out byte[] serverHello)
    {
        serverHello = Array.Empty<byte>();
        if (clientHello.Length != HandshakeHelloLength)
            return false;

        var clientNonce = clientHello[..SessionTranscript.NonceLength].ToArray();
        var clientEphemeral = clientHello[SessionTranscript.NonceLength..].ToArray();
        if (!EphemeralKeyExchange.IsValidPublicKey(clientEphemeral))
            return false;

        lock (_sync)
        {
            var serverNonce = SessionTranscript.CreateNonce();

            byte[] serverEphemeral;
            byte[] material;
            using (var exchange = EphemeralKeyExchange.Create())
            {
                serverEphemeral = exchange.PublicKey;
                byte[] binding = SessionTranscript.ComputeBinding(clientNonce, serverNonce, clientEphemeral, serverEphemeral);
                try
                {
                    material = exchange.DeriveSessionMaterial(clientEphemeral, binding);
                }
                catch (CryptographicException)
                {
                    return false;
                }
            }

            _clientNonce = clientNonce;
            _serverNonce = serverNonce;
            _clientEphemeral = clientEphemeral;
            _serverEphemeral = serverEphemeral;
            _sessionMaterial = material;

            serverHello = new byte[HandshakeHelloLength];
            serverNonce.CopyTo(serverHello, 0);
            serverEphemeral.CopyTo(serverHello, SessionTranscript.NonceLength);
            return true;
        }
    }

    /// <summary>The runtime's proof of identity for the session in progress.</summary>
    public bool TryCreateServerProof(out byte[] proof)
    {
        proof = Array.Empty<byte>();
        lock (_sync)
        {
            if (_clientNonce is null || _serverNonce is null || _clientEphemeral is null || _serverEphemeral is null)
                return false;
            proof = _identity.SignAsServer(_clientNonce, _serverNonce, _clientEphemeral, _serverEphemeral);
            return true;
        }
    }

    /// <summary>
    /// Verifies the phone's signature over this session's transcript, once, and
    /// releases the session key material on success.
    /// </summary>
    /// <remarks>
    /// Every piece of handshake state is cleared whether or not verification
    /// succeeds, so a failed attempt cannot be retried against the same
    /// transcript, and a refused peer never receives usable key material.
    /// </remarks>
    public bool VerifyAndConsume(ReadOnlySpan<byte> signature, out byte[] sessionMaterial)
    {
        sessionMaterial = Array.Empty<byte>();
        lock (_sync)
        {
            byte[]? clientNonce = _clientNonce;
            byte[]? serverNonce = _serverNonce;
            byte[]? clientEphemeral = _clientEphemeral;
            byte[]? serverEphemeral = _serverEphemeral;
            byte[]? material = _sessionMaterial;
            _clientNonce = null;
            _serverNonce = null;
            _clientEphemeral = null;
            _serverEphemeral = null;
            _sessionMaterial = null;

            if (clientNonce is null || serverNonce is null || clientEphemeral is null || serverEphemeral is null || material is null)
                return false;

            bool verified;
            try
            {
                byte[] expected = SessionTranscript.Compute(HandshakeRole.Client, clientNonce, serverNonce, clientEphemeral, serverEphemeral);
                verified = _verifier.VerifyHash(expected, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                verified = false;
            }

            if (!verified)
            {
                CryptographicOperations.ZeroMemory(material);
                return false;
            }

            sessionMaterial = material;
            return true;
        }
    }

    public void Dispose()
    {
        _verifier.Dispose();
        if (_ownsIdentity)
            _identity.Dispose();
        lock (_sync)
        {
            if (_sessionMaterial is not null)
                CryptographicOperations.ZeroMemory(_sessionMaterial);
            _sessionMaterial = null;
        }
    }
}

public sealed record Gate1ConnectionSnapshot(string SessionId, bool Connected, bool Authenticated, DateTime LastHeartbeatUtc, string? LastTerminationReason);

/// <summary>
/// The Guard channel could not bind. Carries a structured <see cref="Reason"/>
/// (for example <c>guard_port_in_use:17471</c>) so the failure names the port and
/// the remedy instead of surfacing as an opaque socket error.
/// </summary>
public sealed class GuardChannelBindException : Exception
{
    public GuardChannelBindException(string reason)
        : base($"Guard channel could not bind: {reason}. " +
               "Free the port, pass --guard-port <n>, or pass --guard-port 0 for an ephemeral port.")
        => Reason = reason;

    public string Reason { get; }
}

public sealed class GuardAiNetworkChannel : IAsyncDisposable
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMilliseconds(2000);

    private readonly int _port;
    private readonly SessionAuth _auth;
    private readonly IPAddress _bindAddress;
    private readonly SequenceGuard _ingress = new();
    private readonly SequenceGuard _egress = new();
    private readonly object _sync = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private DateTime _lastHeartbeatUtc;
    private string? _terminationReason;

    /// <summary>
    /// Session payload encryption (ADR-0009). Null until the phone authenticates,
    /// which is exactly why a non-handshake frame before that point is refused
    /// instead of being read in clear.
    /// </summary>
    private SessionCipher? _cipher;

    private Func<Gate1CanonicalSnapshot>? _snapshotSource;

    public bool IsClientConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public string? ActiveSessionId { get; private set; }
    public int LocalPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
    public event Action<string>? OnSessionTerminated;

    public void SetSnapshotSource(Func<Gate1CanonicalSnapshot> snapshotSource)
        => _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));

    /// <param name="bindAddress">
    /// Interface to listen on. <see cref="IPAddress.Any"/> is required for the
    /// Wi-Fi transport, since the phone dials this machine's LAN address; over USB
    /// the <c>adb reverse</c> tunnel terminates on loopback and either works.
    /// </param>
    /// <remarks>
    /// Binding beyond loopback exposes the channel to the local network. It stays
    /// fail-closed — an unknown key is refused — but two consequences follow and
    /// are documented in ADR-0007: any host on the network can reach the handshake,
    /// and because the channel serves one phone at a time, one that merely connects
    /// can hold the slot. Use loopback-only where the network is not trusted.
    /// </remarks>
    public GuardAiNetworkChannel(int port, SessionAuth auth, IPAddress? bindAddress = null)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _auth = auth;
        _bindAddress = bindAddress ?? IPAddress.Loopback;
    }

    /// <summary>
    /// Binds the Guard channel, reporting a busy or refused port as a structured
    /// reason instead of a raw <see cref="SocketException"/>. Unlike the operator
    /// dashboard, a failure here is not survivable: this is the authenticated
    /// PC-phone link, so the caller must fail closed rather than run without it.
    /// </summary>
    public bool TryStart(out string? failureReason)
    {
        if (_listener is not null) throw new InvalidOperationException("Channel already started.");

        var listener = new TcpListener(_bindAddress, _port);
        // Without this, binding 0.0.0.0 succeeds while another process holds
        // 127.0.0.1 on the same port -- and that process, being more specific, keeps
        // the loopback traffic. The USB path would then land on a foreign listener
        // with nothing to say so. An occupied port has to fail loudly.
        listener.ExclusiveAddressUse = true;
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            listener.Dispose();
            failureReason = DescribeBindFailure(_port, ex);
            return false;
        }

        _listener = listener;
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
        failureReason = null;
        return true;
    }

    public void Start()
    {
        if (!TryStart(out var failureReason))
            throw new GuardChannelBindException(failureReason!);
    }

    /// <summary>
    /// Maps the socket error onto a reason an operator can act on. A port already
    /// held by another runtime instance previously surfaced only as
    /// "SocketException (10048)" plus a stack trace, which named neither the port
    /// nor the remedy.
    /// </summary>
    private static string DescribeBindFailure(int port, SocketException ex)
        => ex.SocketErrorCode switch
        {
            SocketError.AddressAlreadyInUse => $"guard_port_in_use:{port}",
            SocketError.AccessDenied => $"guard_port_access_denied:{port}",
            SocketError.AddressNotAvailable => $"guard_address_unavailable:{port}",
            _ => $"guard_bind_failed:{port}:{ex.SocketErrorCode}"
        };

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (_client is not null) { client.Close(); continue; }
                    _client = client;
                    _stream = client.GetStream();
                    IsClientConnected = true;
                    IsAuthenticated = false;
                    ActiveSessionId = Guid.NewGuid().ToString("N");
                    _terminationReason = null;
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    _ingress.Reset();
                    _egress.Reset();
                    // Keys belong to one session. Carrying a cipher across would let
                    // a new peer decrypt under the previous phone's material.
                    _cipher?.Dispose();
                    _cipher = null;
                }
                _ = ProcessSessionAsync(client, _stream, token);
                _ = HeartbeatWatchdogAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { TerminateSession($"accept_error:{ex.GetType().Name}"); }
        }
    }

    private async Task ProcessSessionAsync(TcpClient client, NetworkStream stream, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && client.Connected && IsClientConnected)
            {
                var headerBytes = new byte[WireHeader.HeaderSize];
                await ReadExactlyAsync(stream, headerBytes, token).ConfigureAwait(false);
                if (!WireHeader.TryRead(headerBytes, out var header, out var error)) { TerminateSession($"invalid_header:{error}"); return; }
                if (!_ingress.ValidateAndAdvance(header.SequenceNumber, out var sequenceError)) { TerminateSession($"sequence_violation:{sequenceError}"); return; }
                var payload = new byte[header.PayloadLength];
                if (payload.Length > 0) await ReadExactlyAsync(stream, payload, token).ConfigureAwait(false);

                // ADR-0009: only the handshake is readable. Anything else must be
                // sealed under this session's keys, and a frame that will not open
                // ends the session rather than being interpreted.
                if (!WireMessageTypes.IsHandshake(header.MessageType))
                {
                    SessionCipher? cipher;
                    lock (_sync) cipher = _cipher;
                    if (cipher is null) { TerminateSession($"plaintext_after_handshake:{header.MessageType}"); return; }
                    if (!cipher.TryOpenFrame(headerBytes, payload, out var opened, out var openError))
                    {
                        TerminateSession($"decrypt_failed:{openError}");
                        return;
                    }
                    payload = opened;
                }

                await HandleMessageAsync(header.MessageType, payload, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { TerminateSession("peer_disconnected"); }
        catch (Exception ex) { TerminateSession($"session_error:{ex.GetType().Name}"); }
    }

    private async Task HandleMessageAsync(WireMessageType type, byte[] payload, CancellationToken token)
    {
        switch (type)
        {
            case WireMessageType.SessionHello:
                // The hello carries the phone's nonce and its ephemeral key, so the
                // runtime's proof is bound to values the phone chose. Without the
                // nonce the phone could not tell a fresh proof from a replayed one;
                // without the ephemeral key in the transcript the agreement would be
                // unauthenticated and a peer on the path could sit in the middle.
                if (!_auth.TryBeginHandshake(payload, out byte[] serverHello))
                {
                    TerminateSession("missing_or_malformed_client_hello");
                    break;
                }

                await SendAsync(WireMessageType.Capabilities, Encoding.UTF8.GetBytes("gate1;auth=rsa2048-sha256-mutual;payload=aes256gcm;heartbeat=2000;execution=disabled"), token).ConfigureAwait(false);
                await SendAsync(WireMessageType.AuthChallenge, serverHello, token).ConfigureAwait(false);

                // The runtime proves itself before asking the phone to sign anything.
                // The order matters: a phone that signs first has already answered an
                // unauthenticated peer.
                if (!_auth.TryCreateServerProof(out byte[] serverProof))
                {
                    TerminateSession("server_proof_unavailable");
                    break;
                }

                await SendAsync(WireMessageType.ServerAuthProof, serverProof, token).ConfigureAwait(false);
                break;
            case WireMessageType.AuthResponse:
                var authenticated = _auth.VerifyAndConsume(payload, out byte[] sessionMaterial);
                if (authenticated)
                {
                    // The cipher exists only past this point: a refused phone never
                    // gets one, and every frame after AuthResult travels sealed.
                    var cipher = SessionCipher.ForRuntime(sessionMaterial);
                    CryptographicOperations.ZeroMemory(sessionMaterial);
                    lock (_sync)
                    {
                        _cipher?.Dispose();
                        _cipher = cipher;
                    }
                }
                IsAuthenticated = authenticated;
                await SendAsync(WireMessageType.AuthResult, new[] { (byte)(authenticated ? 1 : 0) }, token).ConfigureAwait(false);
                if (!authenticated)
                {
                    TerminateSession("authentication_failed");
                    break;
                }
                await SendTelemetrySnapshotAsync(token).ConfigureAwait(false);
                break;
            case WireMessageType.Heartbeat:
                lock (_sync) _lastHeartbeatUtc = DateTime.UtcNow;
                await SendAsync(WireMessageType.HeartbeatAck, Array.Empty<byte>(), token).ConfigureAwait(false);
                if (IsAuthenticated)
                    await SendTelemetrySnapshotAsync(token).ConfigureAwait(false);
                break;
            case WireMessageType.CommandRequest:
                var denied = JsonSerializer.SerializeToUtf8Bytes(new { allowed = false, reason = "execution_disabled_in_gate1" });
                await SendAsync(WireMessageType.CommandAck, denied, token).ConfigureAwait(false);
                break;
            case WireMessageType.Disconnect:
                TerminateSession("peer_requested_disconnect");
                break;
        }
    }

    private async Task HeartbeatWatchdogAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && IsClientConnected)
        {
            await Task.Delay(250, token).ConfigureAwait(false);
            DateTime last;
            lock (_sync) last = _lastHeartbeatUtc;
            if (DateTime.UtcNow - last > HeartbeatTimeout) { TerminateSession("heartbeat_timeout_fail_closed"); return; }
        }
    }

    /// <summary>
    /// Writes one frame: handshake messages in clear, everything else sealed.
    /// </summary>
    /// <remarks>
    /// A non-handshake message with no cipher available is dropped and the session
    /// terminated. Sending it in clear would defeat ADR-0009 at exactly the moment
    /// something went wrong, which is when it matters.
    /// </remarks>
    private async Task SendAsync(WireMessageType type, byte[] payload, CancellationToken token)
    {
        bool handshake = WireMessageTypes.IsHandshake(type);
        int limit = handshake ? WireHeader.MaxPayloadLength : SessionCipher.MaxPlaintextLength;
        if (payload.Length > limit) throw new InvalidDataException("payload_too_large");

        NetworkStream? stream;
        SessionCipher? cipher;
        lock (_sync) { stream = _stream; cipher = _cipher; }
        if (stream is null || !IsClientConnected) return;
        if (!handshake && cipher is null) { TerminateSession($"cipher_unavailable:{type}"); return; }

        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var sequence = _egress.Next;
            byte[] packet;
            if (handshake)
            {
                packet = new byte[WireHeader.HeaderSize + payload.Length];
                new WireHeader(type, checked((ushort)payload.Length), sequence).WriteTo(packet);
                payload.CopyTo(packet.AsSpan(WireHeader.HeaderSize));
            }
            else
            {
                packet = cipher!.SealFrame(type, sequence, payload);
            }

            await stream.WriteAsync(packet, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            if (!_egress.ValidateAndAdvance(sequence, out _)) TerminateSession("egress_sequence_failure");
        }
        finally { _sendLock.Release(); }
    }

    private async Task SendTelemetrySnapshotAsync(CancellationToken token)
    {
        var source = _snapshotSource;
        if (source is null || !IsAuthenticated)
            return;
        Gate1CanonicalSnapshot snapshot;
        try
        {
            snapshot = source();
        }
        catch (Exception ex)
        {
            TerminateSession($"telemetry_source_failed:{ex.GetType().Name}");
            return;
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot.ToWire());
        await SendAsync(WireMessageType.TelemetrySnapshot, payload, token).ConfigureAwait(false);
    }

    public Gate1ConnectionSnapshot GetSnapshot()
    {
        lock (_sync) return new(ActiveSessionId ?? "", IsClientConnected, IsAuthenticated, _lastHeartbeatUtc, _terminationReason);
    }

    public void TerminateSession(string reason)
    {
        lock (_sync)
        {
            if (!IsClientConnected && _client is null) return;
            IsClientConnected = false;
            IsAuthenticated = false;
            _terminationReason = reason;
            try { _client?.Close(); } catch { }
            _client = null;
            _stream = null;
            ActiveSessionId = null;
            // The keys die with the session they were derived for.
            _cipher?.Dispose();
            _cipher = null;
        }
        OnSessionTerminated?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        TerminateSession("channel_disposed");
        _listener?.Stop();
        _cts?.Dispose();
        _sendLock.Dispose();
        await Task.CompletedTask;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }
}

public sealed class Gate1RuntimeSnapshotProvider
{
    private readonly RuntimeComponents _runtime;
    private readonly IWorldModel _worldModel;
    private readonly GuardAiNetworkChannel _channel;
    private readonly LiveHardwareTelemetry _hardware;
    private readonly RealClientConnector? _client;
    private readonly Func<RuntimeHealthStatus> _health;
    private readonly string _correlationId;

    public Gate1RuntimeSnapshotProvider(
        RuntimeComponents runtime,
        IWorldModel worldModel,
        GuardAiNetworkChannel channel,
        LiveHardwareTelemetry? hardware = null,
        RealClientConnector? client = null,
        Func<RuntimeHealthStatus>? health = null,
        string? correlationId = null)
    {
        _runtime = runtime;
        _worldModel = worldModel;
        _channel = channel;
        _hardware = hardware ?? new LiveHardwareTelemetry(new FallbackHardwareProbe());
        _client = client;
        // No health source means the state is not established. Bootstrapping, not
        // Healthy: an unreported health must never read as a passing one.
        _health = health ?? (() => RuntimeHealthStatus.Bootstrapping);
        _correlationId = correlationId ?? "gate1";
    }

    public Gate1CanonicalSnapshot Capture()
    {
        var hardware = _hardware.Capture();
        var client = _client?.Observe() ?? new ClientBaselineSnapshot(
            ProcessDetected: false,
            WindowDetected: false,
            ClientAttached: false,
            ProcessId: null,
            WindowHandle: IntPtr.Zero,
            Source: "live_process_attach",
            ObservedAtUtc: DateTime.UtcNow,
            Availability: ClientBaselineAvailability.Unavailable,
            Status: "client_unavailable",
            Warning: "No RealClientConnector is bound to this snapshot provider.",
            FailureReason: "connector_not_bound");
        _ = _worldModel.Current;
        return Gate1SnapshotFactory.Create(
            _health(),
            _correlationId,
            hardware.View,
            client,
            _channel.GetSnapshot(),
            _runtime.SafetyPolicy,
            hardware.FailureReason);
    }

    public object GetSnapshot() => Capture().ToWire();
}
