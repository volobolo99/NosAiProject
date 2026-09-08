using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Testing;

namespace NosAi.LiveIntegration.Capture;

/// <summary>Classifies how a pre-login capture session came to an end.</summary>
public enum PreLoginOutcomeKind
{
    /// <summary>The live stream satisfied the whole close criterion from an observed handshake onwards.</summary>
    Complete = 0,

    /// <summary>The session ended without a fully satisfying live observation, but the recording is still meaningful.</summary>
    Partial = 1,

    /// <summary>The capture could not be produced at all.</summary>
    Failed = 2,
}

/// <summary>
/// Describes the result of a <see cref="PreLoginCaptureSession"/> run: where the capture file lives,
/// how much of the conversation was observed, which opcodes were seen, and why the session stopped.
/// </summary>
public sealed record PreLoginCaptureOutcome(
    PreLoginOutcomeKind Kind,
    string? CapturePath,
    IPEndPoint? Endpoint,
    long PacketsBeforeDiscovery,
    long PacketsAfterDiscovery,
    long PacketsWritten,
    IReadOnlyList<string> OpcodesObserved,
    IReadOnlyList<string> OpcodesMissing,
    long DroppedForCapacity,
    long DroppedForAge,
    bool HandshakeMayBeMissing,
    int VitalsReadings,
    DataSourceKind VitalsProvenance,
    string? VitalsReason,
    string? Reason)
{
    /// <summary>True when at least one vitals reading came from the real live wire.</summary>
    public bool VitalsSatisfied => VitalsReadings > 0 && VitalsProvenance == DataSourceKind.Live;
}

/// <summary>
/// A capture session that waits for the game client to arrive, slices its endpoint out of the wide
/// prelude buffer, records the conversation into a <c>.noscap</c> file while it keeps flowing, and
/// stops by itself once the character load has been observed on the wire.
/// </summary>
/// <remarks>
/// The capture has to be armed before the client: the endpoint does not exist until the client has
/// connected, and by the time it is known the TCP handshake has already passed. That is why the wide
/// prelude buffer keeps the early conversation, why this session slices the buffer by endpoint once
/// the client arrives, and why the presence of the SYN inside the slice is verified as an observed
/// fact that the recording really starts from the beginning of the conversation.
/// </remarks>
public sealed class PreLoginCaptureSession : IDisposable
{
    /// <summary>The opcode that reports character statistics; one of the close-criterion opcodes.</summary>
    public const string OpcodeStat = "stat";

    /// <summary>The opcode that reports an entity entering the map; one of the close-criterion opcodes.</summary>
    public const string OpcodeEnter = "in";

    /// <summary>The opcode that reports inventory content; either it or <see cref="OpcodeEquip"/> completes the close criterion.</summary>
    public const string OpcodeInventory = "ivn";

    /// <summary>The opcode that reports equipped items; either it or <see cref="OpcodeInventory"/> completes the close criterion.</summary>
    public const string OpcodeEquip = "equip";

    /// <summary>How long the session waits for the client endpoint before giving up.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    /// <summary>How often the session re-checks its conditions while idle.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(50);

    private readonly BroadWirePrelude _prelude;
    private readonly ClientArrivalWatcher _watcher;
    private readonly string _capturePath;
    private readonly DataSourceKind _streamProvenance;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;
    private readonly HashSet<string> _opcodes = new(StringComparer.Ordinal);
    private readonly NosTaleWorldProtocolDecoder _decoder = new();

    private CancellationTokenSource? _writerCts;
    private string? _writerFailure;
    private long _written;
    private int _vitalsReadings;
    private DataSourceKind _vitalsProvenance = DataSourceKind.Unknown;
    private string? _vitalsReason;
    private string _endpointHost = string.Empty;
    private int _endpointPort;
    private bool _disposed;

