using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// AP-02/A4: the real capture sequence a <see cref="VisualObservation"/> needs
/// for a live run, wired entirely from pre-existing Gate-era production
/// infrastructure -- <see cref="ClientWindowLocator"/>,
/// <see cref="DxgiDesktopDuplicationSource"/>, <see cref="PerceptionPipeline"/>
/// and <see cref="ScreenVitalReader"/> -- with no new capture technology
/// invented here (docs/ROADMAP_ESECUTIVA.md S:AP-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="NullObjectDetector"/>.</b> No trained ONNX model exists in
/// this repository -- <see cref="OnnxObjectDetector"/> would fail every call
/// with <c>"onnx_model_not_found"</c> -- so the detector fed to the internal
/// <see cref="PerceptionPipeline"/> is <see cref="NullObjectDetector"/>, which
/// never fabricates a detection. That is the honest choice for this pass, not
/// a placeholder: the day a trained decoder ships, only that one line changes.
/// </para>
/// <para>
/// <b>One frame, three readers.</b> <see cref="PerceptionPipeline"/> owns
/// acquisition (freshness policy, ROI segmentation, detect/track) but does not
/// expose the raw frame it consumed, and <see cref="ScreenVitalReader"/> needs
/// that same frame to measure HUD bars, as does the target-frame read wired
/// into <see cref="Capture"/> through <see cref="ScreenTargetFrameSource"/>
/// (AP-02/A2+A4). Re-acquiring another frame from DXGI for either reader would
/// cost a second real capture per cycle and could legitimately read a different
/// instant of the screen than the entities the pipeline just produced.
/// <see cref="RecordingFrameSource"/> instead wraps the
/// single DXGI source and remembers the last frame it actually handed out, so
/// all readers see exactly the same pixels from exactly one acquisition.
/// </para>
/// <para>
/// <b>Fail-closed at every step.</b> No process id, no located client window,
/// no platform support for either, an unavailable DXGI backend, and no
/// acquired frame each return <see cref="VisualObservation.Unobserved(string, DateTime?)"/>
/// with a specific, diagnosable reason -- never an unhandled exception. This is
/// deliberate belt-and-suspenders: <see cref="WorldModel.Fusion.WorldModelFusionLoop"/>
/// already treats an exception from its optional vision source as a missed
/// cycle rather than a fault, but this class does not lean on that safety net
/// for its own ordinary failure modes.
/// </para>
/// <para>
/// <b>DXGI lifecycle: open once, reuse, retry only on failure.</b>
/// <see cref="DxgiDesktopDuplicationSource"/> is comparatively expensive to
/// (re)acquire -- a full factory/adapter/output/device/staging-texture chain --
/// so once <see cref="DxgiDesktopDuplicationSource.TryCreate"/> succeeds this instance keeps that source for
/// the rest of its own lifetime instead of reopening it every cycle. If
/// creation has not yet succeeded (never attempted, or a previous attempt
/// failed), the next <see cref="Capture"/> call tries again -- an operator
/// starting this runtime before the desktop session is ready, or a transient
/// access-denied on a just-locked desktop, should not need a restart to
/// recover. What this class deliberately does NOT do is detect and rebuild on
/// <c>DXGI_ERROR_ACCESS_LOST</c> mid-session (a desktop lock/unlock or GPU
/// reset after a successful open): <see cref="DxgiDesktopDuplicationSource.TryAcquire"/>
/// only reports a frame or no-frame, not why, so this class cannot distinguish
/// that case from an ordinary idle-screen timeout without changing
/// <c>DxgiCapture.cs</c> (out of scope for this pass, read-only). A capture
/// left silently failing after such an event is a declared limitation, not a
/// hidden one.
/// </para>
/// </remarks>
public sealed class ScreenVitalsCapture : IDisposable
{
    /// <summary>The client process id has no reading at all this cycle.</summary>
    public const string NoProcessIdReason = "client_process_id_unavailable";

    /// <summary>
    /// Neither <see cref="ClientWindowLocator"/> nor <see cref="DxgiDesktopDuplicationSource"/>
    /// can be asked anything real off Windows; asking anyway would either throw
    /// (an unguarded P/Invoke) or -- for DXGI, which self-guards -- simply repeat
    /// the same answer <see cref="DxgiDesktopDuplicationSource.TryCreate"/> gives.
    /// </summary>
    public const string RequiresWindowsReason = "screen_vitals_capture_requires_windows";

    private readonly Func<int?> _processId;
    private readonly ScreenVitalReader _reader;
    private readonly TargetRoiCalibration _targetCalibration;
    private readonly IPlayerAttackObserver? _wire;
    private readonly uint _adapterIndex;
    private readonly uint _outputIndex;
    private readonly uint _acquireTimeoutMs;
    private readonly Func<DateTime> _clock;

