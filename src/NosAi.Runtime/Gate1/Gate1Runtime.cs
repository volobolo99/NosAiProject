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
    /// <summary>
    /// Length of the hello each side sends: nonce followed by ephemeral key.
    /// </summary>
    public const int HandshakeHelloLength = SessionTranscript.NonceLength + SessionTranscript.EphemeralKeyLength;

    private readonly RSA _verifier;
    private readonly RuntimeIdentity _identity;
    private readonly bool _ownsIdentity;

    /// <summary>
    /// Guards the shared RSA objects.
    /// </summary>
    /// <remarks>
    /// Several handshakes now run at once (ADR-0011), and they share one verifier
    /// and one identity. Neither <see cref="RSA"/> instance is documented as
    /// thread-safe, so every use of them is serialised here. The per-connection
    /// state that used to live on this class has moved to
    /// <see cref="HandshakeSession"/>, which is what made concurrency possible.
    /// </remarks>
    private readonly object _sync = new();

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
    /// Starts a handshake for one connection and returns its private state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns a fresh <see cref="HandshakeSession"/> per call rather than mutating
    /// this object. That is what lets several peers be admitted at once without
    /// overwriting each other's nonces — the prerequisite ADR-0011 named for
    /// concurrent admission.
    /// </para>
    /// <para>
    /// A hello that is not a well-formed nonce plus a valid P-256 point is refused
    /// here rather than later. A peer that will not commit to a nonce cannot be
    /// given a session-bound proof, and an unchecked ephemeral point would let it
    /// steer the key agreement.
    /// </para>
    /// </remarks>
    public HandshakeSession? TryBeginHandshake(ReadOnlySpan<byte> clientHello)
    {
        if (clientHello.Length != HandshakeHelloLength)
            return null;

        var clientNonce = clientHello[..SessionTranscript.NonceLength].ToArray();
        var clientEphemeral = clientHello[SessionTranscript.NonceLength..].ToArray();
        if (!EphemeralKeyExchange.IsValidPublicKey(clientEphemeral))
            return null;

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
                return null;
            }
        }

        return new HandshakeSession(this, clientNonce, serverNonce, clientEphemeral, serverEphemeral, material);
    }

    internal byte[] SignAsServer(byte[] clientNonce, byte[] serverNonce, byte[] clientEphemeral, byte[] serverEphemeral)
    {
        lock (_sync)
            return _identity.SignAsServer(clientNonce, serverNonce, clientEphemeral, serverEphemeral);
    }

    internal bool VerifyClient(byte[] digest, ReadOnlySpan<byte> signature)
    {
        lock (_sync)
        {
            try
            {
                return _verifier.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _verifier.Dispose();
            if (_ownsIdentity)
                _identity.Dispose();
        }
    }
}

/// <summary>
/// One connection's handshake: its nonces, its ephemeral keys and the session
/// material they derive.
/// </summary>
/// <remarks>
/// <para>
/// This state used to live on <see cref="SessionAuth"/> as fields, which meant one
/// handshake at a time — a second peer would overwrite the first one's nonces.
/// ADR-0011 needs several peers admitted at once so an unauthenticated squatter
/// cannot exclude the paired phone, and moving the state here is what allows it.
/// </para>
/// <para>
/// The material is released only when the phone's signature verifies, and cleared
/// either way, so a refused peer never walks away with a usable cipher and a
/// failed attempt cannot be retried against the same transcript.
/// </para>
/// </remarks>
public sealed class HandshakeSession
{
    private readonly SessionAuth _auth;
    private readonly byte[] _clientNonce;
    private readonly byte[] _serverNonce;
    private readonly byte[] _clientEphemeral;
    private readonly byte[] _serverEphemeral;
    private readonly object _sync = new();
    private byte[]? _material;

    internal HandshakeSession(
        SessionAuth auth,
        byte[] clientNonce,
        byte[] serverNonce,
        byte[] clientEphemeral,
        byte[] serverEphemeral,
        byte[] material)
    {
        _auth = auth;
        _clientNonce = clientNonce;
        _serverNonce = serverNonce;
        _clientEphemeral = clientEphemeral;
        _serverEphemeral = serverEphemeral;
        _material = material;

        ServerHello = new byte[SessionAuth.HandshakeHelloLength];
        serverNonce.CopyTo(ServerHello, 0);
        serverEphemeral.CopyTo(ServerHello, SessionTranscript.NonceLength);
    }

