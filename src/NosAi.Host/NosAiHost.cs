using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NosAi.Adapter;
using NosAi.Core;
using NosAi.Security;
using NosAi.Storage;

namespace NosAi.Host;

/// <summary>Everything <see cref="NosAiHost.Compose"/> needs to wire up a Gate 1 run.</summary>
/// <param name="ListenPort">
/// <c>-1</c> (default) skips the transport listener, preserving the original
/// one-shot attach+journal bootstrap. <c>0</c> binds an ephemeral port;
/// any positive value binds that port.
/// </param>
public sealed record HostOptions(
    string ProcessName,
    string ExpectedModule,
    string ModuleSha256,
    int AttachTimeoutMs,
    SqliteJournalOptions JournalOptions,
    string SessionId,
    bool VerifyChainOnStart,
    int ListenPort = -1,
    string ListenAddress = "127.0.0.1",
    byte[]? StaticPrivateKey = null,
    byte[]? CapabilityRootKey = null);

/// <summary>What one <see cref="NosAiHost.RunAsync"/> pass found and recorded.</summary>
public readonly record struct HostBootstrapResult(
    bool Attached,
    FaultCode AttachFault,
    long JournaledSequence,
    bool? ChainIntact,
    long ChainFirstBrokenSequence);

/// <summary>
/// Gate 1 composition root (docs/ROADMAP_ESECUTIVA.md S:2.2). Owns the real
/// process adapter, the hash-chained journal, the dashboard, and — when
/// <see cref="HostOptions.ListenPort"/> is not <c>-1</c> — the Noise/CapBAC
/// frame loop against a mobile peer. The peer is never simulated here: a
/// connection is a real TCP socket running a real
/// <c>Noise_XX_25519_ChaChaPoly_SHA256</c> handshake. Human-in-the-loop
/// evidence on a physical phone remains <c>docs/TEST_RIMANDATI.md</c> T-06/T-07.
/// </summary>
public sealed class NosAiHost : IAsyncDisposable
{
    private readonly HostOptions _options;
    private readonly IMonotonicClock _clock;
    private readonly IEventJournal _journal;
    private readonly IGameProcessAdapter _adapter;
    private readonly DashboardHub _dashboard;
    private readonly byte[] _staticPrivateKey;
    private readonly byte[] _capabilityRootKey;
    private readonly HmacCapabilityValidator _capabilityValidator;
    private readonly TaskCompletionSource _listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TcpListener? _listener;
    private bool _disposed;