    private DxgiDesktopDuplicationSource? _dxgi;
    private RecordingFrameSource? _recordingSource;
    private PerceptionPipeline? _pipeline;
    private bool _disposed;

    // Continuity state handed back into ScreenVitalReader.Read on the next
    // call, exactly as a caller running the reader across real frames would --
    // it is what lets ScreenDerivedBarGate/ScreenDerivedVitalGate reject an
    // implausible single-frame jump instead of publishing it as DERIVED.
    private ScreenBarFill? _previousHpBar;
    private ScreenBarFill? _previousMpBar;
    private ScreenVitalPair? _previousHp;
    private ScreenVitalPair? _previousMp;

    /// <param name="processId">
    /// Reads the client's process id on demand, or null when no client is
    /// attached. In production this is a small wrapper over
    /// <c>Gate1BootstrapHost.Capture().Client.ProcessId</c>; tests supply a stub.
    /// </param>
    /// <param name="reader">
    /// The vitals reader to drive. Defaults to a fresh <see cref="ScreenVitalReader"/>
    /// with no OCR atlas (<c>ocr: null</c>) -- no trained glyph atlas exists in
    /// this repository, so numeric HP/MP stay honestly UNKNOWN; bar-fill still
    /// works without one.
    /// </param>
    /// <param name="targetCalibration">
    /// Where the target frame sits on this operator's client (ADR-0018). Defaults to
    /// <see cref="TargetRoiCalibration.Uncalibrated"/>, which
    /// <see cref="TargetStateComposer.Compose"/> already reports honestly as
    /// <c>target_roi_not_calibrated</c> rather than a confident wrong answer -- see
    /// <c>HudProbe</c> for how an operator calibrates one.
    /// </param>
    /// <param name="wire">
    /// The wire's side of ADR-0018, used only to contradict a screen reading that
    /// says no target. Null (the default) means no contradiction check is available
    /// at this composition site today -- the screen stands alone, the same accepted
    /// shape <c>TargetAwareGameplayProvider</c> already uses for the same reason.
    /// </param>
    /// <param name="adapterIndex">Forwarded to <see cref="DxgiDesktopDuplicationSource.TryCreate"/>.</param>
    /// <param name="outputIndex">Forwarded to <see cref="DxgiDesktopDuplicationSource.TryCreate"/>.</param>
    /// <param name="acquireTimeoutMs">Forwarded to <see cref="DxgiDesktopDuplicationSource.TryCreate"/>.</param>
    /// <param name="clock">Time source for produced observations. Defaults to <see cref="DateTime.UtcNow"/>.</param>
    public ScreenVitalsCapture(
        Func<int?> processId,
        ScreenVitalReader? reader = null,
        TargetRoiCalibration? targetCalibration = null,
        IPlayerAttackObserver? wire = null,
        uint adapterIndex = 0,
        uint outputIndex = 0,
        uint acquireTimeoutMs = 250,
        Func<DateTime>? clock = null)
    {
        _processId = processId ?? throw new ArgumentNullException(nameof(processId));
        _reader = reader ?? new ScreenVitalReader();
        _targetCalibration = targetCalibration ?? TargetRoiCalibration.Uncalibrated;
        _wire = wire;
        _adapterIndex = adapterIndex;
        _outputIndex = outputIndex;
        _acquireTimeoutMs = acquireTimeoutMs;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Runs the real capture sequence once: process id -> client window ->
    /// DXGI frame -> vision pipeline + vitals reader -> <see cref="VisualObservation"/>.
    /// Never throws for an ordinary unavailability; every failure mode returns
    /// <see cref="VisualObservation.Unobserved(string, DateTime?)"/> with a
    /// specific reason instead.
    /// </summary>
    public VisualObservation Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DateTime now = _clock();

        if (_processId() is not int pid)
            return VisualObservation.Unobserved(NoProcessIdReason, now);

        // ClientWindowLocator.TryFind has no runtime guard of its own (only the
        // [SupportedOSPlatform("windows")] annotation) and calls into user32.dll
        // unconditionally -- on a non-Windows host that is an unhandled
        // DllNotFoundException, not an ordinary failure. This check is what
        // keeps that branch fail-closed instead of throwing, the same guard
        // RealClientConnector/Gate1BootstrapHost already apply before every
        // ClientWindowLocator call.
        if (!OperatingSystem.IsWindows())
            return VisualObservation.Unobserved(RequiresWindowsReason, now);

        ClientWindow? window = ClientWindowLocator.TryFind(pid, out string? locateFailure);
        if (window is null)
            return VisualObservation.Unobserved(locateFailure ?? "client_window_not_found", now);

        if (!TryEnsurePipeline(out string? dxgiFailure))
            return VisualObservation.Unobserved(dxgiFailure ?? "dxgi_unavailable", now);

        PerceptionResult result = _pipeline!.ProcessNext();
        if (!result.FrameAcquired || _recordingSource!.LastFrame is not { } frame)
            return VisualObservation.Unobserved(result.UnavailableReason ?? "no_frame_acquired", now);

        ScreenVitalObservation vitals = _reader.Read(
            frame,
            _previousHpBar,
            _previousMpBar,
            _previousHp,
            _previousMp,
            window.ClientArea);

        _previousHpBar = vitals.HpBar;
        _previousMpBar = vitals.MpBar;
        _previousHp = vitals.Hp;
        _previousMp = vitals.Mp;

        // ScreenTargetFrameSource reads the same already-acquired `frame` (via
        // SingleFrameSource, never a second real DXGI acquisition -- see the
        // class remarks, "One frame, two readers") at the operator-calibrated
        // ROI, and TargetStateComposer composes the result against the wire
        // side, when one is available. An uncalibrated TargetRoiCalibration
        // (the default) still produces an honest
        // Unknown(NotCalibratedReason) here -- this wiring does not require
        // calibration to exist, only makes real calibration take effect once
        // it does.
        var targetFrames = new ScreenTargetFrameSource(
            new SingleFrameSource(frame, _recordingSource!.Source),
            _targetCalibration,
            () => window.ClientArea);
        TargetFrameObservation screenTarget = targetFrames.Read();
        ClassifiedValue<bool> hasTarget = TargetStateComposer.Compose(
            _targetCalibration, screenTarget, _wire?.LastPlayerAttackAtUtc);

        return new VisualObservation(result, vitals, hasTarget, frame.CapturedUtc);
    }

