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

public sealed class SessionAuth : IDisposable
{
    private readonly RSA _verifier;
    private readonly object _sync = new();
    private byte[]? _challenge;

    public SessionAuth(string trustedPublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(trustedPublicKeyPem))
            throw new ArgumentException("A trusted public key is required; authentication is fail-closed.", nameof(trustedPublicKeyPem));
        _verifier = RSA.Create();
        _verifier.ImportFromPem(trustedPublicKeyPem);
        if (_verifier.KeySize != 2048)
            throw new CryptographicException("Only RSA-2048 trusted keys are accepted by Gate 1.");
    }

    public byte[] CreateChallenge()
    {
        lock (_sync)
        {
            _challenge = RandomNumberGenerator.GetBytes(32);
            return _challenge.ToArray();
        }
    }

    public bool VerifyAndConsume(ReadOnlySpan<byte> signature)
    {
        lock (_sync)
        {
            var challenge = _challenge;
            _challenge = null;
            if (challenge is null) return false;
            try { return _verifier.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
            catch (CryptographicException) { return false; }
        }
    }

    public void Dispose() => _verifier.Dispose();
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

    private Func<Gate1CanonicalSnapshot>? _snapshotSource;

    public bool IsClientConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public string? ActiveSessionId { get; private set; }
    public int LocalPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
    public event Action<string>? OnSessionTerminated;

    public void SetSnapshotSource(Func<Gate1CanonicalSnapshot> snapshotSource)
        => _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));

    public GuardAiNetworkChannel(int port, SessionAuth auth)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _auth = auth;
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

        var listener = new TcpListener(IPAddress.Loopback, _port);
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
                await SendAsync(WireMessageType.Capabilities, Encoding.UTF8.GetBytes("gate1;auth=rsa2048-sha256;heartbeat=2000;execution=disabled"), token).ConfigureAwait(false);
                await SendAsync(WireMessageType.AuthChallenge, _auth.CreateChallenge(), token).ConfigureAwait(false);
                break;
            case WireMessageType.AuthResponse:
                var authenticated = _auth.VerifyAndConsume(payload);
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

    private async Task SendAsync(WireMessageType type, byte[] payload, CancellationToken token)
    {
        if (payload.Length > WireHeader.MaxPayloadLength) throw new InvalidDataException("payload_too_large");
        NetworkStream? stream;
        lock (_sync) stream = _stream;
        if (stream is null || !IsClientConnected) return;
        await _sendLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var packet = new byte[WireHeader.HeaderSize + payload.Length];
            var header = new WireHeader(type, checked((ushort)payload.Length), _egress.Next);
            header.WriteTo(packet);
            payload.CopyTo(packet.AsSpan(WireHeader.HeaderSize));
            await stream.WriteAsync(packet, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            if (!_egress.ValidateAndAdvance(header.SequenceNumber, out _)) TerminateSession("egress_sequence_failure");
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