    /// <summary>
    /// Creates a session bound to the given wide prelude and client arrival watcher. The session does
    /// not own the prelude: whoever constructed it remains responsible for disposing it.
    /// </summary>
    /// <param name="prelude">The wide capture buffer that keeps packets for every endpoint until the client is discovered.</param>
    /// <param name="watcher">The watcher that reports when the game client process has an identifiable session endpoint.</param>
    /// <param name="capturePath">Where the <c>.noscap</c> file is written once the endpoint is known.</param>
    /// <param name="streamProvenance">Provenance stamped onto decoded frames; only <see cref="DataSourceKind.Live"/> can close the session as complete.</param>
    /// <param name="timeout">Overall budget for discovering the endpoint and meeting the close criterion; defaults to <see cref="DefaultTimeout"/>.</param>
    /// <param name="pollInterval">Idle re-check cadence of the main loop; defaults to <see cref="DefaultPollInterval"/>.</param>
    public PreLoginCaptureSession(
        BroadWirePrelude prelude,
        ClientArrivalWatcher watcher,
        string capturePath,
        DataSourceKind streamProvenance = DataSourceKind.Live,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(prelude);
        ArgumentNullException.ThrowIfNull(watcher);
        ArgumentException.ThrowIfNullOrWhiteSpace(capturePath);
        if (timeout is { } t && t <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than TimeSpan.Zero.");
        }

        if (pollInterval is { } p && p <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be greater than TimeSpan.Zero.");
        }

        _prelude = prelude;
        _watcher = watcher;
        _capturePath = capturePath;
        _streamProvenance = streamProvenance;
        _timeout = timeout ?? DefaultTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>
    /// Runs the session: waits for the client endpoint, slices the prelude, records the endpoint
    /// conversation while decoding it, and stops when the close criterion is met or the session ends.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait for the endpoint or the ongoing capture.</param>
    /// <param name="onStatus">Optional callback receiving short status messages as the session progresses.</param>
    /// <returns>The outcome of the capture session.</returns>
    public PreLoginCaptureOutcome Run(CancellationToken cancellationToken = default, Action<string>? onStatus = null)
    {
        var elapsed = Stopwatch.StartNew();

        ClientArrivalStatus status = _watcher.Poll();
        ClientArrivalStage lastStage = status.Stage;
        string? lastReason = status.Reason;
        onStatus?.Invoke($"waiting:{status.Stage}:{status.Reason}");
        while (!status.Identified)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return EndpointNeverResolved("cancelled_before_endpoint");
            }

            if (elapsed.Elapsed >= _timeout)
            {
                return EndpointNeverResolved($"no_client_endpoint:{status.Reason ?? status.Stage.ToString()}");
            }

            cancellationToken.WaitHandle.WaitOne(_watcher.PollInterval);
            status = _watcher.Poll();
            if (status.Stage != lastStage || status.Reason != lastReason)
            {
                lastStage = status.Stage;
                lastReason = status.Reason;
                onStatus?.Invoke($"waiting:{status.Stage}:{status.Reason}");
            }
        }

        IPEndPoint endpoint = status.Endpoint!;
        _endpointHost = endpoint.Address.ToString();
        _endpointPort = endpoint.Port;
        onStatus?.Invoke($"endpoint_identified:{endpoint}");

        PreludeSlice slice = _prelude.FocusOn(endpoint.Address, endpoint.Port);
        long packetsBeforeDiscovery = slice.Packets.Count;
        onStatus?.Invoke($"sliced:{packetsBeforeDiscovery}");

        bool handshakeMayBeMissing = true;
        foreach (CapturedPacket packet in slice.Packets)
        {
            ParsedPacket parsed = Ipv4TcpParser.Parse(packet.Raw.Span);
            if (parsed.Ok && parsed.Syn)
            {
                handshakeMayBeMissing = false;
                break;
            }
        }

        var tee = new PreLoginTeeSource(_prelude, slice.Packets, endpoint.Address, endpoint.Port);

        _writerCts = new CancellationTokenSource();
        CancellationTokenSource writerCts = _writerCts;
        var writerThread = new Thread(() =>
        {
            try
            {
                _written = CaptureFile.Record(tee, _capturePath, writerCts.Token, TimeSpan.FromMilliseconds(200));
            }
            catch (Exception ex)
            {
                _writerFailure = $"capture_write_failed:{ex.GetType().Name}";
            }
        })
        {
            IsBackground = true,
            Name = "prelogin-capture-writer",
        };
        writerThread.Start();

        var engine = new GameTrafficCaptureEngine(
            new InMemoryPacketSource(endpoint.Address, endpoint.Port, Array.Empty<CapturedPacket>()),
            NosTaleWorldFramer.Factory(_streamProvenance));
        engine.FrameProduced += OnFrame;

