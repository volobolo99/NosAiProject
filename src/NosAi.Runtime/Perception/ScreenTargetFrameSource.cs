using NosAi.LiveIntegration;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Reads the target frame off captured pixels, at the region the operator
/// calibrated.
/// </summary>
/// <remarks>
/// <para>
/// The screen half of ADR-0018. Every refusal here is named, and none of them
/// falls back to a region nobody aimed: the whole reason the calibration exists
/// is that <see cref="RoiSegmenter"/>'s <see cref="RoiKind.TargetHpBar"/>
/// fractions are an uninspected guess, and reading them would produce a confident
/// <i>no target</i> rather than an error.
/// </para>
/// <para>
/// The client area is required rather than optional. Without it the region is a
/// fraction of the whole desktop, which is the client area only when the client
/// is fullscreen — the mistake T-03 made, where the reader measured the editor
/// behind the game.
/// </para>
/// </remarks>
public sealed class ScreenTargetFrameSource : ITargetFrameSource
{
    private readonly IFrameSource _frames;
    private readonly TargetRoiCalibration _calibration;
    private readonly Func<PixelRect?> _clientArea;

    /// <param name="frames">Where the pixels come from.</param>
    /// <param name="calibration">The region the operator confirmed against a crop.</param>
    /// <param name="clientArea">
    /// Where the client draws, re-read on each call because a window moves.
    /// Returning null means the window was not located, which is a refusal.
    /// </param>
    public ScreenTargetFrameSource(
        IFrameSource frames,
        TargetRoiCalibration calibration,
        Func<PixelRect?> clientArea)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _clientArea = clientArea ?? throw new ArgumentNullException(nameof(clientArea));
    }

    /// <inheritdoc />
    public TargetFrameObservation Read()
    {
        // Checked first so an uncalibrated runtime reports the calibration rather
        // than whatever the capture happened to do.
        if (!_calibration.IsCalibrated)
            return Refused(TargetRoiCalibration.NotCalibratedReason);

        if (_clientArea() is not { } clientArea)
            return Refused("client_window_not_located");

        if (!_frames.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
            return Refused("no_frame_pixels");

        if (_calibration.Resolve(clientArea) is not { } roi)
            return Refused(TargetRoiCalibration.NotCalibratedReason);

        // Off the frame is a refusal, not a clamp. A clamped region is a different
        // region from the calibrated one, and the reading would be of pixels
        // nobody confirmed.
        if (!roi.IsWithin(frame.Width, frame.Height))
            return Refused("target_roi_outside_frame", frame.CapturedUtc);

        byte[] crop = ScreenVitalReader.Crop(frame, roi);
        return new TargetFrameObservation(
            TargetFrameReader.Read(crop, roi.Width, roi.Height),
            frame.CapturedUtc);
    }

    private static TargetFrameObservation Refused(string reason, DateTime? atUtc = null)
        => new(
            new TargetFrameReading(TargetFrameState.Unreadable, HpRatio: null, Confidence: 0, reason),
            atUtc ?? DateTime.UtcNow);
}
