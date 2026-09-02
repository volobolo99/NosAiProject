using System.Globalization;
using System.IO;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel;

/// <summary>Where the target-id hunt has got to, as the control panel draws it.</summary>
internal enum TargetHuntKind : byte
{
    /// <summary>
    /// No candidate file. Distinct from a file that recorded zero survivors:
    /// the hunt has not been run, so there is no empty set to report.
    /// </summary>
    NotStarted = 0,

    /// <summary>The file exists but could not be read. No counts are invented.</summary>
    Unreadable = 1,

    /// <summary>The file exists and names no surviving candidate.</summary>
    ZeroCandidates = 2,

    /// <summary>The file exists and still has candidates; the proof is not complete.</summary>
    InProgress = 3,

    /// <summary>The file names one durable candidate that has met the proof rule.</summary>
    Proven = 4
}

/// <summary>
/// The target-frame ROI file, drawn as the second independent source.
/// After ADR-0021 this is never a combat precondition, so an absent
/// calibration is not an error.
/// </summary>
internal enum TargetRoiKind : byte
{
    /// <summary>No calibration file. Not an error and not a combat gate.</summary>
    NotCalibrated = 0,

    /// <summary>A confirmed calibration, with when and against which resolution.</summary>
    Calibrated = 1,

    /// <summary>A file was present but could not be believed. Still not a combat error.</summary>
    Unreadable = 2
}

/// <summary>Operator-facing target-hunt view: oracle progress, next step, and the screen ROI.</summary>
internal sealed class TargetHuntView
{
    public TargetHuntKind HuntKind { get; init; }
    public string HuntStatusLine { get; init; } = "";
    public string ClearedPassLine { get; init; } = "";
    public bool ClearedPassMissing { get; init; }
    public string AdviceLine { get; init; } = "";
    public TargetRoiKind RoiKind { get; init; }
    public string RoiLine { get; init; } = "";

    /// <summary>
    /// Always false for an uncalibrated ROI: after ADR-0021 the screen
    /// rectangle is not a combat failure.
    /// </summary>
    public bool RoiIsError { get; init; }

    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
}

/// <summary>
/// Read-only target-hunt view for the control panel.
/// </summary>
/// <remarks>
/// <para>
/// The candidate file is the same artefact <see cref="TargetIdFinder.Format"/>
/// writes. The next-step sentence is <see cref="TargetIdFinder.Advice"/> —
/// the panel does not restate that order. The ROI file is
/// <see cref="TargetRoiCalibration.Load"/>, drawn as the second independent
/// source rather than as a missing precondition of combat (ADR-0021).
/// </para>
/// <para>
/// Nothing here writes those files, arms input, or invents a count when a
/// file is absent. A missing candidate file is <see cref="TargetHuntKind.NotStarted"/>,
/// which is a different drawing from zero surviving candidates.
/// </para>
/// </remarks>
internal static class TargetInspect
{
    /// <summary>Operator-facing mark when <see cref="TargetIdFinder.CandidatePath"/> does not exist.</summary>
    public const string HuntNotStartedLabel = "caccia non iniziata";

    /// <summary>
    /// Operator-facing mark while the no-target pass has never run against this set.
    /// That pass is what tells the selection apart from the client's entity list.
    /// </summary>
    public const string ClearedMissingLabel = "mancante";

    /// <summary>Operator-facing mark after a no-target pass has been recorded.</summary>
    public const string ClearedDoneLabel = "fatta";

    /// <summary>Operator-facing mark when the target-frame ROI has never been confirmed.</summary>
    public const string RoiNotCalibratedLabel = "NON CALIBRATO";

    /// <summary>
    /// Why the ROI row is on this view at all, after ADR-0021 made it optional.
    /// Shown for both the calibrated and the uncalibrated drawing so neither
    /// looks like a combat error.
    /// </summary>
    public const string IndependentSourceNote =
        "seconda sorgente indipendente, non è una precondizione del combattimento (ADR-0021)";

    /// <summary>Why the no-target pass is the line that matters.</summary>
    public const string ClearedPassWhy =
        "chiedere al valore di TORNARE allo stesso « nessuno »; è l'unica prova che un contatore non sa superare";

    public const string HuntUnreadableReason = "target_candidates_unreadable";
    public const string CandidatesLabel = "Candidati sopravvissuti";
    public const string AnchoredLabel = "Ancorati a una base";
    public const string SelectionsLabel = "Selezioni diverse";
    public const string RestartsLabel = "Riavvii superati";
    public const string ClearedLabel = "Passata senza bersaglio";
    public const string AdviceLabel = "Prossimo passo";
    public const string RoiLabel = "Riquadro bersaglio";

    /// <summary>
    /// Builds the view from the two files the hunt and the screen reader leave
    /// on disk. Either path may be absent; absence is a named state, not a zero.
    /// </summary>
    public static TargetHuntView Inspect(string candidatePath, string roiPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(roiPath);

        CandidateFile hunt = ReadCandidateFile(candidatePath);
        RoiReading roi = ReadRoi(roiPath);
        return Compose(hunt, roi);
    }