        string cause;
        long packetsDecoded = 0;
        while (true)
        {
            if (_writerFailure != null)
            {
                cause = "writer_failed";
                break;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                cause = "cancelled";
                break;
            }

            if (elapsed.Elapsed >= _timeout)
            {
                cause = "timeout";
                break;
            }

            if (CriterionMet)
            {
                cause = "criterion_met";
                break;
            }

            if (tee.Ended && tee.DecodeQueueEmpty)
            {
                cause = "source_ended";
                break;
            }

            if (tee.TryDequeueForDecoding(out CapturedPacket packet))
            {
                engine.Pump(packet);
                packetsDecoded++;
                continue;
            }

            cancellationToken.WaitHandle.WaitOne(_pollInterval);
        }

        long packetsAfterDiscovery = Math.Max(0, packetsDecoded - packetsBeforeDiscovery);

        writerCts.Cancel();
        writerThread.Join(TimeSpan.FromSeconds(5));
        engine.FrameProduced -= OnFrame;
        tee.Dispose();

        string[] missing = BuildMissingOpcodes();
        string[] observed = _opcodes.OrderBy(o => o, StringComparer.Ordinal).ToArray();

        DataSourceKind vitalsProvenance = _vitalsProvenance;
        string? vitalsReason = _vitalsReason;
        if (_vitalsReadings == 0)
        {
            vitalsProvenance = DataSourceKind.Unknown;
            vitalsReason = "no_vitals_observed";
        }

        PreLoginOutcomeKind kind;
        string? reason;
        if (_writerFailure != null)
        {
            kind = PreLoginOutcomeKind.Failed;
            reason = _writerFailure;
        }
        else if (cause == "cancelled")
        {
            kind = PreLoginOutcomeKind.Partial;
            reason = $"cancelled:missing={string.Join(',', missing)}";
        }
        else if (!CriterionMet && cause == "timeout")
        {
            kind = PreLoginOutcomeKind.Partial;
            reason = $"timeout_before_criterion:missing={string.Join(',', missing)}";
        }
        else if (!CriterionMet && cause == "source_ended")
        {
            kind = PreLoginOutcomeKind.Partial;
            reason = $"source_ended_before_criterion:missing={string.Join(',', missing)}";
        }
        else if (CriterionMet && _streamProvenance != DataSourceKind.Live)
        {
            kind = PreLoginOutcomeKind.Partial;
            reason = $"stream_not_live:{_streamProvenance.ToWire()}";
        }
        else if (CriterionMet && handshakeMayBeMissing)
        {
            kind = PreLoginOutcomeKind.Partial;
            reason = "handshake_not_in_recording:no_syn_before_discovery";
        }
        else
        {
            kind = PreLoginOutcomeKind.Complete;
            reason = null;
        }