    /// <summary>The runtime's nonce and ephemeral key, as sent in the challenge.</summary>
    public byte[] ServerHello { get; }

    /// <summary>The runtime's proof of identity for this handshake.</summary>
    public byte[] CreateServerProof()
        => _auth.SignAsServer(_clientNonce, _serverNonce, _clientEphemeral, _serverEphemeral);

    /// <summary>
    /// Verifies the phone's signature over this transcript, once, and releases the
    /// session key material on success.
    /// </summary>
    public bool VerifyAndConsume(ReadOnlySpan<byte> signature, out byte[] sessionMaterial)
    {
        sessionMaterial = Array.Empty<byte>();

        byte[]? material;
        lock (_sync)
        {
            material = _material;
            _material = null;
        }

        if (material is null)
            return false;

        byte[] expected = SessionTranscript.Compute(
            HandshakeRole.Client, _clientNonce, _serverNonce, _clientEphemeral, _serverEphemeral);

        if (!_auth.VerifyClient(expected, signature))
        {
            CryptographicOperations.ZeroMemory(material);
            return false;
        }

        sessionMaterial = material;
        return true;
    }

    /// <summary>
    /// Drops the key material for a handshake that will not complete.
    /// </summary>
    /// <remarks>
    /// Called when a connection loses the race for the session or is evicted.
    /// Material derived for a peer that never authenticated must not outlive it.
    /// </remarks>
    public void Abandon()
    {
        lock (_sync)
        {
            if (_material is not null)
                CryptographicOperations.ZeroMemory(_material);
            _material = null;
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
    /// <summary>How long an <b>authenticated</b> session may go without a heartbeat.</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMilliseconds(2000);

    /// <summary>
    /// How long a connection may hold the single session slot without
    /// authenticating (ADR-0011).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately independent of <see cref="HeartbeatTimeout"/>. Before this
    /// existed, an unauthenticated peer was evicted only because the heartbeat
    /// watchdog happened to also cover it — so raising the heartbeat budget for a
    /// flaky network silently widened the window a squatter could hold the slot,
    /// with nothing to say so.
    /// </para>
    /// <para>
    /// 1500 ms is roughly ten times the worst measured handshake (median 75 ms,
    /// worst 151 ms over loopback, 12 samples), so a slow phone on a slow network
    /// is never cut off part-way through authenticating.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan AuthenticationDeadline = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// How many connections may be part-way through a handshake at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The slot for <i>connecting</i> and the slot for the <i>session</i> are not
    /// the same slot (ADR-0011). Admitting several candidates at once means a peer
    /// that cannot authenticate no longer excludes the paired phone by holding the
    /// only connection: the phone gets its own, and whoever authenticates first
    /// takes the session.
    /// </para>
    /// <para>
    /// Bounded, because unbounded admission is its own denial of service. Four is
    /// enough for a phone to get in alongside a handful of squatters and small
    /// enough that the sockets cost nothing.
    /// </para>
    /// </remarks>
    public const int MaxPendingConnections = 4;

    private readonly int _port;
    private readonly SessionAuth _auth;
    private readonly IPAddress _bindAddress;
    private readonly object _sync = new();

    /// <summary>Connections still trying to authenticate. Guarded by <c>_sync</c>.</summary>
    private readonly List<GuardConnection> _pending = new();

    /// <summary>The one authenticated session, once someone wins. Guarded by <c>_sync</c>.</summary>
    private GuardConnection? _session;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _terminationReason;
    private DateTime _lastActivityUtc;

    private Func<Gate1CanonicalSnapshot>? _snapshotSource;

    /// <summary>Whether anything holds or is trying for the session slot.</summary>
    public bool IsClientConnected
    {
        get { lock (_sync) return _session is not null || _pending.Count > 0; }
    }

    /// <summary>Whether an authenticated session exists. Never true for a candidate.</summary>
    public bool IsAuthenticated
    {
        get { lock (_sync) return _session?.IsAuthenticated == true; }
    }

    public string? ActiveSessionId
    {
        get { lock (_sync) return _session?.SessionId; }
    }

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
    /// fail-closed — an unknown key is refused — and ADR-0011 bounds what an
    /// unauthenticated peer can do with a connection, but a peer that reconnects
    /// fast enough can still make the phone retry. That is recorded, not solved.
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
        _ = WatchdogAsync(_cts.Token);
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

                GuardConnection connection;
                GuardConnection? evicted = null;
                lock (_sync)
                {
                    // An authenticated session is never displaced by a new arrival.
                    // Without this, "first wins" would merely become "last wins".
                    if (_session is not null) { client.Close(); continue; }

                    if (_pending.Count >= MaxPendingConnections)
                        evicted = TakeEvictionCandidateLocked();

                    connection = new GuardConnection(client);
                    _pending.Add(connection);
                    _lastActivityUtc = DateTime.UtcNow;
                }

                if (evicted is not null)
                    Drop(evicted, "admission_slots_full");

                _ = ServeAsync(connection, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { break; }
        }
    }

    /// <summary>
    /// Picks which candidate loses its place when the pending set is full.
    /// </summary>
    /// <remarks>
    /// Least progressed first: a connection that has not even sent a hello is
    /// costing a slot for nothing, and "connect and stay silent" is the cheapest
    /// way to squat. Among equals, the oldest goes. Caller holds <c>_sync</c>.
    /// </remarks>
    private GuardConnection TakeEvictionCandidateLocked()
    {
        GuardConnection victim = _pending[0];
        foreach (var candidate in _pending)
        {
            if (candidate.SawHello == victim.SawHello)
            {
                if (candidate.AcceptedAtUtc < victim.AcceptedAtUtc)
                    victim = candidate;
            }
            else if (!candidate.SawHello)
            {
                victim = candidate;
            }
        }

        _pending.Remove(victim);
        return victim;
    }

    /// <summary>
    /// Promotes a connection that has just authenticated, if the slot is still free.
    /// </summary>
    /// <remarks>
    /// The first to authenticate wins and every other candidate is closed: their
    /// handshakes are abandoned and their derived material zeroed, because key
    /// material for a peer that never became the session must not outlive it.
    /// </remarks>
    private bool TryPromote(GuardConnection connection)
    {
        List<GuardConnection> losers;
        lock (_sync)
        {
            if (_session is not null)
                return false;
            if (!_pending.Remove(connection))
                return false;

            _session = connection;
            _terminationReason = null;
            _lastActivityUtc = DateTime.UtcNow;
            losers = new List<GuardConnection>(_pending);
            _pending.Clear();
        }

        foreach (var loser in losers)
            Drop(loser, "another_peer_authenticated");

        return true;
    }

    /// <summary>Ends one connection, whether it was the session or a candidate.</summary>
    private void Drop(GuardConnection connection, string reason)
    {
        bool wasKnown;
        lock (_sync)
        {
            if (ReferenceEquals(_session, connection))
            {
                _session = null;
                wasKnown = true;
            }
            else
            {
                wasKnown = _pending.Remove(connection);
            }

            if (wasKnown)
                _terminationReason = reason;
        }

        connection.Dispose();

        if (wasKnown)
            OnSessionTerminated?.Invoke(reason);
    }

    private async Task ServeAsync(GuardConnection connection, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && connection.IsAlive)
            {
                // Rented per iteration, not held across it: each pass through the
                // loop reads exactly one frame, so the pool sees a bounded number
                // of buffers outstanding rather than one per frame ever received.
                using var headerBuffer = PooledWireBuffer.Rent(WireHeader.HeaderSize);
                await ReadExactlyAsync(connection.Stream, headerBuffer.Memory, token).ConfigureAwait(false);
                if (!WireHeader.TryRead(headerBuffer.Span, out var header, out var error)) { Drop(connection, $"invalid_header:{error}"); return; }
                if (!connection.Ingress.ValidateAndAdvance(header.SequenceNumber, out var sequenceError)) { Drop(connection, $"sequence_violation:{sequenceError}"); return; }

                using var payloadBuffer = PooledWireBuffer.Rent(header.PayloadLength);
                if (header.PayloadLength > 0) await ReadExactlyAsync(connection.Stream, payloadBuffer.Memory, token).ConfigureAwait(false);

                // ADR-0009: only the handshake is readable. Anything else must be
                // sealed under this session's keys, and a frame that will not open
                // ends the session rather than being interpreted.
                byte[] payload;
                if (!WireMessageTypes.IsHandshake(header.MessageType))
                {
                    var cipher = connection.Cipher;
                    if (cipher is null) { Drop(connection, $"plaintext_after_handshake:{header.MessageType}"); return; }
                    if (!cipher.TryOpenFrame(headerBuffer.Span, payloadBuffer.Span, out var opened, out var openError))
                    {
                        Drop(connection, $"decrypt_failed:{openError}");
                        return;
                    }
                    payload = opened;
                }
                else
                {
                    // Handshake payloads are small and rare; the owned copy hands
                    // off to HandleMessageAsync while the rented buffer above still
                    // returns to the pool at the end of this iteration.
                    payload = payloadBuffer.Span.ToArray();
                }

                await HandleMessageAsync(connection, header.MessageType, payload, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { Drop(connection, "peer_disconnected"); }
        catch (Exception ex) { Drop(connection, $"session_error:{ex.GetType().Name}"); }
    }

    private async Task HandleMessageAsync(GuardConnection connection, WireMessageType type, byte[] payload, CancellationToken token)
    {
        switch (type)
        {
            case WireMessageType.SessionHello:
                // The hello carries the phone's nonce and its ephemeral key, so the
                // runtime's proof is bound to values the phone chose. Without the
                // nonce the phone could not tell a fresh proof from a replayed one;
                // without the ephemeral key in the transcript the agreement would be
                // unauthenticated and a peer on the path could sit in the middle.
                var handshake = _auth.TryBeginHandshake(payload);
                if (handshake is null)
                {
                    Drop(connection, "missing_or_malformed_client_hello");
                    break;
                }

                connection.Handshake = handshake;

                await SendAsync(connection, WireMessageType.Capabilities, Encoding.UTF8.GetBytes("gate1;auth=rsa2048-sha256-mutual;payload=aes256gcm;heartbeat=2000;execution=disabled"), token).ConfigureAwait(false);
                await SendAsync(connection, WireMessageType.AuthChallenge, handshake.ServerHello, token).ConfigureAwait(false);

                // The runtime proves itself before asking the phone to sign anything.
                // The order matters: a phone that signs first has already answered an
                // unauthenticated peer.
                await SendAsync(connection, WireMessageType.ServerAuthProof, handshake.CreateServerProof(), token).ConfigureAwait(false);
                break;

            case WireMessageType.AuthResponse:
                var pending = connection.Handshake;
                if (pending is null)
                {
                    Drop(connection, "auth_response_before_hello");
                    break;
                }

                if (!pending.VerifyAndConsume(payload, out byte[] sessionMaterial))
                {
                    await SendAsync(connection, WireMessageType.AuthResult, new byte[] { 0 }, token).ConfigureAwait(false);
                    Drop(connection, "authentication_failed");
                    break;
                }

                // The cipher exists only past this point: a refused peer never gets
                // one, and every frame after AuthResult travels sealed.
                var cipher = SessionCipher.ForRuntime(sessionMaterial);
                CryptographicOperations.ZeroMemory(sessionMaterial);
                connection.Cipher = cipher;

                // Whoever authenticates first takes the session. A peer that loses
                // the race is closed with its keys abandoned rather than served.
                if (!TryPromote(connection))
                {
                    Drop(connection, "session_already_held");
                    break;
                }

                connection.IsAuthenticated = true;
                connection.LastHeartbeatUtc = DateTime.UtcNow;
                await SendAsync(connection, WireMessageType.AuthResult, new byte[] { 1 }, token).ConfigureAwait(false);
                await SendTelemetrySnapshotAsync(connection, token).ConfigureAwait(false);
                break;

            case WireMessageType.Heartbeat:
                connection.LastHeartbeatUtc = DateTime.UtcNow;
                await SendAsync(connection, WireMessageType.HeartbeatAck, Array.Empty<byte>(), token).ConfigureAwait(false);
                if (connection.IsAuthenticated)
                    await SendTelemetrySnapshotAsync(connection, token).ConfigureAwait(false);
                break;

            case WireMessageType.CommandRequest:
                var denied = JsonSerializer.SerializeToUtf8Bytes(new { allowed = false, reason = "execution_disabled_in_gate1" });
                await SendAsync(connection, WireMessageType.CommandAck, denied, token).ConfigureAwait(false);
                break;

            case WireMessageType.Disconnect:
                Drop(connection, "peer_requested_disconnect");
                break;
        }
    }

    /// <summary>
    /// Enforces whichever deadline matches each connection's state.
    /// </summary>
    /// <remarks>
    /// A candidate is held to <see cref="AuthenticationDeadline"/> and the
    /// termination says so. Reporting a heartbeat timeout for a peer that never
    /// owed a heartbeat sent the reader looking for a network problem instead of a
    /// peer that never authenticated.
    /// </remarks>
    private async Task WatchdogAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            var now = DateTime.UtcNow;
            GuardConnection? staleSession = null;
            List<GuardConnection>? staleCandidates = null;

            lock (_sync)
            {
                if (_session is { } session && now - session.LastHeartbeatUtc > HeartbeatTimeout)
                    staleSession = session;

                foreach (var candidate in _pending)
                {
                    if (now - candidate.AcceptedAtUtc <= AuthenticationDeadline)
                        continue;
                    staleCandidates ??= new List<GuardConnection>();
                    staleCandidates.Add(candidate);
                }
            }

            if (staleSession is not null)
                Drop(staleSession, "heartbeat_timeout_fail_closed");

            if (staleCandidates is not null)
            {
                foreach (var candidate in staleCandidates)
                    Drop(candidate, "authentication_deadline_exceeded");
            }
        }
    }