    /// <summary>
    /// Cheap identity of the two files, so the window can skip a redraw when
    /// neither has changed. Absence is part of the identity, not a default size.
    /// </summary>
    public static string Signature(string candidatePath, string roiPath)
        => FileSignature(candidatePath) + "|" + FileSignature(roiPath);

    internal static TargetHuntView Compose(CandidateFile hunt, RoiReading roi)
    {
        HuntDrawing huntDrawing = DrawHunt(hunt);
        RoiDrawing roiDrawing = DrawRoi(roi);
        return new TargetHuntView
        {
            HuntKind = huntDrawing.Kind,
            HuntStatusLine = huntDrawing.Status,
            ClearedPassLine = huntDrawing.ClearedLine,
            ClearedPassMissing = huntDrawing.ClearedMissing,
            AdviceLine = huntDrawing.Advice,
            RoiKind = roiDrawing.Kind,
            RoiLine = roiDrawing.Line,
            RoiIsError = false,
            Fields =
            [
                huntDrawing.CandidatesField,
                huntDrawing.AnchoredField,
                huntDrawing.SelectionsField,
                huntDrawing.RestartsField,
                huntDrawing.ClearedField,
                huntDrawing.AdviceField,
                roiDrawing.Field
            ]
        };
    }

    private static HuntDrawing DrawHunt(CandidateFile hunt)
    {
        if (!hunt.Exists)
        {
            return HuntUnknown(
                TargetHuntKind.NotStarted,
                HuntNotStartedLabel,
                HuntNotStartedLabel);
        }

        if (hunt.Candidates is not { } candidates)
        {
            string reason = hunt.ReadFailure ?? HuntUnreadableReason;
            return HuntUnknown(TargetHuntKind.Unreadable, $"UNKNOWN · {reason}", reason);
        }

        int count = candidates.Hits.Count;
        int durable = CountDurable(candidates.Hits);
        int passes = candidates.Selections;
        int restarts = candidates.Restarts;
        bool sawCleared = candidates.SawCleared;
        string advice = TargetIdFinder.Advice(count, durable, passes, restarts, sawCleared);
        bool proven = TargetIdFinder.Proven(candidates.Hits, passes, restarts, sawCleared);
        TargetHuntKind kind = count == 0
            ? TargetHuntKind.ZeroCandidates
            : proven ? TargetHuntKind.Proven : TargetHuntKind.InProgress;

        bool clearedMissing = !sawCleared;
        string clearedValue = sawCleared ? ClearedDoneLabel : ClearedMissingLabel;
        string clearedLine = sawCleared
            ? $"{ClearedLabel}: {ClearedDoneLabel}"
            : $"{ClearedLabel}: {ClearedMissingLabel} — {ClearedPassWhy}";

        string status = string.Create(CultureInfo.InvariantCulture,
            $"{count} candidati ({durable} ancorati) — selezioni seguite {passes}/{TargetIdFinder.RequiredSelections}, riavvii {restarts}/1");

        string cached = DataSourceKind.Cached.ToWire();
        return new HuntDrawing(
            kind,
            status,
            clearedLine,
            clearedMissing,
            advice,
            new DisplayField(CandidatesLabel, $"{count} [{cached}]", cached),
            new DisplayField(AnchoredLabel, $"{durable} [{cached}]", cached),
            new DisplayField(
                SelectionsLabel,
                string.Create(CultureInfo.InvariantCulture, $"{passes}/{TargetIdFinder.RequiredSelections} [{cached}]"),
                cached),
            new DisplayField(
                RestartsLabel,
                string.Create(CultureInfo.InvariantCulture, $"{restarts}/1 [{cached}]"),
                cached),
            new DisplayField(ClearedLabel, clearedValue, cached),
            new DisplayField(AdviceLabel, advice, DataSourceKind.Derived.ToWire()));
    }

    private static HuntDrawing HuntUnknown(TargetHuntKind kind, string status, string reason)
    {
        DisplayField unknown(string label) => new(label, $"UNKNOWN · {reason}", "UNKNOWN");
        return new HuntDrawing(
            kind,
            status,
            $"{ClearedLabel}: UNKNOWN · {reason}",
            ClearedMissing: false,
            Advice: $"UNKNOWN · {reason}",
            unknown(CandidatesLabel),
            unknown(AnchoredLabel),
            unknown(SelectionsLabel),
            unknown(RestartsLabel),
            unknown(ClearedLabel),
            unknown(AdviceLabel));
    }