        return new PreLoginCaptureOutcome(
            kind,
            _capturePath,
            endpoint,
            packetsBeforeDiscovery,
            packetsAfterDiscovery,
            _written,
            observed,
            missing,
            _prelude.DroppedForCapacity,
            _prelude.DroppedForAge,
            handshakeMayBeMissing,
            _vitalsReadings,
            vitalsProvenance,
            vitalsReason,
            reason);
    }

    /// <summary>
    /// Releases the session resources. Idempotent, and never disposes the prelude: the session does not own it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource? writerCts = _writerCts;
        if (writerCts != null)
        {
            writerCts.Cancel();
            writerCts.Dispose();
        }
    }

    /// <summary>True once stat, in and at least one of ivn/equip have been observed on the decoded stream.</summary>
    private bool CriterionMet =>
        _opcodes.Contains(OpcodeStat)
        && _opcodes.Contains(OpcodeEnter)
        && (_opcodes.Contains(OpcodeInventory) || _opcodes.Contains(OpcodeEquip));

    /// <summary>Builds the list of close-criterion opcodes that were never observed, in the documented order.</summary>
    private string[] BuildMissingOpcodes()
    {
        var missing = new List<string>(3);
        if (!_opcodes.Contains(OpcodeStat))
        {
            missing.Add(OpcodeStat);
        }

        if (!_opcodes.Contains(OpcodeEnter))
        {
            missing.Add(OpcodeEnter);
        }

        if (!_opcodes.Contains(OpcodeInventory) && !_opcodes.Contains(OpcodeEquip))
        {
            missing.Add("ivn|equip");
        }

        return missing.ToArray();
    }

    /// <summary>Builds the zeroed failed outcome returned when the client endpoint is never discovered.</summary>
    private static PreLoginCaptureOutcome EndpointNeverResolved(string reason) =>
        new(
            PreLoginOutcomeKind.Failed,
            null,
            null,
            0,
            0,
            0,
            Array.Empty<string>(),
            new[] { OpcodeStat, OpcodeEnter, "ivn|equip" },
            0,
            0,
            true,
            0,
            DataSourceKind.Unknown,
            "no_vitals_observed",
            reason);

    /// <summary>
    /// Handles a decoded game frame: records its opcode, feeds its body through the stateful semantic
    /// decoder, and tracks vitals provenance. Invoked synchronously by <see cref="GameTrafficCaptureEngine.Pump"/>
    /// from the main loop thread only, so it needs no locking.
    /// </summary>
    private void OnFrame(CapturedGameFrame frame)
    {
        if (frame.Frame.Source == DataSourceKind.Unknown)
        {
            return;
        }

        IReadOnlyList<string> lines = NosTaleWorldDecoder.Decode(frame.Frame.Body.Span);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separator = line.IndexOf(' ');
            _opcodes.Add(separator < 0 ? line : line.Substring(0, separator));
        }

        var observed = new ObservedPacket(
            frame.TimestampUtc,
            frame.Direction == StreamDirection.Inbound ? NetworkDirection.Inbound : NetworkDirection.Outbound,
            _endpointHost,
            _endpointPort,
            frame.Frame.Body,
            frame.Frame.Source);
        DecodedObservations decoded = _decoder.Decode(observed);

        if (decoded.Vitals is PlayerVitals vitals)
        {
            _vitalsReadings++;
            if (_vitalsReadings == 1)
            {
                _vitalsProvenance = vitals.Source;
            }
            else if (vitals.Source != DataSourceKind.Live)
            {
                _vitalsProvenance = vitals.Source;
            }

            if (vitals.Source != DataSourceKind.Live && _vitalsReason == null)
            {
                _vitalsReason = $"vitals_not_live:{vitals.Source.ToWire()}";
            }
        }
    }

    /// <summary>
    /// A tee over the discovered endpoint: serves the sliced backlog first, then hands over to the
    /// live prelude, while queueing every delivered packet for decoding. Finite, so that the recording
    /// ends by itself when the underlying prelude source ends and has been fully drained.
    /// </summary>
    private sealed class PreLoginTeeSource : IPacketSource, IFinitePacketSource
    {
        private readonly BroadWirePrelude _prelude;
        private readonly IReadOnlyList<CapturedPacket> _backlog;
        private readonly IPAddress _serverAddress;
        private readonly int _serverPort;
        private readonly ConcurrentQueue<CapturedPacket> _forDecoding = new();
        private int _backlogIndex;
        private bool _preludeDrained;

        public PreLoginTeeSource(BroadWirePrelude prelude, IReadOnlyList<CapturedPacket> backlog, IPAddress serverAddress, int serverPort)
        {
            _prelude = prelude;
            _backlog = backlog;
            _serverAddress = serverAddress;
            _serverPort = serverPort;
        }

        public IPAddress ServerAddress => _serverAddress;

        public int ServerPort => _serverPort;

        public bool Ended => _backlogIndex >= _backlog.Count && _prelude.SourceEnded && _preludeDrained;

        public bool DecodeQueueEmpty => _forDecoding.IsEmpty;

        public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
        {
            if (_backlogIndex < _backlog.Count)
            {
                packet = _backlog[_backlogIndex++];
                _forDecoding.Enqueue(packet);
                return true;
            }

            if (_prelude.TryRead(timeout, out packet))
            {
                _forDecoding.Enqueue(packet);
                return true;
            }

            if (_prelude.SourceEnded)
            {
                _preludeDrained = true;
            }

            return false;
        }

        public bool TryDequeueForDecoding(out CapturedPacket packet) => _forDecoding.TryDequeue(out packet);

        public void Dispose()
        {
            // The session does not own the prelude, so there is nothing to release here.
        }
    }
}

/// <summary>
/// Command-line entry point behind <c>--await-client-capture</c>: arms the wide prelude before any
/// client exists, waits for the game client to connect, records the whole pre-login conversation,
/// stops by itself once the close criterion has been observed and writes the deferred evidence
/// records T-14 and T-05.
/// </summary>
public static class AwaitClientCaptureCommand
{
    /// <summary>The command-line flag that activates this command.</summary>
    public const string Flag = "--await-client-capture";