    /// <summary>
    /// Ensures the DXGI source (and the pipeline wrapped around it) exist,
    /// creating them on first success and reusing them afterwards. See the
    /// class remarks for the lifecycle rationale.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so tests can exercise the DXGI-creation
    /// failure path directly (<c>InternalsVisibleTo NosAi.Runtime.Tests</c>,
    /// <c>NosAi.Runtime.csproj</c>) without first needing a real client window
    /// -- on this sandbox's Linux host <see cref="Capture"/> itself never
    /// reaches this call (it fails closed one step earlier, at the
    /// Windows-only window lookup), so this is what lets the honest
    /// <c>"dxgi_requires_windows"</c> answer from
    /// <see cref="DxgiDesktopDuplicationSource.TryCreate"/> be demonstrated
    /// without a real Windows target.
    /// </remarks>
    internal bool TryEnsurePipeline(out string? failureReason)
    {
        failureReason = null;
        if (_pipeline is not null) return true;

        if (!DxgiDesktopDuplicationSource.TryCreate(
                out DxgiDesktopDuplicationSource? source,
                out CaptureUnavailable? unavailable,
                _adapterIndex,
                _outputIndex,
                _acquireTimeoutMs,
                _clock))
        {
            failureReason = unavailable?.Reason ?? "dxgi_unavailable";
            return false;
        }

        _dxgi = source;
        _recordingSource = new RecordingFrameSource(source!);
        _pipeline = new PerceptionPipeline(_recordingSource, new NullObjectDetector(), clock: _clock);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dxgi?.Dispose();
    }

    /// <summary>
    /// Wraps a frame source and remembers the last frame it actually produced,
    /// so <see cref="PerceptionPipeline"/> and <see cref="ScreenVitalReader"/>
    /// can share exactly one real acquisition per cycle instead of each
    /// capturing its own (see class remarks, "One frame, two readers").
    /// </summary>
    private sealed class RecordingFrameSource : IFrameSource
    {
        private readonly IFrameSource _inner;

        public RecordingFrameSource(IFrameSource inner) => _inner = inner;

        public CaptureFrame? LastFrame { get; private set; }

        public DataSourceKind Source => _inner.Source;

        public bool TryAcquire(out CaptureFrame frame)
        {
            bool acquired = _inner.TryAcquire(out frame);
            if (acquired) LastFrame = frame;
            return acquired;
        }
    }

    /// <summary>
    /// Wraps one already-acquired frame as an <see cref="IFrameSource"/> that
    /// returns exactly that frame on every <see cref="TryAcquire"/> call,
    /// never a fresh real capture. Exists so <see cref="ScreenTargetFrameSource"/>
    /// reads the exact same pixels <see cref="PerceptionPipeline"/> and
    /// <see cref="ScreenVitalReader"/> already consumed this cycle -- see the
    /// class remarks, "One frame, two readers" (now three): a second real
    /// DXGI acquisition per cycle would cost double the capture and could
    /// legitimately observe a different instant of the screen than the
    /// entities/vitals this cycle already produced.
    /// </summary>
    private sealed class SingleFrameSource : IFrameSource
    {
        private readonly CaptureFrame _frame;

        public SingleFrameSource(CaptureFrame frame, DataSourceKind source)
        {
            _frame = frame;
            Source = source;
        }

        public DataSourceKind Source { get; }

        public bool TryAcquire(out CaptureFrame frame)
        {
            frame = _frame;
            return true;
        }
    }
}
