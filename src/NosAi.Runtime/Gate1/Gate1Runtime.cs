using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Safety;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Gate1;

public enum WireMessageType : byte
{
    SessionHello = 0x01,
    Capabilities = 0x02,
    AuthChallenge = 0x03,
    AuthResponse = 0x04,
    AuthResult = 0x05,
    Heartbeat = 0x06,
    HeartbeatAck = 0x07,
    WorldStateDelta = 0x10,
    TelemetrySnapshot = 0x11,
    CommandRequest = 0x20,
    CommandAck = 0x21,
    Disconnect = 0xFF
}

public readonly record struct WireHeader(WireMessageType MessageType, ushort PayloadLength, uint SequenceNumber)
{
    public const uint ExpectedMagic = 0x4E4F5341; // NOSA
    public const byte CurrentVersion = 1;
    public const int HeaderSize = 12;
    public const int MaxPayloadLength = ushort.MaxValue;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < HeaderSize)
            throw new ArgumentException("Destination buffer is smaller than the 12-byte header.", nameof(destination));
        BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], ExpectedMagic);
        destination[4] = CurrentVersion;
        destination[5] = (byte)MessageType;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], PayloadLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], SequenceNumber);
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out WireHeader header, out string? error)
    {
        header = default;
        error = null;
        if (source.Length < HeaderSize) { error = "incomplete_header"; return false; }
        if (BinaryPrimitives.ReadUInt32BigEndian(source[0..4]) != ExpectedMagic) { error = "invalid_magic"; return false; }
        if (source[4] != CurrentVersion) { error = "unsupported_version"; return false; }
        header = new WireHeader((WireMessageType)source[5], BinaryPrimitives.ReadUInt16BigEndian(source[6..8]), BinaryPrimitives.ReadUInt32BigEndian(source[8..12]));
        return true;
    }
}

public sealed class SequenceGuard
{
    private readonly object _sync = new();
    private uint _expected;

    public SequenceGuard(uint expected = 1) => _expected = expected;

    public bool ValidateAndAdvance(uint received, out string? reason)
    {
        lock (_sync)
        {
            if (received == _expected)
            {
                _expected = _expected == uint.MaxValue ? 1 : _expected + 1;
                reason = null;
                return true;
            }
            reason = received < _expected ? "replay_or_duplicate" : "sequence_gap";
            return false;
        }
    }

    public uint Next { get { lock (_sync) return _expected; } }
    public void Reset(uint expected = 1) { lock (_sync) _expected = expected; }
}

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

    public bool IsClientConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public string? ActiveSessionId { get; private set; }
    public int LocalPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
    public event Action<string>? OnSessionTerminated;

    public GuardAiNetworkChannel(int port, SessionAuth auth)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
        _auth = auth;
    }

    public void Start()
    {
        if (_listener is not null) throw new InvalidOperationException("Channel already started.");
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

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
                if (!authenticated) TerminateSession("authentication_failed");
                break;
            case WireMessageType.Heartbeat:
                lock (_sync) _lastHeartbeatUtc = DateTime.UtcNow;
                await SendAsync(WireMessageType.HeartbeatAck, Array.Empty<byte>(), token).ConfigureAwait(false);
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

    public Gate1RuntimeSnapshotProvider(RuntimeComponents runtime, IWorldModel worldModel, GuardAiNetworkChannel channel)
    {
        _runtime = runtime;
        _worldModel = worldModel;
        _channel = channel;
    }

    public object GetSnapshot() => new
    {
        session = _channel.GetSnapshot(),
        worldState = _worldModel.Current,
        safety = new
        {
            _runtime.SafetyPolicy.LiveInputEnabled,
            _runtime.SafetyPolicy.PacketInjectionEnabled,
            _runtime.SafetyPolicy.RequireClientHealthy,
            _runtime.SafetyPolicy.RequireGuardApproval
        },
        trust = "managed_by_runtime_guard",
        hardware = "unavailable_until_real_telemetry_provider",
        execution = "disabled_in_gate1"
    };
}