    /// <summary>Default directory where capture files land when no <c>--out</c> path is supplied.</summary>
    public const string DefaultDirectory = "data";

    /// <summary>Default session timeout, in minutes, applied when <c>--timeout</c> is absent or invalid.</summary>
    public const int DefaultTimeoutMinutes = 15;

    /// <summary>Builds the default capture path for the given UTC instant inside <see cref="DefaultDirectory"/>.</summary>
    /// <param name="nowUtc">The UTC instant stamped onto the file name; converted to UTC when it is not already.</param>
    /// <returns>A path like <c>data/nostale_prelogin_yyyyMMdd_HHmmssZ.noscap</c>.</returns>
    public static string DefaultPath(DateTime nowUtc) =>
        Path.Combine(DefaultDirectory, string.Create(CultureInfo.InvariantCulture, $"nostale_prelogin_{nowUtc.ToUniversalTime():yyyyMMdd_HHmmss}Z.noscap"));

    /// <summary>
    /// Runs the command to completion: announces what is about to happen, opens the wide prelude,
    /// waits for the client while recording, prints the outcome and writes the deferred evidence.
    /// Returns zero only when the session completed with a fully live observation.
    /// </summary>
    /// <param name="outPath">Where the <c>.noscap</c> file must be written; when null or blank, <see cref="DefaultPath"/> is used.</param>
    /// <param name="timeoutMinutes">Session budget in minutes; when not positive, <see cref="DefaultTimeoutMinutes"/> is used.</param>
    /// <returns>Zero when <see cref="PreLoginOutcomeKind.Complete"/> was reached, one otherwise.</returns>
    /// <param name="report">Receives every line the command would otherwise print; the console entry point passes <see cref="Console.WriteLine(string)"/>.</param>
    /// <param name="cancellationToken">Stops the wait and the capture; the console entry point wires Ctrl+C to it.</param>
    public static int Run(string? outPath, int timeoutMinutes, Action<string> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        string path = string.IsNullOrWhiteSpace(outPath) ? DefaultPath(DateTime.UtcNow) : outPath;
        int minutes = timeoutMinutes > 0 ? timeoutMinutes : DefaultTimeoutMinutes;

        report("=== Cattura in attesa del client ===");
        report($"File di destinazione: {path}");
        report($"Scadenza: {minutes} minuti");
        report("L'operatore deve solo aprire NosTale: la cattura riconosce l'arrivo del client e si ferma da sola.");

        BroadWirePrelude? prelude = BroadWirePrelude.TryOpen(out string? failure);
        if (prelude == null)
        {
            report($"[NON DISPONIBILE] {failure}");
            report("Senza il driver di cattura non viene registrato nulla dal filo live e non viene inventato niente.");
            return 1;
        }

        using (prelude)
        {
            var watcher = new ClientArrivalWatcher();
            using var session = new PreLoginCaptureSession(prelude, watcher, path, DataSourceKind.Live, TimeSpan.FromMinutes(minutes));

            PreLoginCaptureOutcome outcome = session.Run(cancellationToken, line => report($"[stato] {line}"));

            report($"Esito: {outcome.Kind}");
            report($"File prodotto: {outcome.CapturePath ?? "(nessuno)"}");
            report($"Endpoint: {outcome.Endpoint?.ToString() ?? "(nessuno)"}");
            report($"Pacchetti prima della scoperta: {outcome.PacketsBeforeDiscovery}");
            report($"Pacchetti dopo la scoperta: {outcome.PacketsAfterDiscovery}");
            report($"Pacchetti scritti: {outcome.PacketsWritten}");
            report($"Opcodi osservati: {string.Join(",", outcome.OpcodesObserved)}");
            report($"Opcodi mancanti: {string.Join(",", outcome.OpcodesMissing)}");
            report($"Scartati per capienza: {outcome.DroppedForCapacity}");
            report($"Scartati per anzianita': {outcome.DroppedForAge}");
            report($"Handshake potrebbe mancare: {outcome.HandshakeMayBeMissing}");
            report($"Letture di vitals: {outcome.VitalsReadings} (provenienza {outcome.VitalsProvenance.ToWire()})");
            if (outcome.Reason != null)
            {
                report($"Motivo: {outcome.Reason}");
            }

            try
            {
                string[] attachments = outcome.CapturePath is { } capturePath
                    ? new[] { capturePath }
                    : Array.Empty<string>();

                IReadOnlyList<DeferredCriterion> t14Criteria = DeferredEvidence.CriteriaFor("T-14");
                var t14List = new List<DeferredCriterion>(t14Criteria.Count);
                foreach (DeferredCriterion criterion in t14Criteria)
                {
                    bool satisfied = criterion.Id switch
                    {
                        "capture_armed_before_client" => !outcome.HandshakeMayBeMissing,
                        "handshake_recorded" => !outcome.HandshakeMayBeMissing,
                        "character_load_observed" => outcome.OpcodesMissing.Count == 0,
                        _ => criterion.Satisfied,
                    };
                    t14List.Add(criterion with { Satisfied = satisfied });
                }

                var t14Measurements = new Dictionary<string, string>
                {
                    ["packets_before_discovery"] = outcome.PacketsBeforeDiscovery.ToString(CultureInfo.InvariantCulture),
                    ["packets_after_discovery"] = outcome.PacketsAfterDiscovery.ToString(CultureInfo.InvariantCulture),
                    ["packets_written"] = outcome.PacketsWritten.ToString(CultureInfo.InvariantCulture),
                    ["opcodes_observed"] = string.Join(",", outcome.OpcodesObserved),
                    ["opcodes_missing"] = string.Join(",", outcome.OpcodesMissing),
                    ["dropped_for_capacity"] = outcome.DroppedForCapacity.ToString(CultureInfo.InvariantCulture),
                    ["dropped_for_age"] = outcome.DroppedForAge.ToString(CultureInfo.InvariantCulture),
                };

                var t14 = new DeferredEvidenceRecord(
                    "T-14",
                    DateTime.UtcNow,
                    outcome.Kind == PreLoginOutcomeKind.Complete,
                    t14List,
                    t14Measurements,
                    DataSourceKind.Live,
                    attachments,
                    outcome.Reason);
                report($"Evidenza T-14 scritta in: {DeferredEvidence.Write(t14)}");

                IReadOnlyList<DeferredCriterion> t05Criteria = DeferredEvidence.CriteriaFor("T-05");
                var t05List = new List<DeferredCriterion>(t05Criteria.Count);
                foreach (DeferredCriterion criterion in t05Criteria)
                {
                    bool satisfied = criterion.Id switch
                    {
                        "vitals_read_from_wire" => outcome.VitalsReadings > 0,
                        "vitals_provenance_live" => outcome.VitalsSatisfied,
                        _ => criterion.Satisfied,
                    };
                    t05List.Add(criterion with { Satisfied = satisfied });
                }

                var t05Measurements = new Dictionary<string, string>
                {
                    ["vitals_readings"] = outcome.VitalsReadings.ToString(CultureInfo.InvariantCulture),
                    ["vitals_provenance"] = outcome.VitalsProvenance.ToWire(),
                };

                var t05 = new DeferredEvidenceRecord(
                    "T-05",
                    DateTime.UtcNow,
                    outcome.VitalsSatisfied,
                    t05List,
                    t05Measurements,
                    outcome.VitalsProvenance,
                    attachments,
                    outcome.VitalsReason);
                report($"Evidenza T-05 scritta in: {DeferredEvidence.Write(t05)}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                report($"[avviso] evidenza non scritta: {ex.GetType().Name}");
            }

            return outcome.Kind == PreLoginOutcomeKind.Complete ? 0 : 1;
        }
        }

    /// <summary>
    /// Console entry point: prints to standard output and cancels on Ctrl+C.
    /// </summary>
    /// <param name="outPath">Where the <c>.noscap</c> file must be written; when null or blank, <see cref="DefaultPath"/> is used.</param>
    /// <param name="timeoutMinutes">Session budget in minutes; when not positive, <see cref="DefaultTimeoutMinutes"/> is used.</param>
    /// <returns>Zero when <see cref="PreLoginOutcomeKind.Complete"/> was reached, one otherwise.</returns>
    public static int Run(string? outPath, int timeoutMinutes)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            return Run(outPath, timeoutMinutes, Console.WriteLine, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }
}
