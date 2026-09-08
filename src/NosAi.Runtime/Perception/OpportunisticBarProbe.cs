using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Perception;

/// <summary>
/// A single accepted observation for the T-03 probe: the fill ratios independently read
/// from pixels and from the wire, their absolute difference, and the metadata of the
/// capture that produced them. The (FrameWidth, FrameHeight) pair is kept on the sample so
/// that evidence collected under different frame geometries is never mixed when closing.
/// </summary>
public readonly record struct BarProbeSample(
    double PixelRatio,
    double WireRatio,
    double Difference,
    double Confidence,
    int FrameWidth,
    int FrameHeight,
    DataSourceKind FrameSource,
    DateTime ObservedUtc);

/// <summary>
/// Lifecycle of the opportunistic bar probe. Waiting means it is still collecting the
/// "bar not full" evidence; the two closed states are the only outcomes it can produce.
/// </summary>
public enum BarProbeState : byte
{
    Waiting = 0,
    Confirmed = 1,
    Disagreed = 2,
}

/// <summary>
/// Self-directed probe that opportunistically watches the game while it runs and closes by
/// itself once it has seen a drained HP bar often enough. It compares the fill ratio read
/// from the pixels against the ratio reported by the wire, labels the glyph atlas with the
/// wire text while both sources are present together, and finally emits the deferred T-03
/// evidence. Closing is driven only by the ratio comparison: glyph-atlas training is a side
/// effect that never gates the probe outcome.
/// </summary>
public sealed class OpportunisticBarProbe
{
    /// <summary>Deferred-evidence identifier of this probe.</summary>
    public const string TestId = "T-03";

    /// <summary>Default fill ratio above which the HP bar is treated as full.</summary>
    public const double DefaultDrainedThreshold = 0.90;

    /// <summary>Default maximum accepted difference between the pixel and the wire ratio.</summary>
    public const double DefaultTolerance = 0.08;

    /// <summary>Default number of agreeing samples required to confirm the probe.</summary>
    public const int DefaultRequiredSamples = 3;

    private readonly string? _atlasPath;
    private readonly double _drainedThreshold;
    private readonly double _tolerance;
    private readonly int _requiredSamples;

    private readonly List<BarProbeSample> _samples = new();
    private readonly SortedDictionary<string, int> _rejections = new(StringComparer.Ordinal);

    private BarProbeState _state;
    private HudGlyphTrainingResult? _lastTraining;
    private int _atlasLearnedTotal;
    private string? _savedAtlasPath;
    private string? _failureReason;
    private bool _wireSeen;
    private bool _drainedSeen;