    private static RoiDrawing DrawRoi(RoiReading roi)
    {
        if (roi.Calibration is { IsCalibrated: true } calibrated)
        {
            string when = calibrated.CalibratedAtUtc is { } at
                ? at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : "UNKNOWN";
            string line = string.Create(CultureInfo.InvariantCulture,
                $"calibrato il {when} su {calibrated.ClientWidth}x{calibrated.ClientHeight} — {IndependentSourceNote}");
            return new RoiDrawing(
                TargetRoiKind.Calibrated,
                $"{RoiLabel}: {line}",
                new DisplayField(RoiLabel, $"{line} [CACHED]", DataSourceKind.Cached.ToWire()));
        }

        if (roi.FileExists && roi.FailureReason is { } reason
            && reason != TargetRoiCalibration.NotCalibratedReason)
        {
            string unreadable = $"UNKNOWN · {reason} — {IndependentSourceNote}";
            return new RoiDrawing(
                TargetRoiKind.Unreadable,
                $"{RoiLabel}: {unreadable}",
                new DisplayField(RoiLabel, unreadable, "UNKNOWN"));
        }

        string absent = $"{RoiNotCalibratedLabel} — {IndependentSourceNote}";
        return new RoiDrawing(
            TargetRoiKind.NotCalibrated,
            $"{RoiLabel}: {absent}",
            new DisplayField(RoiLabel, absent, "UNKNOWN"));
    }

    private static CandidateFile ReadCandidateFile(string path)
    {
        if (!File.Exists(path))
            return new CandidateFile(Exists: false, ReadFailure: null, Candidates: null);

        try
        {
            return new CandidateFile(Exists: true, ReadFailure: null, Candidates: ParseFormat(File.ReadAllLines(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CandidateFile(
                Exists: true,
                ReadFailure: $"{HuntUnreadableReason}:{ex.GetType().Name}",
                Candidates: null);
        }
    }

    /// <summary>
    /// The layout <see cref="TargetIdFinder.Format"/> writes. Kept here because
    /// the loader on that type is internal to the runtime; the file is the
    /// contract the panel is allowed to observe. Field names and hit lines
    /// match that writer, including the <c>cleared=1</c> flag for the no-target pass.
    /// </summary>
    private static TargetIdCandidates ParseFormat(string[] lines)
    {
        int passes = 0, restarts = 0, processId = 0;
        bool sawCleared = false;
        var hits = new List<TargetIdHit>();

        foreach (string line in lines)
        {
            string text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;

            if (text.StartsWith("selections=", StringComparison.Ordinal))
                int.TryParse(text.AsSpan(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out passes);
            else if (text.StartsWith("restarts=", StringComparison.Ordinal))
                int.TryParse(text.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out restarts);
            else if (text.StartsWith("process=", StringComparison.Ordinal))
                int.TryParse(text.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out processId);
            else if (text.StartsWith("cleared=", StringComparison.Ordinal))
                sawCleared = text.AsSpan(8).SequenceEqual("1");
            else if (TryParseHit(text, out TargetIdHit hit))
                hits.Add(hit);
        }

        return new TargetIdCandidates(passes, restarts, sawCleared, processId, hits);
    }

    private static bool TryParseHit(string line, out TargetIdHit hit)
    {
        hit = default;
        // Four columns since the behavioural oracle: the fourth is the value the word
        // takes when the target is cleared. A three-column row was written by the
        // scene-list oracle and is refused rather than shown with a made-up sentinel.
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            return false;

        MapIdAnchorKind? anchor = MapIdAnchors.Parse(parts[0]);
        if (anchor is not { } kind)
            return false;

        if (!long.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long offset)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long entityId)
            || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long nobody))
        {
            return false;
        }

        hit = new TargetIdHit(kind, offset, entityId, nobody);
        return true;
    }

    private static RoiReading ReadRoi(string path)
    {
        bool exists = File.Exists(path);
        TargetRoiCalibration calibration = TargetRoiCalibration.Load(path, out string? reason);
        return new RoiReading(exists, calibration, reason);
    }

    private static int CountDurable(IReadOnlyList<TargetIdHit> hits)
    {
        int durable = 0;
        foreach (TargetIdHit hit in hits)
        {
            if (hit.IsDurable)
                durable++;
        }

        return durable;
    }

    private static string FileSignature(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return "absent";
            return string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unreadable:" + ex.GetType().Name;
        }
    }

    internal readonly record struct CandidateFile(
        bool Exists,
        string? ReadFailure,
        TargetIdCandidates? Candidates);

    internal readonly record struct RoiReading(
        bool FileExists,
        TargetRoiCalibration Calibration,
        string? FailureReason);

    private readonly record struct HuntDrawing(
        TargetHuntKind Kind,
        string Status,
        string ClearedLine,
        bool ClearedMissing,
        string Advice,
        DisplayField CandidatesField,
        DisplayField AnchoredField,
        DisplayField SelectionsField,
        DisplayField RestartsField,
        DisplayField ClearedField,
        DisplayField AdviceField);

    private readonly record struct RoiDrawing(
        TargetRoiKind Kind,
        string Line,
        DisplayField Field);
}
