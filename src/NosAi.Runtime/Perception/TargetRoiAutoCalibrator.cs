using System.Globalization;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Lifecycle of an automatic target-ROI calibration session. A session starts in
/// <see cref="TargetRoiCalibrationState.Waiting"/> while independent groups of labelled
/// frames are collected, moves to <see cref="TargetRoiCalibrationState.Proposed"/> once the
/// region has been derived from the luminance difference between the two groups, and ends
/// either <see cref="TargetRoiCalibrationState.Confirmed"/> (the derived region was verified
/// on fresh observations and the calibration file was written) or
/// <see cref="TargetRoiCalibrationState.Failed"/> (a verification contradicted the label, or
/// the file could not be written). The state is exposed as a byte so it can be persisted
/// cheaply if needed.
/// </summary>
public enum TargetRoiCalibrationState : byte
{
    /// <summary>Collecting labelled frames and attempting a derivation.</summary>
    Waiting = 0,

    /// <summary>A region was derived and is being verified on independent frames.</summary>
    Proposed = 1,

    /// <summary>The derived region passed verification and the calibration was written.</summary>
    Confirmed = 2,

    /// <summary>Verification contradicted the label, or writing the calibration failed.</summary>
    Failed = 3,
}

/// <summary>
/// Derives the target-box ROI by itself. The second source labels each offered frame as
/// "target present" or "target absent"; luminance is accumulated per group and the changed
/// region is the bounding rectangle of the pixels whose average luminance differs enough
/// between the groups. The derived region is then verified on independent observations,
/// and only a passing verification writes a calibration through
/// <see cref="TargetRoiCalibration.Confirmed"/>.
/// </summary>
/// <remarks>
/// Frames are only meaningful while the same frame size and the same client area are in
/// effect: samples collected under one geometry are never mixed with samples from another,
/// so a geometry change resets the whole session instead of corrupting the means.
/// </remarks>
public sealed class TargetRoiAutoCalibrator
{
    public const string TestId = "T-09";
    public const int DefaultMinFramesPerGroup = 3;
    public const int DefaultMinVerificationsPerCase = 3;
    public const int DefaultDifferenceThreshold = 24;
    public const int DefaultMinChangedPixels = 64;
    public const int DefaultMaxFramesPerGroup = 12;

    private readonly string _calibrationPath;
    private readonly int _differenceThreshold;
    private readonly int _minFramesPerGroup;
    private readonly int _minVerificationsPerCase;
    private readonly int _minChangedPixels;
    private readonly int _maxFramesPerGroup;

    private readonly SortedDictionary<string, int> _rejections = new();

    private TargetRoiCalibrationState _state;
    private PixelRect? _proposed;
    private PixelRect _geometryClientArea;
    private int _geometryFrameWidth;
    private int _geometryFrameHeight;
    private bool _hasGeometry;

    private int[]? _sumWithTarget;
    private int[]? _sumWithoutTarget;
    private int _framesWithTarget;
    private int _framesWithoutTarget;
    private int _changedPixels;
    private string? _waitingReason;

    private int _verifiedPresent;
    private int _verifiedAbsent;
    private int _contradictions;
    private int _unreadableVerifications;

    private DataSourceKind _provenance = DataSourceKind.Unknown;
    private string? _failureReason;
    private string? _writtenPath;

    /// <summary>
    /// Creates a calibrator that writes to <paramref name="calibrationPath"/> only after a
    /// verified derivation. Accumulators are <see langword="int"/> arrays on purpose: with up
    /// to <see cref="DefaultMaxFramesPerGroup"/> frames of luminance 255 the per-pixel sum stays
    /// below 3060, well inside an int but not inside a byte.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="calibrationPath"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric parameter is outside its allowed range.</exception>
    public TargetRoiAutoCalibrator(
        string calibrationPath,
        int differenceThreshold = DefaultDifferenceThreshold,
        int minFramesPerGroup = DefaultMinFramesPerGroup,
        int minVerificationsPerCase = DefaultMinVerificationsPerCase,
        int minChangedPixels = DefaultMinChangedPixels,
        int maxFramesPerGroup = DefaultMaxFramesPerGroup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(calibrationPath);
        if (differenceThreshold is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(differenceThreshold), "Must be between 1 and 255.");
        }