    public NosAiHost(HostOptions options, IMonotonicClock clock, IEventJournal journal, IGameProcessAdapter adapter, DashboardHub dashboard)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));

        _staticPrivateKey = options.StaticPrivateKey ?? (options.ListenPort >= 0
            ? NoiseXxSession.GenerateStaticPrivateKey()
            : []);
        _capabilityRootKey = options.CapabilityRootKey ?? (options.ListenPort >= 0
            ? RandomNumberGenerator.GetBytes(32)
            : []);
        _capabilityValidator = _capabilityRootKey.Length > 0
            ? new HmacCapabilityValidator(_capabilityRootKey)
            : new HmacCapabilityValidator(RandomNumberGenerator.GetBytes(32));

        SequenceGuard = new SequenceGuard();
    }

    public DashboardHub Dashboard => _dashboard;
    public IEventJournal Journal => _journal;
    public IGameProcessAdapter Adapter => _adapter;
    public SequenceGuard SequenceGuard { get; private set; }
    public byte[] CapabilityRootKey => _capabilityRootKey;
    public int BoundPort { get; private set; }
    public Task WhenListening => _listening.Task;

    /// <summary>
    /// Wires the real dependencies: <see cref="Win32ProcessAdapter"/> and a
    /// journal opened from the labeled volume in <paramref name="options"/>
    /// (fails closed -- no fallback drive -- if that volume is not attached).
    /// </summary>
    public static NosAiHost Compose(HostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        IEventJournal journal = SqliteEventJournal.OpenFromVolume(options.JournalOptions, options.SessionId);
        return new NosAiHost(options, new MonotonicClock(), journal, new Win32ProcessAdapter(), new DashboardHub());
    }

    /// <summary>
    /// Same as <see cref="Compose"/>, but opens the journal at an explicit
    /// path instead of resolving <see cref="HostOptions.JournalOptions"/>'s
    /// volume label. For local development and tests where the
    /// <c>NOSAI-SSD</c> volume is not attached; never used to make production
    /// bootstrap silently accept a different drive.
    /// </summary>
    public static NosAiHost ComposeWithJournalPath(HostOptions options, string journalDatabasePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDatabasePath);
        IEventJournal journal = new SqliteEventJournal(journalDatabasePath, options.JournalOptions, options.SessionId);
        return new NosAiHost(options, new MonotonicClock(), journal, new Win32ProcessAdapter(), new DashboardHub());
    }

    public async ValueTask<HostBootstrapResult> RunAsync(CancellationToken ct)
    {
        var attachOptions = new ProcessAttachOptions(_options.ProcessName, _options.ExpectedModule, _options.ModuleSha256, _options.AttachTimeoutMs);
        bool attached = _adapter.TryAttach(attachOptions, out FaultCode attachFault);

        long sequence = JournalUtf8($"attach={attached};fault={attachFault};process={_options.ProcessName}");

        bool? chainIntact = null;
        long firstBroken = -1;
        if (_options.VerifyChainOnStart)
            chainIntact = _journal.VerifyChain(0, out firstBroken);

        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, attached ? "attached" : "attach-failed", attachFault));

        var result = new HostBootstrapResult(attached, attachFault, sequence, chainIntact, firstBroken);

        if (_options.ListenPort >= 0)
            await ServeTransportAsync(ct).ConfigureAwait(false);

        return result;
    }

    private async Task ServeTransportAsync(CancellationToken ct)
    {
        IPAddress address = IPAddress.Parse(_options.ListenAddress);
        int requested = _options.ListenPort;
        _listener = new TcpListener(address, requested < 0 ? 0 : requested);
        try
        {
            _listener.Start();
            BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _listening.TrySetResult();
        }
        catch (Exception ex)
        {
            _listening.TrySetException(ex);
            throw;
        }

        JournalUtf8($"listen={_options.ListenAddress}:{BoundPort}");
        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, $"listen:{BoundPort}", FaultCode.None));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                    client.NoDelay = true;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                await HandleSessionAsync(client, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task HandleSessionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        using (NoiseXxSession session = new(initiator: false, _staticPrivateKey))
        {
            SequenceGuard incoming = new();
            SequenceGuard = incoming;
            uint outboundSequence = 0;
            FrameTagCalculator? tags = null;
            bool capabilityGranted = false;
            byte[] noiseBuffer = ArrayPool<byte>.Shared.Rent(LengthPrefixedRecord.MaxLength);
            byte[] plainBuffer = ArrayPool<byte>.Shared.Rent(LengthPrefixedRecord.MaxLength);
            FaultCode disconnectFault = FaultCode.Network;

            try
            {
                if (!await HandshakeAsResponderAsync(session, stream, noiseBuffer, plainBuffer, ct).ConfigureAwait(false))
                {
                    JournalUtf8($"handshake;state={session.State}");
                    _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "handshake-failed", FaultCode.Network));
                    return;
                }

                tags = new FrameTagCalculator(session.DeriveFrameSessionKey());
                JournalUtf8("handshake;state=Transport");
                _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "transport", FaultCode.None));

                while (!ct.IsCancellationRequested)
                {
                    if (session.IsRekeyDue)
                    {
                        await SendEmptyFrameAsync(session, stream, tags, FrameOpCode.Rekey, outboundSequence++, noiseBuffer, plainBuffer, ct).ConfigureAwait(false);
                        session.Rekey();
                    }

                    int cipherLength = await LengthPrefixedRecord.ReadAsync(stream, noiseBuffer, ct).ConfigureAwait(false);
                    if (cipherLength < 0)
                    {
                        disconnectFault = FaultCode.Network;
                        break;
                    }

                    int plainLength;
                    try
                    {
                        plainLength = session.ReadMessage(noiseBuffer.AsSpan(0, cipherLength), plainBuffer);
                    }
                    catch
                    {
                        sessionFailedJournal("transport-decrypt");
                        disconnectFault = FaultCode.FrameInvalid;
                        break;
                    }

                    if (!TryDecodeCopied(plainBuffer, plainLength, tags, out NosFrameHeader header, out byte[] payload, out FaultCode decodeFault))
                    {
                        JournalUtf8($"frame;fault={decodeFault}");
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame-invalid", decodeFault));
                        continue;
                    }

                    if (!incoming.TryAccept(header.Sequence))
                    {
                        JournalUtf8($"frame;seq={header.Sequence};fault={FaultCode.Replay}");
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "replay", FaultCode.Replay));
                        continue;
                    }

                    if (!TryGetOpCode(header.OpCode, out FrameOpCode op))
                    {
                        JournalUtf8($"frame;op={header.OpCode};fault={FaultCode.FrameInvalid}");
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame-invalid", FaultCode.FrameInvalid));
                        continue;
                    }

                    if (op == FrameOpCode.Rekey)
                    {
                        session.Rekey();
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame:rekey", FaultCode.None), countAsAcceptedFrame: true);
                        continue;
                    }

                    if (!capabilityGranted)
                    {
                        if (op != FrameOpCode.PresentCapability)
                        {
                            CapabilityVerdict denied = new(false, FaultCode.ScopeDenied, 0);
                            outboundSequence = await SendDecisionAsync(session, stream, tags, outboundSequence, denied, noiseBuffer, plainBuffer, ct).ConfigureAwait(false);
                            disconnectFault = FaultCode.ScopeDenied;
                            break;
                        }

                        CapabilityVerdict verdict = ValidatePresentedCapability(payload);
                        outboundSequence = await SendDecisionAsync(session, stream, tags, outboundSequence, verdict, noiseBuffer, plainBuffer, ct).ConfigureAwait(false);
                        JournalUtf8($"capability;granted={verdict.Granted};fault={verdict.Fault}");
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, verdict.Granted ? "capability-granted" : "capability-denied", verdict.Fault), countAsAcceptedFrame: verdict.Granted);
                        if (!verdict.Granted)
                        {
                            disconnectFault = verdict.Fault;
                            break;
                        }

                        capabilityGranted = true;
                        continue;
                    }

                    if (op == FrameOpCode.Disconnect)
                    {
                        disconnectFault = FaultCode.None;
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame:disconnect", FaultCode.None), countAsAcceptedFrame: true);
                        break;
                    }

                    if (op == FrameOpCode.Heartbeat)
                    {
                        JournalUtf8($"frame;op=heartbeat;seq={header.Sequence}");
                        _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame:heartbeat", FaultCode.None), countAsAcceptedFrame: true);
                        continue;
                    }

                    JournalUtf8($"frame;op={header.OpCode};fault={FaultCode.FrameInvalid}");
                    _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "frame-invalid", FaultCode.FrameInvalid));
                }
            }
            catch (OperationCanceledException)
            {
                disconnectFault = FaultCode.Timeout;
            }
            catch (EndOfStreamException)
            {
                disconnectFault = FaultCode.Network;
            }
            catch (IOException)
            {
                disconnectFault = FaultCode.Network;
            }
            finally
            {
                JournalUtf8($"disconnect;fault={disconnectFault};frames={_dashboard.AcceptedFrameCount}");
                _dashboard.Publish(new TelemetryFrame(_clock.UnixMillis, PipelineStage.Observe, "disconnected", disconnectFault));
                tags?.Dispose();
                ArrayPool<byte>.Shared.Return(noiseBuffer);
                ArrayPool<byte>.Shared.Return(plainBuffer);
            }
        }

        void sessionFailedJournal(string reason)
        {
            JournalUtf8($"handshake;state=Failed;reason={reason}");
        }
    }

    private CapabilityVerdict ValidatePresentedCapability(ReadOnlySpan<byte> payload)
    {
        if (!CapabilityToken.TryRead(payload, out CapabilityToken token))
            return new CapabilityVerdict(false, FaultCode.FrameInvalid, 0);

        return _capabilityValidator.Validate(token, PipelineStage.Guard, CapabilityScope.Observe, _clock.UnixMillis);
    }

    private static async Task<uint> SendDecisionAsync(
        INoiseSession session,
        Stream stream,
        FrameTagCalculator tags,
        uint outboundSequence,
        CapabilityVerdict verdict,
        byte[] noiseBuffer,
        byte[] plainBuffer,
        CancellationToken ct)
    {
        byte[] decision = new byte[CapabilityVerdict.WireLength];
        verdict.WriteTo(decision);
        int cipherLength = EncryptFrame(session, tags, FrameOpCode.CapabilityDecision, outboundSequence, decision, noiseBuffer, plainBuffer);
        await LengthPrefixedRecord.WriteAsync(stream, noiseBuffer.AsMemory(0, cipherLength), ct).ConfigureAwait(false);
        return outboundSequence + 1;
    }

    private static async Task SendEmptyFrameAsync(
        INoiseSession session,
        Stream stream,
        FrameTagCalculator tags,
        FrameOpCode op,
        uint sequence,
        byte[] noiseBuffer,
        byte[] plainBuffer,
        CancellationToken ct)
    {
        int cipherLength = EncryptFrame(session, tags, op, sequence, ReadOnlySpan<byte>.Empty, noiseBuffer, plainBuffer);
        await LengthPrefixedRecord.WriteAsync(stream, noiseBuffer.AsMemory(0, cipherLength), ct).ConfigureAwait(false);
    }

    private static int EncryptFrame(
        INoiseSession session,
        FrameTagCalculator tags,
        FrameOpCode op,
        uint sequence,
        ReadOnlySpan<byte> payload,
        byte[] noiseBuffer,
        byte[] plainBuffer)
    {
        int frameLength = FrameCodec.Encode((byte)op, sequence, payload, tags, plainBuffer);
        return session.WriteMessage(plainBuffer.AsSpan(0, frameLength), noiseBuffer);
    }

    private static bool TryDecodeCopied(
        byte[] plainBuffer,
        int plainLength,
        FrameTagCalculator tags,
        out NosFrameHeader header,
        out byte[] payload,
        out FaultCode fault)
    {
        if (!FrameCodec.TryDecode(plainBuffer.AsSpan(0, plainLength), tags, out header, out ReadOnlySpan<byte> payloadSpan, out fault))
        {
            payload = [];
            return false;
        }

        payload = payloadSpan.ToArray();
        return true;
    }

    private static async Task<bool> HandshakeAsResponderAsync(
        INoiseSession session,
        Stream stream,
        byte[] noiseBuffer,
        byte[] plainBuffer,
        CancellationToken ct)
    {
        try
        {
            int first = await LengthPrefixedRecord.ReadAsync(stream, noiseBuffer, ct).ConfigureAwait(false);
            if (first <= 0)
                return false;

            session.ReadMessage(noiseBuffer.AsSpan(0, first), plainBuffer);

            int second = session.WriteMessage(ReadOnlySpan<byte>.Empty, noiseBuffer);
            await LengthPrefixedRecord.WriteAsync(stream, noiseBuffer.AsMemory(0, second), ct).ConfigureAwait(false);

            int third = await LengthPrefixedRecord.ReadAsync(stream, noiseBuffer, ct).ConfigureAwait(false);
            if (third <= 0)
                return false;

            session.ReadMessage(noiseBuffer.AsSpan(0, third), plainBuffer);
            return session.State == NoiseHandshakeState.Transport;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetOpCode(byte value, out FrameOpCode op)
    {
        op = (FrameOpCode)value;
        return value is (byte)FrameOpCode.Heartbeat
            or (byte)FrameOpCode.PresentCapability
            or (byte)FrameOpCode.CapabilityDecision
            or (byte)FrameOpCode.Disconnect
            or (byte)FrameOpCode.Rekey;
    }

    private long JournalUtf8(string text)
    {
        return _journal.Append(new JournalRecord(0, _clock.UnixMillis, PipelineStage.Observe, Encoding.UTF8.GetBytes(text), ReadOnlyMemory<byte>.Empty));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _listener?.Stop();
        _listening.TrySetCanceled();
        _adapter.Dispose();
        await _journal.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }
}
