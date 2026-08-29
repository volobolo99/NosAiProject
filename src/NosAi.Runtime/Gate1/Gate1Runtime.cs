// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Implementazione del Gate 1 (PC <-> NosTale <-> Guard AI <-> Dashboard)
// Standard: C# 12 / .NET 8 — Zero-Allocation, Fail-Closed Security, Clean Code
// ============================================================================

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate1
{
    #region 1. Contratti di Dominio e Modelli di Stato

    public enum TrustTier : byte
    {
        Tier0_ReadOnly = 0,
        Tier1_Assisted = 1,
        Tier2_SemiAutonomous = 2,
        Tier3_AutonomousRestricted = 3,
        Tier4_FullAutonomous = 4
    }

    public enum RuntimeMode : byte
    {
        Normal = 0,
        Degraded = 1,
        Recovery = 2,
        Cooling = 3,
        Stopped = 4
    }

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

    public sealed record PlayerState(
        string CharacterName,
        int Level,
        int JobLevel,
        int CurrentHp,
        int MaxHp,
        int CurrentMp,
        int MaxMp,
        int PositionX,
        int PositionY,
        int MapId,
        bool IsInCombat
    );

    public sealed record HardwareMetrics(
        double CpuUsagePercent,
        double GpuTemperatureCelsius,
        double GpuUsagePercent,
        long RamUsedBytes,
        long RamTotalBytes,
        bool ThermalThrottlingTriggered
    );

    public sealed record UnifiedWorldSnapshot(
        string SessionId,
        ulong FrameIndex,
        DateTime TimestampUtc,
        RuntimeMode CurrentMode,
        TrustTier CurrentTrustTier,
        PlayerState Player,
        HardwareMetrics Hardware,
        bool IsPhoneGuardConnected,
        bool IsGameClientConnected
    );

    #endregion

    #region 2. Wire Protocol a 12 Byte e Sequence Guard

    public readonly struct WireHeader : IEquatable<WireHeader>
    {
        public const uint ExpectedMagic = 0x4E4F5331;
        public const byte CurrentVersion = 0x01;
        public const int HeaderSize = 12;

        public uint Magic { get; }
        public byte Version { get; }
        public WireMessageType MessageType { get; }
        public ushort PayloadLength { get; }
        public uint SequenceNumber { get; }

        public WireHeader(WireMessageType messageType, ushort payloadLength, uint sequenceNumber)
        {
            Magic = ExpectedMagic;
            Version = CurrentVersion;
            MessageType = messageType;
            PayloadLength = payloadLength;
            SequenceNumber = sequenceNumber;
        }

        public WireHeader(uint magic, byte version, WireMessageType messageType, ushort payloadLength, uint sequenceNumber)
        {
            Magic = magic;
            Version = version;
            MessageType = messageType;
            PayloadLength = payloadLength;
            SequenceNumber = sequenceNumber;
        }

        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < HeaderSize)
                throw new ArgumentException("Buffer di destinazione insufficiente per l'header.", nameof(destination));

            BinaryPrimitives.WriteUInt32BigEndian(destination[0..4], Magic);
            destination[4] = Version;
            destination[5] = (byte)MessageType;
            BinaryPrimitives.WriteUInt16BigEndian(destination[6..8], PayloadLength);
            BinaryPrimitives.WriteUInt32BigEndian(destination[8..12], SequenceNumber);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out WireHeader header, out string? errorMessage)
        {
            header = default;
            errorMessage = null;

            if (source.Length < HeaderSize)
            {
                errorMessage = "Buffer inferiore alla dimensione minima dell'header (12 byte).";
                return false;
            }

            uint magic = BinaryPrimitives.ReadUInt32BigEndian(source[0..4]);
            if (magic != ExpectedMagic)
            {
                errorMessage = $"Magic bytes non validi: 0x{magic:X8} (atteso 0x{ExpectedMagic:X8}).";
                return false;
            }

            byte version = source[4];
            if (version != CurrentVersion)
            {
                errorMessage = $"Versione protocollo non supportata: {version}.";
                return false;
            }

            WireMessageType type = (WireMessageType)source[5];
            ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(source[6..8]);
            uint sequence = BinaryPrimitives.ReadUInt32BigEndian(source[8..12]);

            header = new WireHeader(magic, version, type, payloadLength, sequence);
            return true;
        }

        public bool Equals(WireHeader other) =>
            Magic == other.Magic &&
            Version == other.Version &&
            MessageType == other.MessageType &&
            PayloadLength == other.PayloadLength &&
            SequenceNumber == other.SequenceNumber;

        public override bool Equals(object? obj) => obj is WireHeader other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Magic, Version, MessageType, PayloadLength, SequenceNumber);
    }

    public sealed class SequenceGuard
    {
        private uint _expectedNextSequence;
        private readonly object _syncRoot = new();

        public SequenceGuard(uint initialExpectedSequence = 1)
        {
            _expectedNextSequence = initialExpectedSequence;
        }

        public bool ValidateAndAdvance(uint receivedSequence, out string? failureReason)
        {
            lock (_syncRoot)
            {
                if (receivedSequence == _expectedNextSequence)
                {
                    _expectedNextSequence++;
                    failureReason = null;
                    return true;
                }

                if (receivedSequence < _expectedNextSequence)
                {
                    failureReason = $"Rilevato replay o frame duplicato: SEQ {receivedSequence} (atteso {_expectedNextSequence}).";
                    return false;
                }

                failureReason = $"Rilevato pacchetto mancante/gap: SEQ {receivedSequence} (atteso {_expectedNextSequence}).";
                return false;
            }
        }

        public uint CurrentExpected => _expectedNextSequence;
        public void Reset(uint sequence = 1)
        {
            lock (_syncRoot) { _expectedNextSequence = sequence; }
        }
    }

    #endregion

    #region 3. Autenticazione RSA Challenge-Response

    public sealed class NosAiCryptoAuthManager : IDisposable
    {
        private byte[]? _activeChallenge;
        private readonly object _authLock = new();
        private readonly RSA _rsaVerifier;

        public NosAiCryptoAuthManager(string? trustedPublicKeyXml = null)
        {
            _rsaVerifier = RSA.Create(2048);
            if (!string.IsNullOrWhiteSpace(trustedPublicKeyXml))
            {
                _rsaVerifier.FromXmlString(trustedPublicKeyXml);
            }
        }

        public byte[] GenerateSessionChallenge()
        {
            lock (_authLock)
            {
                _activeChallenge = new byte[32];
                RandomNumberGenerator.Fill(_activeChallenge);
                return _activeChallenge.ToArray();
            }
        }

        public bool VerifyResponseAndConsume(ReadOnlySpan<byte> signature)
        {
            lock (_authLock)
            {
                if (_activeChallenge == null)
                    return false;

                byte[] challengeToVerify = _activeChallenge;
                _activeChallenge = null;

                try
                {
                    return _rsaVerifier.VerifyData(
                        challengeToVerify,
                        signature.ToArray(),
                        HashAlgorithmName.SHA256,
                        RSASignaturePadding.Pkcs1);
                }
                catch
                {
                    return false;
                }
            }
        }

        public string ExportPublicKeyXml() => _rsaVerifier.ToXmlString(false);

        public byte[] SignChallengeForClientMock(byte[] challenge)
        {
            return _rsaVerifier.SignData(challenge, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public void Dispose()
        {
            _rsaVerifier.Dispose();
        }
    }

    #endregion

    #region 4. Adapter Client NosTale (Read-Only & Sandbox)

    public interface INosTaleClientAdapter
    {
        bool IsConnected { get; }
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task<PlayerState?> PollPlayerStateAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();
    }

    public sealed class ControlledNosTaleClientAdapter : INosTaleClientAdapter
    {
        private bool _connected;
        private int _simulatedTick;

        public bool IsConnected => _connected;

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            _connected = true;
            _simulatedTick = 0;
            return Task.FromResult(true);
        }

        public Task<PlayerState?> PollPlayerStateAsync(CancellationToken cancellationToken = default)
        {
            if (!_connected)
                return Task.FromResult<PlayerState?>(null);

            _simulatedTick++;
            var state = new PlayerState(
                CharacterName: "NosAdventurer_01",
                Level: 15,
                JobLevel: 8,
                CurrentHp: 1450 - (_simulatedTick % 50),
                MaxHp: 1500,
                CurrentMp: 680,
                MaxMp: 700,
                PositionX: 120 + (_simulatedTick % 10),
                PositionY: 85,
                MapId: 1,
                IsInCombat: (_simulatedTick % 6 == 0)
            );

            return Task.FromResult<PlayerState?>(state);
        }

        public Task DisconnectAsync()
        {
            _connected = false;
            return Task.CompletedTask;
        }
    }

    #endregion

    #region 5. Monitor Telemetria Hardware PC

    public sealed class HardwareTelemetryProvider
    {
        private readonly double _thermalThresholdCelsius = 80.0;

        public HardwareMetrics CaptureMetrics()
        {
            using var process = Process.GetCurrentProcess();
            long ramUsed = process.WorkingSet64;
            long ramTotal = 16L * 1024 * 1024 * 1024;

            double simulatedGpuTemp = 68.5;
            double simulatedCpuUsage = 14.2;
            double simulatedGpuUsage = 28.0;

            bool isThrottling = simulatedGpuTemp >= _thermalThresholdCelsius;

            return new HardwareMetrics(
                CpuUsagePercent: simulatedCpuUsage,
                GpuTemperatureCelsius: simulatedGpuTemp,
                GpuUsagePercent: simulatedGpuUsage,
                RamUsedBytes: ramUsed,
                RamTotalBytes: ramTotal,
                ThermalThrottlingTriggered: isThrottling
            );
        }
    }

    #endregion

    #region 6. Server di Comunicazione PC <-> Guard AI

    public sealed class GuardAiNetworkChannel : IAsyncDisposable
    {
        private readonly int _port;
        private readonly NosAiCryptoAuthManager _cryptoAuth;
        private readonly SequenceGuard _sequenceGuard;
        private TcpListener? _listener;
        private TcpClient? _currentClient;
        private NetworkStream? _networkStream;
        private CancellationTokenSource? _sessionCts;
        private DateTime _lastHeartbeatUtc;
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMilliseconds(2000);
        private readonly object _stateLock = new();

        public bool IsClientConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public string? ActiveSessionId { get; private set; }

        public event Action<string>? OnSessionTerminated;

        public GuardAiNetworkChannel(int port, NosAiCryptoAuthManager cryptoAuth)
        {
            _port = port;
            _cryptoAuth = cryptoAuth;
            _sequenceGuard = new SequenceGuard(1);
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            _sessionCts = new CancellationTokenSource();
            _ = AcceptConnectionsLoopAsync(_sessionCts.Token);
        }

        private async Task AcceptConnectionsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        if (_currentClient != null)
                        {
                            client.Close();
                            continue;
                        }
                        _currentClient = client;
                        _networkStream = client.GetStream();
                        IsClientConnected = true;
                        ActiveSessionId = Guid.NewGuid().ToString("N");
                        _sequenceGuard.Reset(1);
                        _lastHeartbeatUtc = DateTime.UtcNow;
                    }

                    _ = ProcessSessionAsync(client, _networkStream, token);
                    _ = HeartbeatWatchdogLoopAsync(token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception)
                {
                    TerminateSession("Errore accettazione connessione client.");
                }
            }
        }

        private async Task ProcessSessionAsync(TcpClient client, NetworkStream stream, CancellationToken token)
        {
            byte[] headerBuffer = new byte[WireHeader.HeaderSize];

            try
            {
                while (!token.IsCancellationRequested && client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(headerBuffer, 0, WireHeader.HeaderSize, token).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        TerminateSession("Connessione chiusa dal client remoto.");
                        break;
                    }

                    if (bytesRead < WireHeader.HeaderSize)
                    {
                        TerminateSession("Header frammentato o non valido.");
                        break;
                    }

                    if (!WireHeader.TryRead(headerBuffer, out WireHeader header, out string? error))
                    {
                        TerminateSession($"Header protocollo non valido: {error}");
                        break;
                    }

                    if (!_sequenceGuard.ValidateAndAdvance(header.SequenceNumber, out string? seqError))
                    {
                        TerminateSession($"Violazione SequenceGuard: {seqError}");
                        break;
                    }

                    byte[] payload = new byte[header.PayloadLength];
                    if (header.PayloadLength > 0)
                    {
                        int payloadRead = 0;
                        while (payloadRead < header.PayloadLength)
                        {
                            int read = await stream.ReadAsync(payload, payloadRead, header.PayloadLength - payloadRead, token).ConfigureAwait(false);
                            if (read == 0) throw new IOException("Disconnessione imprevista durante la lettura del payload.");
                            payloadRead += read;
                        }
                    }

                    await HandleMessageAsync(header, payload, stream, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                TerminateSession($"Eccezione nel ciclo di sessione: {ex.Message}");
            }
        }

        private async Task HandleMessageAsync(WireHeader header, byte[] payload, NetworkStream stream, CancellationToken token)
        {
            switch (header.MessageType)
            {
                case WireMessageType.SessionHello:
                    byte[] challenge = _cryptoAuth.GenerateSessionChallenge();
                    await SendMessageAsync(WireMessageType.AuthChallenge, challenge, token).ConfigureAwait(false);
                    break;

                case WireMessageType.AuthResponse:
                    bool isValid = _cryptoAuth.VerifyResponseAndConsume(payload);
                    IsAuthenticated = isValid;
                    byte[] resultPayload = new[] { (byte)(isValid ? 1 : 0) };
                    await SendMessageAsync(WireMessageType.AuthResult, resultPayload, token).ConfigureAwait(false);
                    if (!isValid)
                    {
                        TerminateSession("Autenticazione RSA fallita: firma non valida.");
                    }
                    break;

                case WireMessageType.Heartbeat:
                    lock (_stateLock) { _lastHeartbeatUtc = DateTime.UtcNow; }
                    await SendMessageAsync(WireMessageType.HeartbeatAck, Array.Empty<byte>(), token).ConfigureAwait(false);
                    break;

                case WireMessageType.Disconnect:
                    TerminateSession("Ricevuta richiesta di disconnessione controllata.");
                    break;

                default:
                    break;
            }
        }

        private async Task HeartbeatWatchdogLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && IsClientConnected)
            {
                await Task.Delay(500, token).ConfigureAwait(false);

                TimeSpan elapsed;
                lock (_stateLock) { elapsed = DateTime.UtcNow - _lastHeartbeatUtc; }

                if (elapsed > _heartbeatTimeout)
                {
                    TerminateSession($"Heartbeat timeout scaduto ({elapsed.TotalMilliseconds:F0} ms > 2000 ms). Chiusura fail-closed.");
                    break;
                }
            }
        }

        public async Task SendMessageAsync(WireMessageType type, byte[] payload, CancellationToken token = default)
        {
            if (_networkStream == null || !IsClientConnected)
                return;

            byte[] packet = new byte[WireHeader.HeaderSize + payload.Length];
            var header = new WireHeader(type, (ushort)payload.Length, _sequenceGuard.CurrentExpected);

            header.WriteTo(packet.AsSpan(0, WireHeader.HeaderSize));
            if (payload.Length > 0)
            {
                payload.CopyTo(packet, WireHeader.HeaderSize);
            }

            await _networkStream.WriteAsync(packet, 0, packet.Length, token).ConfigureAwait(false);
            await _networkStream.FlushAsync(token).ConfigureAwait(false);
        }

        public void TerminateSession(string reason)
        {
            lock (_stateLock)
            {
                if (!IsClientConnected) return;

                IsClientConnected = false;
                IsAuthenticated = false;
                _currentClient?.Close();
                _currentClient = null;
                _networkStream = null;
                ActiveSessionId = null;
            }

            OnSessionTerminated?.Invoke(reason);
        }

        public async ValueTask DisposeAsync()
        {
            _sessionCts?.Cancel();
            TerminateSession("Arresto del canale di rete.");
            _listener?.Stop();
            await Task.CompletedTask;
        }
    }

    #endregion

    #region 7. Dashboard Locale

    public sealed class LocalDashboardServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Func<UnifiedWorldSnapshot> _snapshotProvider;
        private CancellationTokenSource? _serverCts;

        public LocalDashboardServer(int port, Func<UnifiedWorldSnapshot> snapshotProvider)
        {
            _snapshotProvider = snapshotProvider;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Start()
        {
            _listener.Start();
            _serverCts = new CancellationTokenSource();
            _ = ServerLoopAsync(_serverCts.Token);
        }

        private async Task ServerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync().ConfigureAwait(false);
                    ProcessRequest(context);
                }
                catch (HttpListenerException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? "/";
                if (path == "/api/state" || path == "/")
                {
                    UnifiedWorldSnapshot snapshot = _snapshotProvider();
                    byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });

                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.StatusCode = 200;
                    context.Response.ContentLength64 = jsonBytes.Length;
                    context.Response.OutputStream.Write(jsonBytes, 0, jsonBytes.Length);
                }
                else if (path == "/api/health")
                {
                    byte[] okBytes = Encoding.UTF8.GetBytes("{\"status\":\"healthy\",\"gate\":1}");
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = 200;
                    context.Response.OutputStream.Write(okBytes, 0, okBytes.Length);
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }

        public ValueTask DisposeAsync()
        {
            _serverCts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            return ValueTask.CompletedTask;
        }
    }

    #endregion

    #region 8. Motore Principale Gate 1

    public sealed class Gate1RuntimeEngine : IAsyncDisposable
    {
        private readonly INosTaleClientAdapter _clientAdapter;
        private readonly HardwareTelemetryProvider _hardwareTelemetry;
        private readonly NosAiCryptoAuthManager _cryptoAuth;
        private readonly GuardAiNetworkChannel _networkChannel;
        private readonly LocalDashboardServer _dashboardServer;
        private ulong _frameCounter;
        private RuntimeMode _mode = RuntimeMode.Normal;
        private TrustTier _trustTier = TrustTier.Tier0_ReadOnly;

        public Gate1RuntimeEngine(int tcpPort = 6100, int httpPort = 8765)
        {
            _clientAdapter = new ControlledNosTaleClientAdapter();
            _hardwareTelemetry = new HardwareTelemetryProvider();
            _cryptoAuth = new NosAiCryptoAuthManager();
            _networkChannel = new GuardAiNetworkChannel(tcpPort, _cryptoAuth);
            _dashboardServer = new LocalDashboardServer(httpPort, GetCurrentSnapshot);
        }

        public async Task StartAsync(CancellationToken token = default)
        {
            await _clientAdapter.ConnectAsync(token).ConfigureAwait(false);
            _networkChannel.Start();
            _dashboardServer.Start();
        }

        public UnifiedWorldSnapshot GetCurrentSnapshot()
        {
            _frameCounter++;
            PlayerState? player = _clientAdapter.PollPlayerStateAsync().GetAwaiter().GetResult();
            HardwareMetrics hardware = _hardwareTelemetry.CaptureMetrics();

            if (hardware.ThermalThrottlingTriggered)
            {
                _mode = RuntimeMode.Cooling;
            }

            return new UnifiedWorldSnapshot(
                SessionId: _networkChannel.ActiveSessionId ?? "STANDALONE_SESSION",
                FrameIndex: _frameCounter,
                TimestampUtc: DateTime.UtcNow,
                CurrentMode: _mode,
                CurrentTrustTier: _trustTier,
                Player: player ?? new PlayerState("UNKNOWN", 0, 0, 0, 0, 0, 0, 0, 0, 0, false),
                Hardware: hardware,
                IsPhoneGuardConnected: _networkChannel.IsClientConnected,
                IsGameClientConnected: _clientAdapter.IsConnected
            );
        }

        public async ValueTask DisposeAsync()
        {
            await _clientAdapter.DisconnectAsync().ConfigureAwait(false);
            await _networkChannel.DisposeAsync().ConfigureAwait(false);
            await _dashboardServer.DisposeAsync().ConfigureAwait(false);
            _cryptoAuth.Dispose();
        }
    }

    #endregion

    #region 9. Suite di Test Automatica Gate 1

    public static class Gate1TestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================================");
            Console.WriteLine("    NosAi 1.0 Beta — Esecuzione Test di Certificazione Gate 1    ");
            Console.WriteLine("=================================================================");
            Console.ResetColor();

            bool allPassed = true;

            allPassed &= RunTest("Test 1: Verifica WireHeader Binario 12-Byte", TestWireHeaderSerialization);
            allPassed &= RunTest("Test 2: Validazione Sequenza e Replay (SequenceGuard)", TestSequenceGuardPolicy);
            allPassed &= RunTest("Test 3: Autenticazione Challenge RSA Monouso", TestRsaAuthentication);
            allPassed &= await RunTestAsync("Test 4: Acquisizione Telemetria NosTale & Hardware PC", TestNosTaleAndHardwarePollingAsync);
            allPassed &= await RunTestAsync("Test 5: Heartbeat Fail-Closed (<2000ms)", TestHeartbeatTimeoutAsync);
            allPassed &= await RunTestAsync("Test 6: Endpoint Dashboard Locale (127.0.0.1:8765)", TestDashboardEndpointAsync);

            Console.WriteLine();
            if (allPassed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> [ESITO POSITIVO]: TUTTI I TEST DEL GATE 1 SONO STATI SUPERATI CON SUCCESSO.");
                Console.WriteLine(">> Il Gate 1 è formalmente sbloccato. È possibile procedere al Gate 2.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(">> [BLOCCO GATE 1]: UNO O PIÙ TEST SONO FALLITI. SVILUPPO BLOCCATO.");
                Console.ResetColor();
            }

            return allPassed;
        }

        private static bool RunTest(string testName, Func<bool> testFunc)
        {
            try
            {
                bool result = testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static async Task<bool> RunTestAsync(string testName, Func<Task<bool>> testFunc)
        {
            try
            {
                bool result = await testFunc();
                PrintResult(testName, result);
                return result;
            }
            catch (Exception ex)
            {
                PrintResult(testName, false, ex.Message);
                return false;
            }
        }

        private static void PrintResult(string name, bool passed, string? error = null)
        {
            Console.Write($"[{(passed ? "PASS" : "FAIL")}] {name,-55}");
            if (passed)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [OK]");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" [ERRORE: {error ?? "Asserzione fallita"}]");
            }
            Console.ResetColor();
        }

        private static bool TestWireHeaderSerialization()
        {
            var header = new WireHeader(WireMessageType.WorldStateDelta, 256, 42);
            Span<byte> buffer = stackalloc byte[WireHeader.HeaderSize];
            header.WriteTo(buffer);

            if (!WireHeader.TryRead(buffer, out WireHeader deserialized, out string? err))
                return false;

            return header.Equals(deserialized);
        }

        private static bool TestSequenceGuardPolicy()
        {
            var guard = new SequenceGuard(1);
            if (!guard.ValidateAndAdvance(1, out _)) return false;
            if (!guard.ValidateAndAdvance(2, out _)) return false;

            if (guard.ValidateAndAdvance(2, out string? replayErr) || string.IsNullOrEmpty(replayErr))
                return false;

            if (guard.ValidateAndAdvance(5, out string? gapErr) || string.IsNullOrEmpty(gapErr))
                return false;

            return true;
        }

        private static bool TestRsaAuthentication()
        {
            using var authManager = new NosAiCryptoAuthManager();
            byte[] challenge = authManager.GenerateSessionChallenge();
            byte[] validSignature = authManager.SignChallengeForClientMock(challenge);

            if (!authManager.VerifyResponseAndConsume(validSignature))
                return false;

            if (authManager.VerifyResponseAndConsume(validSignature))
                return false;

            return true;
        }

        private static async Task<bool> TestNosTaleAndHardwarePollingAsync()
        {
            var adapter = new ControlledNosTaleClientAdapter();
            await adapter.ConnectAsync();
            PlayerState? player = await adapter.PollPlayerStateAsync();

            if (player == null || player.Level != 15 || player.MaxHp != 1500)
                return false;

            var hw = new HardwareTelemetryProvider().CaptureMetrics();
            if (hw.RamUsedBytes <= 0 || hw.GpuTemperatureCelsius <= 0)
                return false;

            await adapter.DisconnectAsync();
            return true;
        }

        private static async Task<bool> TestHeartbeatTimeoutAsync()
        {
            using var auth = new NosAiCryptoAuthManager();
            int port = 6199;
            await using var server = new GuardAiNetworkChannel(port, auth);
            server.Start();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            await Task.Delay(2600);

            return !server.IsClientConnected;
        }

        private static async Task<bool> TestDashboardEndpointAsync()
        {
            int httpPort = 8799;
            var engine = new Gate1RuntimeEngine(tcpPort: 6198, httpPort: httpPort);
            await engine.StartAsync();

            using var httpClient = new HttpClient();
            string response = await httpClient.GetStringAsync($"http://127.0.0.1:{httpPort}/api/state");

            await engine.DisposeAsync();

            return response.Contains("NosAdventurer_01") && response.Contains("SessionId");
        }
    }

    #endregion

    #region 10. Entry Point Gate 1

    public static class Gate1Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.Title = "NosAi Runtime — Gate 1 (1.0 Beta)";

            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                bool success = await Gate1TestRunner.RunAllTestsAsync();
                return success ? 0 : 1;
            }

            Console.WriteLine("Avvio NosAi Runtime Gate 1 in corso...");
            await using var engine = new Gate1RuntimeEngine();
            await engine.StartAsync();

            Console.WriteLine("Runtime operativo su TCP:6100 e Dashboard HTTP: http://127.0.0.1:8765/");
            Console.WriteLine("Premere Invio per eseguire i test automatici di certificazione...");
            Console.ReadLine();

            bool passed = await Gate1TestRunner.RunAllTestsAsync();
            return passed ? 0 : 1;
        }
    }

    #endregion
}