        if (minFramesPerGroup < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minFramesPerGroup), "Must be at least 1.");
        }

        if (minVerificationsPerCase < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minVerificationsPerCase), "Must be at least 1.");
        }

        if (minChangedPixels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minChangedPixels), "Must be at least 1.");
        }

        if (maxFramesPerGroup < minFramesPerGroup)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFramesPerGroup), "Must be >= minFramesPerGroup.");
        }

        _calibrationPath = calibrationPath;
        _differenceThreshold = differenceThreshold;
        _minFramesPerGroup = minFramesPerGroup;
        _minVerificationsPerCase = minVerificationsPerCase;
        _minChangedPixels = minChangedPixels;
        _maxFramesPerGroup = maxFramesPerGroup;
        _state = TargetRoiCalibrationState.Waiting;
    }

    /// <summary>Current lifecycle state of the calibration session.</summary>
    public TargetRoiCalibrationState State => _state;

    /// <summary>True once the session ended, either confirmed or failed.</summary>
    public bool IsClosed => State is TargetRoiCalibrationState.Confirmed or TargetRoiCalibrationState.Failed;

    /// <summary>The derived region in frame coordinates, when one exists.</summary>
    public PixelRect? Proposed => _proposed;

    /// <summary>Frames labelled "target present" accumulated in the derivation group.</summary>
    public int FramesWithTarget => _framesWithTarget;

    /// <summary>Frames labelled "target absent" accumulated in the derivation group.</summary>
    public int FramesWithoutTarget => _framesWithoutTarget;

    /// <summary>Pixels differing between the mean-luminance groups of the last derivation attempt.</summary>
    public int ChangedPixels => _changedPixels;

    /// <summary>Independent frames on which the reader said Present while the label said target.</summary>
    public int VerifiedPresent => _verifiedPresent;

    /// <summary>Independent frames on which the reader said Absent while the label said no target.</summary>
    public int VerifiedAbsent => _verifiedAbsent;

    /// <summary>Verification frames in which the reader disagreed with the second source.</summary>
    public int Contradictions => _contradictions;

    /// <summary>Verification frames the reader could not classify.</summary>
    public int UnreadableVerifications => _unreadableVerifications;

    /// <summary>
    /// Why offered observations were refused, counted per reason. A separate waiting reason
    /// (<c>target_region_not_separable</c>, <c>derived_region_too_small</c>) is intentionally
    /// not counted here: it does not refuse a frame, it only explains why a derivation attempt
    /// did not yet produce a rectangle.
    /// </summary>
    public IReadOnlyDictionary<string, int> Rejections => _rejections;

    /// <summary>Why the session failed, when it did; null otherwise.</summary>
    public string? FailureReason => _failureReason;

    /// <summary>Path the calibration was written to, when the session confirmed.</summary>
    public string? WrittenPath => _writtenPath;

    /// <summary>
    /// Operator-facing description of the current state, in Italian. Formatting is pinned to
    /// <see cref="CultureInfo.InvariantCulture"/> so the same session reads identically on any
    /// machine.
    /// </summary>
    public string PendingReason
    {
        get
        {
            switch (_state)
            {
                case TargetRoiCalibrationState.Confirmed:
                    return string.Create(CultureInfo.InvariantCulture,
                        $"chiusa: ROI derivata e verificata, calibrazione scritta in {_writtenPath}");
                case TargetRoiCalibrationState.Failed:
                    return string.Create(CultureInfo.InvariantCulture,
                        $"chiusa senza scrivere: {_failureReason}");
                case TargetRoiCalibrationState.Proposed:
                    PixelRect rect = _proposed!.Value;
                    return string.Create(CultureInfo.InvariantCulture,
                        $"rettangolo proposto {rect.Width}x{rect.Height}@{rect.X},{rect.Y}: " +
                        $"verifica a {_verifiedPresent}/{_minVerificationsPerCase} presenti e " +
                        $"{_verifiedAbsent}/{_minVerificationsPerCase} assenti");
                default:
                    string baseText = string.Create(CultureInfo.InvariantCulture,
                        $"servono almeno {_minFramesPerGroup} frame per gruppo: " +
                        $"{_framesWithTarget} con bersaglio, {_framesWithoutTarget} senza");
                    // Once both groups are full no new evidence can arrive; if the derivation
                    // still produced nothing the operator needs to know why, so append it.
                    if (_framesWithTarget >= _maxFramesPerGroup
                        && _framesWithoutTarget >= _maxFramesPerGroup
                        && _proposed is null
                        && _waitingReason is not null)
                    {
                        return string.Create(CultureInfo.InvariantCulture, $"{baseText} — {_waitingReason}");
                    }

                    return baseText;
            }
        }
    }

    /// <summary>
    /// Offers one labelled frame to the calibrator. Returns the state after the observation.
    /// A closed calibrator ignores further offers so a confirmed session can never be
    /// overwritten by late frames.
    /// </summary>
    public TargetRoiCalibrationState Offer(
        CaptureFrame? frame,
        bool? hasTarget,
        PixelRect clientArea,
        bool clientInForeground = true)
    {
        if (IsClosed)
        {
            return State;
        }

        if (!clientInForeground)
        {
            RegisterRejection("client_not_foreground");
            return State;
        }

        if (frame is null || !frame.HasPixels)
        {
            RegisterRejection("frame_without_pixels");
            return State;
        }

        if (hasTarget is null)
        {
            // UNKNOWN is not "absent": an unlabelled frame must never enter either group.
            RegisterRejection("target_label_unknown");
            return State;
        }

        if (clientArea.Width <= 0 || clientArea.Height <= 0 || !clientArea.IsWithin(frame.Width, frame.Height))
        {
            RegisterRejection("client_area_outside_frame");
            return State;
        }

        if (!_hasGeometry)
        {
            _hasGeometry = true;
            _geometryFrameWidth = frame.Width;
            _geometryFrameHeight = frame.Height;
            _geometryClientArea = clientArea;
        }
        else if (frame.Width != _geometryFrameWidth
                 || frame.Height != _geometryFrameHeight
                 || clientArea != _geometryClientArea)
        {
            // Samples gathered at a different geometry are not comparable to the current ones:
            // discard everything and restart the session on the new geometry, using this very
            // frame as the first sample of the fresh session.
            RegisterRejection("frame_geometry_changed");
            ResetSession();
            _geometryFrameWidth = frame.Width;
            _geometryFrameHeight = frame.Height;
            _geometryClientArea = clientArea;
        }

        if (_provenance == DataSourceKind.Unknown)
        {
            _provenance = frame.Source;
        }

        bool hasTargetValue = hasTarget.Value;
        return _state == TargetRoiCalibrationState.Waiting
            ? ProcessDerivationFrame(frame, hasTargetValue, clientArea)
            : ProcessVerificationFrame(frame, hasTargetValue, clientArea);
    }

    /// <summary>
    /// Builds the T-09 deferred evidence from the current session state. Complete only when
    /// the calibration was confirmed, so the evidence pipeline can decide to wait otherwise.
    /// </summary>
    public DeferredEvidenceRecord BuildEvidence(DateTime closedUtc)
    {
        bool groupsCollected = _framesWithTarget >= _minFramesPerGroup
                               && _framesWithoutTarget >= _minFramesPerGroup;
        bool regionDerived = _proposed is not null;
        bool regionVerified = _contradictions == 0
                              && _verifiedPresent >= _minVerificationsPerCase
                              && _verifiedAbsent >= _minVerificationsPerCase;
        bool calibrationWritten = _writtenPath is not null;

        var criteria = new List<DeferredCriterion>(4)
        {
            new(
                "groups_collected",
                "Raccolti almeno tre frame con bersaglio e tre senza, etichettati dalla seconda fonte.",
                groupsCollected,
                string.Create(CultureInfo.InvariantCulture,
                    $"{_framesWithTarget} con bersaglio, {_framesWithoutTarget} senza")),
            new(
                "region_derived",
                "Il rettangolo del riquadro bersaglio è stato derivato dalla differenza fra i due gruppi.",
                regionDerived,
                DescribeDerivation()),
            new(
                "region_verified",
                "La ROI derivata dà Present col bersaglio e Absent senza, su osservazioni indipendenti.",
                regionVerified,
                DescribeVerification()),
            new(
                "calibration_written",
                "La calibrazione è stata scritta in data/perception/target-roi.calibration.",
                calibrationWritten,
                _writtenPath is not null
                    ? _writtenPath
                    : string.Create(CultureInfo.InvariantCulture,
                        $"non scritta: {_failureReason ?? PendingReason}")),
        };

        var measurements = new Dictionary<string, string>
        {
            ["state"] = _state.ToString(),
            ["frames_with_target"] = _framesWithTarget.ToString(CultureInfo.InvariantCulture),
            ["frames_without_target"] = _framesWithoutTarget.ToString(CultureInfo.InvariantCulture),
            ["changed_pixels"] = _changedPixels.ToString(CultureInfo.InvariantCulture),
            ["derived_rect"] = DescribeDerivedRect(),
            ["derived_fractions"] = DescribeDerivedFractions(),
            ["verified_present"] = _verifiedPresent.ToString(CultureInfo.InvariantCulture),
            ["verified_absent"] = _verifiedAbsent.ToString(CultureInfo.InvariantCulture),
            ["contradictions"] = _contradictions.ToString(CultureInfo.InvariantCulture),
            ["unreadable_verifications"] = _unreadableVerifications.ToString(CultureInfo.InvariantCulture),
            ["difference_threshold"] = _differenceThreshold.ToString(CultureInfo.InvariantCulture),
            ["min_frames_per_group"] = _minFramesPerGroup.ToString(CultureInfo.InvariantCulture),
            ["min_verifications_per_case"] = _minVerificationsPerCase.ToString(CultureInfo.InvariantCulture),
            ["written_path"] = _writtenPath ?? "—",
            ["failure_reason"] = _failureReason ?? "—",
        };

        foreach (KeyValuePair<string, int> rejection in _rejections)
        {
            measurements["rejected_" + rejection.Key] = rejection.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new DeferredEvidenceRecord(
            TestId,
            closedUtc,
            State == TargetRoiCalibrationState.Confirmed,
            criteria,
            measurements,
            _provenance,
            _writtenPath is null ? Array.Empty<string>() : new[] { _writtenPath! },
            State == TargetRoiCalibrationState.Confirmed ? null : PendingReason);
    }

    private TargetRoiCalibrationState ProcessDerivationFrame(CaptureFrame frame, bool hasTarget, PixelRect clientArea)
    {
        int pixelCount = clientArea.Width * clientArea.Height;
        ReadOnlySpan<byte> span = frame.Bgra.Span;

        int groupCount = hasTarget ? _framesWithTarget : _framesWithoutTarget;
        if (groupCount >= _maxFramesPerGroup)
        {
            RegisterRejection("group_full");
        }
        else
        {
            // Allocate the accumulator lazily so an empty group costs nothing until it grows.
            int[] sums = hasTarget
                ? _sumWithTarget ??= new int[pixelCount]
                : _sumWithoutTarget ??= new int[pixelCount];

            int frameWidth = frame.Width;
            int left = clientArea.X;
            int top = clientArea.Y;
            int width = clientArea.Width;
            int height = clientArea.Height;
            int p = 0;
            for (int y = 0; y < height; y++)
            {
                int rowBase = ((top + y) * frameWidth + left) * 4;
                for (int x = 0; x < width; x++, p++)
                {
                    int i = rowBase + x * 4;
                    int luminance = (span[i + 2] * 299 + span[i + 1] * 587 + span[i] * 114) / 1000;
                    sums[p] += luminance;
                }
            }

            if (hasTarget)
            {
                _framesWithTarget++;
            }
            else
            {
                _framesWithoutTarget++;
            }
        }

        if (_framesWithTarget >= _minFramesPerGroup && _framesWithoutTarget >= _minFramesPerGroup)
        {
            TryDerive(clientArea);
        }

        return State;
    }

    /// <summary>
    /// Marks every pixel whose mean luminance differs enough between the groups, then proposes
    /// the bounding rectangle of those pixels. A failed attempt leaves the session Waiting so
    /// more frames can refine the means; the reason is remembered for the operator text.
    /// </summary>
    private void TryDerive(PixelRect clientArea)
    {
        int width = clientArea.Width;
        int height = clientArea.Height;
        int[] sumWithTarget = _sumWithTarget!;
        int[] sumWithoutTarget = _sumWithoutTarget!;
        int countWithTarget = _framesWithTarget;
        int countWithoutTarget = _framesWithoutTarget;

        int changed = 0;
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        for (int p = 0; p < sumWithTarget.Length; p++)
        {
            int meanWithTarget = sumWithTarget[p] / countWithTarget;
            int meanWithoutTarget = sumWithoutTarget[p] / countWithoutTarget;
            if (Math.Abs(meanWithTarget - meanWithoutTarget) >= _differenceThreshold)
            {
                changed++;
                int x = p % width;
                int y = p / width;
                if (x < minX)
                {
                    minX = x;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        _changedPixels = changed;

        if (changed < _minChangedPixels)
        {
            _waitingReason = "target_region_not_separable";
            return;
        }

        int rectWidth = maxX - minX + 1;
        int rectHeight = maxY - minY + 1;
        if (rectWidth < HudBarFillReader.MinWidth || rectHeight < HudBarFillReader.MinHeight)
        {
            _waitingReason = "derived_region_too_small";
            return;
        }

        _proposed = new PixelRect(clientArea.X + minX, clientArea.Y + minY, rectWidth, rectHeight);
        _state = TargetRoiCalibrationState.Proposed;
        _waitingReason = null;
        // The means are no longer needed once a region is proposed; verification works on
        // crops of fresh frames, so release the memory until a geometry reset restarts it.
        _sumWithTarget = null;
        _sumWithoutTarget = null;
    }

    private TargetRoiCalibrationState ProcessVerificationFrame(CaptureFrame frame, bool hasTarget, PixelRect clientArea)
    {
        PixelRect proposed = _proposed!.Value;
        byte[] crop = ScreenVitalReader.Crop(frame, proposed);
        if (crop.Length == 0)
        {
            RegisterRejection("proposed_roi_outside_frame");
            return State;
        }

        TargetFrameReading reading = TargetFrameReader.Read(crop, proposed.Width, proposed.Height);

        if (hasTarget)
        {
            switch (reading.State)
            {
                case TargetFrameState.Present:
                    _verifiedPresent++;
                    break;
                case TargetFrameState.Absent:
                    _contradictions++;
                    _failureReason ??= "verify_absent_with_target";
                    break;
                default:
                    RecordUnreadableVerification(reading);
                    break;
            }
        }
        else
        {
            switch (reading.State)
            {
                case TargetFrameState.Absent:
                    _verifiedAbsent++;
                    break;
                case TargetFrameState.Present:
                    _contradictions++;
                    _failureReason ??= "verify_present_without_target";
                    break;
                default:
                    RecordUnreadableVerification(reading);
                    break;
            }
        }

        if (_contradictions > 0)
        {
            // A single contradiction means the proposed region does not track the target box:
            // write nothing, the caller must not trust it.
            _state = TargetRoiCalibrationState.Failed;
            return State;
        }

        if (_verifiedPresent < _minVerificationsPerCase || _verifiedAbsent < _minVerificationsPerCase)
        {
            return State;
        }

        double fx = (proposed.X - clientArea.X) / (double)clientArea.Width;
        double fy = (proposed.Y - clientArea.Y) / (double)clientArea.Height;
        double fw = proposed.Width / (double)clientArea.Width;
        double fh = proposed.Height / (double)clientArea.Height;
        try
        {
            // The captured timestamp keeps the calibration reproducible in tests: the written
            // moment is when the last verifying frame was captured, not when Save happened.
            TargetRoiCalibration.Confirmed(fx, fy, fw, fh, clientArea.Width, clientArea.Height, frame.CapturedUtc)
                .Save(_calibrationPath);
            _writtenPath = _calibrationPath;
            _state = TargetRoiCalibrationState.Confirmed;
        }
        catch (ArgumentOutOfRangeException)
        {
            _state = TargetRoiCalibrationState.Failed;
            _failureReason = "target_roi_outside_client_area";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _state = TargetRoiCalibrationState.Failed;
            _failureReason = "target_roi_unwritable:" + ex.GetType().Name;
        }

        return State;
    }

    private void RecordUnreadableVerification(TargetFrameReading reading)
    {
        _unreadableVerifications++;
        RegisterRejection("verify_unreadable:" + (reading.FailureReason ?? "unknown"));
    }

    /// <summary>
    /// Clears every piece of state that belongs to one geometry session. The rejection
    /// counters survive on purpose: refused observations did happen and stay observable.
    /// </summary>
    private void ResetSession()
    {
        _state = TargetRoiCalibrationState.Waiting;
        _proposed = null;
        _sumWithTarget = null;
        _sumWithoutTarget = null;
        _framesWithTarget = 0;
        _framesWithoutTarget = 0;
        _changedPixels = 0;
        _waitingReason = null;
        _verifiedPresent = 0;
        _verifiedAbsent = 0;
        _contradictions = 0;
        _unreadableVerifications = 0;
        _provenance = DataSourceKind.Unknown;
        _failureReason = null;
        _writtenPath = null;
    }

    private void RegisterRejection(string reason)
    {
        _rejections.TryGetValue(reason, out int count);
        _rejections[reason] = count + 1;
    }

    private string DescribeDerivation()
    {
        if (_proposed is { } rect)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"{rect.Width}x{rect.Height}@{rect.X},{rect.Y} ({_changedPixels} pixel cambiati)");
        }

        return _waitingReason ?? "derivazione non ancora tentata";
    }

    private string DescribeVerification()
    {
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{_verifiedPresent} presenti, {_verifiedAbsent} assenti, {_contradictions} contraddizioni");
        if (_failureReason is not null)
        {
            text = string.Create(CultureInfo.InvariantCulture, $"{text} — {_failureReason}");
        }

        return text;
    }

    private string DescribeDerivedRect()
    {
        if (_proposed is not { } rect)
        {
            return "—";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{rect.Width}x{rect.Height}@{rect.X},{rect.Y}");
    }

    private string DescribeDerivedFractions()
    {
        if (_proposed is not { } rect || !_hasGeometry)
        {
            return "—";
        }

        double fx = (rect.X - _geometryClientArea.X) / (double)_geometryClientArea.Width;
        double fy = (rect.Y - _geometryClientArea.Y) / (double)_geometryClientArea.Height;
        double fw = rect.Width / (double)_geometryClientArea.Width;
        double fh = rect.Height / (double)_geometryClientArea.Height;
        return string.Create(CultureInfo.InvariantCulture,
            $"x={fx:F6} y={fy:F6} w={fw:F6} h={fh:F6}");
    }
}