    /// <summary>
    /// Writes one frame: handshake messages in clear, everything else sealed.
    /// </summary>
    /// <remarks>
    /// A non-handshake message with no cipher available is dropped and the
    /// connection ended. Sending it in clear would defeat ADR-0009 at exactly the
    /// moment something went wrong, which is when it matters.
    /// </remarks>
    private async Task SendAsync(GuardConnection connection, WireMessageType type, byte[] payload, CancellationToken token)
    {
        bool handshake = WireMessageTypes.IsHandshake(type);
        int limit = handshake ? WireHeader.MaxPayloadLength : SessionCipher.MaxPlaintextLength;
        if (payload.Length > limit) throw new InvalidDataException("payload_too_large");

        if (!connection.IsAlive) return;
        var cipher = connection.Cipher;
        if (!handshake && cipher is null) { Drop(connection, $"cipher_unavailable:{type}"); return; }

        await connection.SendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var sequence = connection.Egress.Next;
            int frameLength = handshake ? WireHeader.HeaderSize + payload.Length : SessionCipher.FrameLength(payload.Length);
            using var frame = PooledWireBuffer.Rent(frameLength);

            if (handshake)
            {
                new WireHeader(type, checked((ushort)payload.Length), sequence).WriteTo(frame.Span);
                payload.CopyTo(frame.Span[WireHeader.HeaderSize..]);
            }
            else
            {
                cipher!.SealFrameInto(frame.Span, type, sequence, payload);
            }

            await connection.Stream.WriteAsync(frame.Memory, token).ConfigureAwait(false);
            await connection.Stream.FlushAsync(token).ConfigureAwait(false);
            if (!connection.Egress.ValidateAndAdvance(sequence, out _)) Drop(connection, "egress_sequence_failure");
        }
        finally { connection.SendLock.Release(); }
    }

    private async Task SendTelemetrySnapshotAsync(GuardConnection connection, CancellationToken token)
    {
        var source = _snapshotSource;
        if (source is null || !connection.IsAuthenticated)
            return;
        Gate1CanonicalSnapshot snapshot;
        try
        {
            snapshot = source();
        }
        catch (Exception ex)
        {
            Drop(connection, $"telemetry_source_failed:{ex.GetType().Name}");
            return;
        }
        var payload = JsonSerializer.SerializeToUtf8Bytes(snapshot.ToWire());
        await SendAsync(connection, WireMessageType.TelemetrySnapshot, payload, token).ConfigureAwait(false);
    }

    public Gate1ConnectionSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var session = _session;
            // With no session, the observable "since" is when something last
            // connected. Reporting a heartbeat that never happened would be worse
            // than reporting the connection time, which is what it has always been.
            DateTime since = session?.LastHeartbeatUtc ?? _lastActivityUtc;
            return new(
                session?.SessionId ?? "",
                session is not null || _pending.Count > 0,
                session?.IsAuthenticated == true,
                since,
                _terminationReason);
        }
    }

    /// <summary>
    /// Ends the authenticated session, and every candidate with it.
    /// </summary>
    /// <remarks>
    /// Kept as the outside-facing way to tear the channel down. Candidates go too:
    /// leaving them would let one be promoted immediately after a deliberate
    /// termination.
    /// </remarks>
    public void TerminateSession(string reason)
    {
        GuardConnection? session;
        List<GuardConnection> candidates;
        lock (_sync)
        {
            session = _session;
            candidates = new List<GuardConnection>(_pending);
            if (session is null && candidates.Count == 0) return;
            _session = null;
            _pending.Clear();
            _terminationReason = reason;
        }

        session?.Dispose();
        foreach (var candidate in candidates)
            candidate.Dispose();

        OnSessionTerminated?.Invoke(reason);
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        TerminateSession("channel_disposed");
        _listener?.Stop();
        _cts?.Dispose();
        await Task.CompletedTask;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    /// <summary>
    /// One connection and everything that belongs to it alone.
    /// </summary>
    /// <remarks>
    /// Sequence guards, send lock, handshake and cipher all used to be fields on
    /// the channel, which is why only one peer could be served at a time. They are
    /// per connection now so several can be admitted at once (ADR-0011).
    /// </remarks>
    private sealed class GuardConnection : IDisposable
    {
        private int _disposed;

        public GuardConnection(TcpClient client)
        {
            Client = client;
            Stream = client.GetStream();
            AcceptedAtUtc = DateTime.UtcNow;
            LastHeartbeatUtc = AcceptedAtUtc;
            SessionId = Guid.NewGuid().ToString("N");
        }

        public TcpClient Client { get; }
        public NetworkStream Stream { get; }
        public SequenceGuard Ingress { get; } = new();
        public SequenceGuard Egress { get; } = new();
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public DateTime AcceptedAtUtc { get; }
        public string SessionId { get; }

        public volatile bool IsAuthenticated;
        public DateTime LastHeartbeatUtc;

        /// <summary>This connection's handshake, once it has sent a hello.</summary>
        public HandshakeSession? Handshake { get; set; }

        /// <summary>Session encryption, once it has authenticated (ADR-0009).</summary>
        public SessionCipher? Cipher { get; set; }

        /// <summary>Whether a hello has been seen; used to pick an eviction victim.</summary>
        public bool SawHello => Handshake is not null;

        public bool IsAlive => Volatile.Read(ref _disposed) == 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            // Key material for a peer that never became the session must not
            // outlive it.
            Handshake?.Abandon();
            Cipher?.Dispose();
            try { Client.Close(); } catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
            Client.Dispose();
            SendLock.Dispose();
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
    private readonly IGameplayProvider _gameplay;
    private readonly Func<RuntimeHealthStatus> _health;
    private readonly string _correlationId;

    /// <param name="gameplay">
    /// Reads the game's own state. Optional, and absent by default: no provider
    /// means the snapshot keeps publishing gameplay as UNKNOWN with a reason,
    /// which is exactly what it published before one existed. Attaching a provider
    /// is the operator's decision and carries the operator's risk (ADR-0014).
    /// </param>
    public Gate1RuntimeSnapshotProvider(
        RuntimeComponents runtime,
        IWorldModel worldModel,
        GuardAiNetworkChannel channel,
        LiveHardwareTelemetry? hardware = null,
        RealClientConnector? client = null,
        Func<RuntimeHealthStatus>? health = null,
        string? correlationId = null,
        IGameplayProvider? gameplay = null)
    {
        _runtime = runtime;
        _worldModel = worldModel;
        _channel = channel;
        _hardware = hardware ?? new LiveHardwareTelemetry(new FallbackHardwareProbe());
        _client = client;
        _gameplay = gameplay ?? UnavailableGameplayProvider.Instance;
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

        // A provider that throws must not take the snapshot down with it: the
        // snapshot is how the operator finds out something is wrong, so it has to
        // survive the thing that went wrong.
        GameplayObservation gameplay;
        try
        {
            gameplay = _gameplay.Observe();
        }
        catch (Exception ex)
        {
            gameplay = GameplayObservation.Unobserved($"gameplay_provider_failed:{ex.GetType().Name}");
        }

        return Gate1SnapshotFactory.Create(
            _health(),
            _correlationId,
            hardware.View,
            client,
            _channel.GetSnapshot(),
            _runtime.SafetyPolicy,
            hardware.FailureReason,
            gameplay);
    }

    public object GetSnapshot() => Capture().ToWire();
}