    /// <summary>
    /// Creates a probe. The thresholds are validated eagerly so that a misconfigured probe
    /// fails at construction time instead of producing nonsense evidence later.
    /// </summary>
    /// <param name="atlasPath">Optional path where the glyph atlas is persisted after learning.</param>
    /// <param name="drainedThreshold">Fill ratio above which the bar is considered full; must be in (0, 1].</param>
    /// <param name="tolerance">Maximum accepted |pixel - wire| difference; must be in (0, 1].</param>
    /// <param name="requiredSamples">Number of agreeing samples that confirm the probe; must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When <paramref name="drainedThreshold"/> or <paramref name="tolerance"/> is outside (0, 1],
    /// or when <paramref name="requiredSamples"/> is less than 1.
    /// </exception>
    public OpportunisticBarProbe(
        string? atlasPath = null,
        double drainedThreshold = DefaultDrainedThreshold,
        double tolerance = DefaultTolerance,
        int requiredSamples = DefaultRequiredSamples)
    {
        if (!(drainedThreshold > 0d && drainedThreshold <= 1d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(drainedThreshold), drainedThreshold, "drainedThreshold must be in (0, 1].");
        }

        if (!(tolerance > 0d && tolerance <= 1d))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tolerance), tolerance, "tolerance must be in (0, 1].");
        }

        if (requiredSamples < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredSamples), requiredSamples, "requiredSamples must be at least 1.");
        }

        _atlasPath = atlasPath;
        _drainedThreshold = drainedThreshold;
        _tolerance = tolerance;
        _requiredSamples = requiredSamples;
    }

    /// <summary>Fill ratio above which the HP bar is treated as full.</summary>
    public double DrainedThreshold => _drainedThreshold;

    /// <summary>Maximum accepted difference between the pixel and the wire ratio.</summary>
    public double Tolerance => _tolerance;

    /// <summary>Number of agreeing samples required to confirm the probe.</summary>
    public int RequiredSamples => _requiredSamples;

    /// <summary>Current lifecycle state of the probe.</summary>
    public BarProbeState State => _state;

    /// <summary>True once the probe reached a terminal state (Confirmed or Disagreed).</summary>
    public bool IsClosed => _state != BarProbeState.Waiting;

    /// <summary>Accepted samples, in collection order. Never cleared once collected.</summary>
    public IReadOnlyList<BarProbeSample> Samples => _samples;

    /// <summary>Rejection reasons observed so far, each mapped to how many times it occurred.</summary>
    public IReadOnlyDictionary<string, int> Rejections => _rejections;

    /// <summary>Outcome of the most recent atlas training attempt, if any.</summary>
    public HudGlyphTrainingResult? LastTraining => _lastTraining;

    /// <summary>Total number of glyphs learned across every successful training call.</summary>
    public int AtlasLearnedTotal => _atlasLearnedTotal;

    /// <summary>Path of the last successful atlas save, if any.</summary>
    public string? SavedAtlasPath => _savedAtlasPath;

    /// <summary>Non-null when the probe closed in disagreement, describing why.</summary>
    public string? FailureReason => _failureReason;

    /// <summary>
    /// Operator-facing explanation, in Italian, of why the probe is still waiting or of how
    /// it closed. All numbers are formatted with the invariant culture.
    /// </summary>
    public string PendingReason
    {
        get
        {
            switch (_state)
            {
                case BarProbeState.Confirmed:
                    return "chiusa: pixel e filo concordano su "
                        + FormatInt(CountAgreeingSamples())
                        + " campioni a barra non piena";
                case BarProbeState.Disagreed:
                    return "chiusa in disaccordo: "
                        + (_failureReason ?? "motivo sconosciuto")
                        + ", tolleranza "
                        + FormatDouble(_tolerance);
                default:
                    if (!_wireSeen)
                    {
                        return "in attesa della seconda fonte: nessuna lettura di vitals dal filo";
                    }

                    if (!_drainedSeen)
                    {
                        return "in attesa che la barra HP scenda sotto "
                            + FormatDouble(_drainedThreshold)
                            + ": finora sempre piena";
                    }

                    int agreeing = CountAgreeingSamples();
                    return "mancano "
                        + FormatInt(_requiredSamples - agreeing)
                        + " campioni validi a barra non piena (raccolti "
                        + FormatInt(agreeing)
                        + " su "
                        + FormatInt(_requiredSamples)
                        + ")";
            }
        }
    }

    /// <summary>
    /// Feeds one observation opportunity into the probe. Ordinary game conditions never make
    /// this method throw: unusable inputs are counted as rejections and the probe simply
    /// keeps waiting (or stays closed). Glyph-atlas training happens only here, and only when
    /// the frame and the wire vitals are both present, so every glyph label comes from the
    /// wire rather than from a guess. A bar whose wire ratio is still full is rejected before
    /// any pixel work, because only a genuinely drained bar is meaningful evidence.
    /// </summary>
    /// <param name="frame">Capture frame offered by the perception pipeline, if any.</param>
    /// <param name="wire">Latest vitals read from the wire, if any.</param>
    /// <param name="clientArea">Client area to segment; null means the whole frame.</param>
    /// <param name="atlas">Glyph atlas to train while both sources are available.</param>
    /// <param name="clientInForeground">False when the game client is known to be occluded.</param>
    /// <returns>The probe state after the offer (unchanged when the probe was already closed).</returns>
    public BarProbeState Offer(
        CaptureFrame? frame,
        WirePlayerVitals? wire,
        PixelRect? clientArea = null,
        HudGlyphAtlas? atlas = null,
        bool clientInForeground = true)
    {
        if (IsClosed)
        {
            return State;
        }

        if (!clientInForeground)
        {
            RecordRejection("client_not_foreground");
            return State;
        }

        if (frame is null || !frame.HasPixels)
        {
            RecordRejection("frame_without_pixels");
            return State;
        }

        if (wire is null)
        {
            // Second source still missing: this is the plain "waiting for the wire" case and
            // the glyph atlas must stay untouched until both sources are present together.
            RecordRejection("wire_unavailable");
            return State;
        }

        WirePlayerVitals vital = wire.Value;
        _wireSeen = true;

        double wireRatio;
        bool absoluteHpAvailable = false;
        int hp = 0;
        int maximum = 0;
        if (vital.Hp is int hpValue && vital.MaxHp is int maxValue && maxValue > 0 && hpValue >= 0 && hpValue <= maxValue)
        {
            hp = hpValue;
            maximum = maxValue;
            wireRatio = hpValue / (double)maxValue;
            absoluteHpAvailable = true;
        }
        else if (vital.HasPercent && vital.HpPercent is int percentValue)
        {
            wireRatio = percentValue / 100.0;
        }
        else
        {
            RecordRejection("wire_without_hp");
            return State;
        }

        // Training labels come from the wire text, so it requires the absolute HP values.
        // Its outcome intentionally never influences the probe closure.
        if (atlas is not null && absoluteHpAvailable)
        {
            _lastTraining = HudGlyphTraining.TrainHpFromFrame(atlas, frame, hp, maximum, clientArea);
            if (_lastTraining.Succeeded && _lastTraining.Learned > 0)
            {
                _atlasLearnedTotal += _lastTraining.Learned;

                string? atlasPath = _atlasPath;
                if (!string.IsNullOrEmpty(atlasPath))
                {
                    try
                    {
                        atlas.Save(atlasPath);
                        _savedAtlasPath = atlasPath;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Persistence is best effort: keep probing even when the atlas cannot
                        // be written to disk.
                        RecordRejection("atlas_save_failed:" + ex.GetType().Name);
                    }
                }
            }
        }

        if (wireRatio >= _drainedThreshold)
        {
            RecordRejection("bar_not_drained");
            return State;
        }

        _drainedSeen = true;

        PixelRect area = clientArea ?? new PixelRect(0, 0, frame.Width, frame.Height);
        if (area.Width <= 0 || area.Height <= 0)
        {
            RecordRejection("client_area_without_extent");
            return State;
        }

        RegionOfInterest? hpRoi = null;
        try
        {
            foreach (RegionOfInterest region in RoiSegmenter.Segment(frame.Width, frame.Height, area))
            {
                if (region.Kind == RoiKind.PlayerHpBar)
                {
                    hpRoi = region;
                    break;
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            RecordRejection("hp_roi_unsegmentable");
            return State;
        }

        if (hpRoi is null)
        {
            RecordRejection("hp_roi_missing");
            return State;
        }

        byte[] crop = ScreenVitalReader.Crop(frame, hpRoi.Rect);
        if (crop.Length == 0)
        {
            RecordRejection("hp_roi_outside_frame");
            return State;
        }

        HudBarMeasure measure = HudBarFillReader.Measure(
            crop, hpRoi.Rect.Width, hpRoi.Rect.Height, HudFillHue.RedOrGreen);
        if (measure.FailureReason is { } failureReason)
        {
            // A failed bar reading means "no usable pixels": never fabricate a zero ratio,
            // because that would look like a perfectly drained bar and poison the evidence.
            RecordRejection(failureReason);
            return State;
        }

        if (measure.Ratio is not double pixelRatio)
        {
            RecordRejection("measure_without_ratio");
            return State;
        }

        var sample = new BarProbeSample(
            pixelRatio,
            wireRatio,
            Math.Abs(pixelRatio - wireRatio),
            measure.Confidence,
            frame.Width,
            frame.Height,
            frame.Source,
            frame.CapturedUtc);
        _samples.Add(sample);

        if (sample.Difference > _tolerance)
        {
            _state = BarProbeState.Disagreed;
            _failureReason = "pixel_wire_disagree:" + FormatDouble4(sample.Difference);
            return State;
        }

        // Only samples taken under the very same frame geometry may accumulate toward
        // confirmation, so a resolution change can never create a fake majority.
        int agreeingWithSameGeometry = _samples.Count(sampleToCount =>
            sampleToCount.Difference <= _tolerance
            && sampleToCount.FrameWidth == sample.FrameWidth
            && sampleToCount.FrameHeight == sample.FrameHeight);
        if (agreeingWithSameGeometry >= _requiredSamples)
        {
            _state = BarProbeState.Confirmed;
        }

        return State;
    }

    /// <summary>
    /// Materializes the deferred T-03 evidence from the current probe state. Completion is
    /// intentionally stricter than the first criterion: Complete is true only when the probe
    /// closed as Confirmed, while the individual criteria can be only partially satisfied.
    /// </summary>
    /// <param name="closedUtc">Timestamp stamped onto the evidence record.</param>
    public DeferredEvidenceRecord BuildEvidence(DateTime closedUtc)
    {
        bool complete = _state == BarProbeState.Confirmed;
        double? maxDifference = _samples.Count > 0 ? _samples.Max(s => s.Difference) : null;

        var criteria = new List<DeferredCriterion>
        {
            new DeferredCriterion(
                "bar_observed_drained",
                "Osservati almeno tre campioni con la barra HP sotto la soglia di pienezza.",
                _samples.Count >= _requiredSamples,
                FormatInt(_samples.Count) + " campioni a barra non piena, soglia " + FormatDouble(_drainedThreshold)),
            new DeferredCriterion(
                "pixel_matches_wire",
                "Il rapporto letto dai pixel concorda col filo entro la tolleranza.",
                _state == BarProbeState.Confirmed,
                maxDifference is double observed
                    ? "differenza massima " + FormatDouble4(observed) + ", tolleranza " + FormatDouble(_tolerance)
                    : "nessun campione"),
            new DeferredCriterion(
                "glyph_atlas_trained",
                "L'atlante dei glifi è stato addestrato con l'etichetta presa dal filo.",
                _atlasLearnedTotal > 0,
                DescribeAtlasTraining()),
        };

        var measurements = new Dictionary<string, string>
        {
            ["state"] = _state.ToString(),
            ["threshold"] = FormatDouble(_drainedThreshold),
            ["tolerance"] = FormatDouble(_tolerance),
            ["required_samples"] = FormatInt(_requiredSamples),
            ["valid_samples"] = FormatInt(_samples.Count),
            ["max_difference"] = maxDifference is double observedMax ? FormatDouble4(observedMax) : "—",
            ["atlas_learned"] = FormatInt(_atlasLearnedTotal),
            ["atlas_result"] = FormatAtlasResult(),
        };

        for (int i = 0; i < _samples.Count; i++)
        {
            BarProbeSample sample = _samples[i];
            measurements["sample_" + FormatInt(i + 1)] =
                "pixel=" + FormatDouble4(sample.PixelRatio)
                + " wire=" + FormatDouble4(sample.WireRatio)
                + " diff=" + FormatDouble4(sample.Difference)
                + " conf=" + FormatDouble4(sample.Confidence)
                + " frame=" + FormatInt(sample.FrameWidth) + "x" + FormatInt(sample.FrameHeight)
                + " source=" + sample.FrameSource
                + " at=" + sample.ObservedUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        foreach (KeyValuePair<string, int> rejection in _rejections)
        {
            measurements["rejected_" + rejection.Key] = FormatInt(rejection.Value);
        }

        DataSourceKind provenance = _samples.Count > 0 ? _samples[0].FrameSource : DataSourceKind.Unknown;

        IReadOnlyList<string> attachments = _savedAtlasPath is { } savedPath
            ? new[] { savedPath }
            : Array.Empty<string>();

        return new DeferredEvidenceRecord(
            TestId,
            closedUtc,
            complete,
            criteria,
            measurements,
            provenance,
            attachments,
            complete ? null : PendingReason);
    }

    private int CountAgreeingSamples() => _samples.Count(sample => sample.Difference <= _tolerance);

    private void RecordRejection(string reason)
    {
        _rejections.TryGetValue(reason, out int count);
        _rejections[reason] = count + 1;
    }

    private string DescribeAtlasTraining()
    {
        if (_lastTraining is null)
        {
            return "nessun addestramento tentato";
        }

        if (_atlasLearnedTotal > 0)
        {
            return FormatInt(_atlasLearnedTotal) + " glifi appresi";
        }

        return _lastTraining.FailureReason ?? "0 glifi appresi";
    }

    private string FormatAtlasResult()
    {
        if (_lastTraining is null)
        {
            return "—";
        }

        HudGlyphTrainingResult training = _lastTraining;
        return "learned=" + FormatInt(training.Learned)
            + " known=" + FormatInt(training.AlreadyKnown)
            + " failure=" + (training.FailureReason ?? "none");
    }

    private static string FormatDouble(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDouble4(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Command behind --watch-evidence: keeps the two self-directed probes alive while a client
/// runs and writes whatever deferred evidence they produced. T-03 waits until a drained HP
/// bar agrees with the wire, T-09 waits until the target frame is observed often enough to
/// lock its ROI; neither asks the operator to prepare a condition. This class is partial
/// because the foreground-window import is implemented by the LibraryImport source generator.
/// </summary>
public static partial class EvidenceWatch
{
    /// <summary>Command-line flag that selects this command in the entry point.</summary>
    public const string Flag = "--watch-evidence";

    /// <summary>Default watch duration in minutes when --minutes is not given.</summary>
    public const int DefaultMinutes = 5;

    /// <summary>
    /// Runs the evidence watch and never lets an exception escape: every failure is reported
    /// on the error stream and turned into a nonzero exit code.
    /// </summary>
    /// <param name="args">Command-line arguments; the flag itself may be included.</param>
    /// <returns>0 when both probes closed with Complete evidence, 1 on any impediment or when
    /// a probe did not complete, 2 when --minutes is not an integer between 1 and 600.</returns>
    public static int Run(string[] args)
    {
        try
        {
            return RunCore(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ERRORE] " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    private static int RunCore(string[] args)
    {
        int minutes = DefaultMinutes;
        string? capturePath = null;
        string processName = "NostaleClientX";

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--minutes", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMinutes)
                    || parsedMinutes < 1
                    || parsedMinutes > 600)
                {
                    Console.Error.WriteLine("--minutes vuole un intero fra 1 e 600.");
                    return 2;
                }

                minutes = parsedMinutes;
                i++;
                continue;
            }

            if (string.Equals(args[i], "--capture", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    capturePath = args[i + 1];
                    i++;
                }

                continue;
            }

            if (string.Equals(args[i], "--process", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    processName = args[i + 1];
                    i++;
                }
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("La cattura schermo richiede Windows.");
            return 1;
        }

        ClientWindow? window = HudProbe.FindClientWindow(processName);
        if (window is null)
        {
            Console.Error.WriteLine(
                "Finestra del client non trovata per il processo '" + processName
                + "': senza l'area client le frazioni sarebbero dei pixel sbagliati.");
            return 1;
        }

        Console.WriteLine(
            "Finestra del client: handle " + FormatLong(window.Handle.ToInt64())
            + ", classe " + window.ClassName
            + ", area client " + FormatInt(window.ClientArea.Width)
            + "x" + FormatInt(window.ClientArea.Height) + ".");

        if (!DxgiDesktopDuplicationSource.TryCreate(out DxgiDesktopDuplicationSource? source, out CaptureUnavailable? unavailable))
        {
            Console.Error.WriteLine(
                "[UNKNOWN] cattura schermo non disponibile: " + (unavailable?.Reason ?? "dxgi_unavailable"));
            return 1;
        }

        DxgiDesktopDuplicationSource liveSource = source!;
        Console.WriteLine(
            "Duplicazione dello schermo: " + FormatInt(liveSource.Width)
            + "x" + FormatInt(liveSource.Height) + ".");

        using (liveSource)
        {
            return Watch(liveSource, window, minutes, capturePath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int Watch(
        DxgiDesktopDuplicationSource capture,
        ClientWindow window,
        int minutes,
        string? capturePath)
    {
        ClientMemorySession? session;
        string? attachFailure;
        if (!ClientMemorySession.TryAttach(out session, out attachFailure))
        {
            Console.WriteLine(
                "Le etichette del bersaglio resteranno UNKNOWN (motivo: "
                + (attachFailure ?? "attach_failed")
                + "): T-09 non potrà chiudere ma il comando prosegue.");
            return WatchCore(null, null, capture, window, minutes, capturePath);
        }

        ClientMemorySession liveSession = session!;
        long? playerId = null;
        using (liveSession)
        {
            if (liveSession.TryReadPlayer(out PlayerObjectReading player, out _))
            {
                playerId = player.CharacterId;
                Console.WriteLine(
                    "Sessione memoria agganciata: id giocatore " + FormatLong(playerId.Value) + ".");
            }

            return WatchCore(liveSession, playerId, capture, window, minutes, capturePath);
        }
    }

    [SupportedOSPlatform("windows")]
    private static int WatchCore(
        ClientMemorySession? session,
        long? playerId,
        DxgiDesktopDuplicationSource capture,
        ClientWindow window,
        int minutes,
        string? capturePath)
    {
        string atlasPath = Path.Combine(Directory.GetCurrentDirectory(), HudGlyphAtlas.RelativePath);
        HudGlyphAtlas atlas = HudGlyphAtlas.Load(atlasPath, out _);
        string calibrationPath = Path.Combine(Directory.GetCurrentDirectory(), TargetRoiCalibration.RelativePath);

        var barProbe = new OpportunisticBarProbe(atlasPath);
        var calibrator = new TargetRoiAutoCalibrator(calibrationPath);

        DateTime deadline = DateTime.UtcNow.AddMinutes(minutes);
        DateTime lastStatusUtc = DateTime.MinValue;
        DateTime lastWireReadUtc = DateTime.MinValue;
        WirePlayerVitals? wire = null;

        while (DateTime.UtcNow < deadline && (!barProbe.IsClosed || !calibrator.IsClosed))
        {
            if (!capture.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
            {
                Thread.Sleep(50);
                continue;
            }

            bool foreground = GetForegroundWindow() == window.Handle;

            // At most one wire re-read per second; without --capture the wire stays null.
            if (capturePath is not null && File.Exists(capturePath))
            {
                DateTime nowUtc = DateTime.UtcNow;
                if ((nowUtc - lastWireReadUtc) >= TimeSpan.FromSeconds(1))
                {
                    lastWireReadUtc = nowUtc;
                    try
                    {
                        wire = WirePlayerVitalsParser.FromCapture(capturePath, playerId);
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        wire = null;
                    }
                }
            }

            bool? hasTarget = null;
            if (session is not null && session.TryReadTarget(out TargetPointerReading target, out _))
            {
                hasTarget = target.HasTarget;
            }

            if (!barProbe.IsClosed)
            {
                barProbe.Offer(frame, wire, window.ClientArea, atlas, foreground);
            }

            if (!calibrator.IsClosed)
            {
                calibrator.Offer(frame, hasTarget, window.ClientArea, foreground);
            }

            // State report at most once every five seconds.
            if ((DateTime.UtcNow - lastStatusUtc) >= TimeSpan.FromSeconds(5))
            {
                lastStatusUtc = DateTime.UtcNow;
                Console.WriteLine("  T-03 " + barProbe.State + ": " + barProbe.PendingReason);
                Console.WriteLine("  T-09 " + calibrator.State + ": " + calibrator.PendingReason);
            }

            Thread.Sleep(100);
        }

        DateTime closed = DateTime.UtcNow;
        DeferredEvidenceRecord barRecord = barProbe.BuildEvidence(closed);
        DeferredEvidenceRecord targetRecord = calibrator.BuildEvidence(closed);

        WriteEvidence(barRecord, "T-03");
        WriteEvidence(targetRecord, "T-09");

        Console.WriteLine(
            "Riepilogo T-03: stato " + barProbe.State + ", Complete=" + barRecord.Complete
            + ", " + barProbe.PendingReason + ".");
        Console.WriteLine(
            "Riepilogo T-09: stato " + calibrator.State + ", Complete=" + targetRecord.Complete
            + ", " + calibrator.PendingReason + ".");

        return barRecord.Complete && targetRecord.Complete ? 0 : 1;
    }

    private static void WriteEvidence(DeferredEvidenceRecord record, string label)
    {
        try
        {
            string path = DeferredEvidence.Write(record);
            Console.WriteLine("Evidenza " + label + " scritta: " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("Evidenza " + label + " non scritta: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string FormatInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatLong(long value) => value.ToString(CultureInfo.InvariantCulture);
}
