namespace NosAi.Runtime.Perception;

/// <summary>Reads the dialog-window ROI of the client's HUD.</summary>
/// <remarks>
/// An interface for the same reason <c>ITargetFrameSource</c> is one: so
/// <see cref="DialogWindowStateComposer"/> can be exercised without a
/// desktop, and a runtime with no capture is a runtime that does not supply
/// one rather than one that supplies a fake.
/// </remarks>
public interface IDialogWindowSource
{
    /// <summary>Reads the region once, with the time the pixels were captured.</summary>
    DialogWindowObservation Read();
}

/// <summary>
/// Reads the dialog-window frame off captured pixels, at the region the
/// operator calibrated -- the screen half of the dialog-presence detection
/// this type's own investigation found buildable
/// (docs/agents/DEEPSEEK_TASKS.md "Candidati da investigare": no wire
/// channel exists for dialog/quest text, but presence/absence of the panel
/// itself is readable without OCR, the same way <see cref="ScreenTargetFrameSource"/>
/// already reads target-frame presence without it).
/// </summary>
/// <remarks>
/// The client area is required rather than optional, same reasoning as
/// <see cref="ScreenTargetFrameSource"/>: without it the region is a
/// fraction of the whole desktop, which is the client area only when the
/// client is fullscreen.
/// </remarks>
public sealed class ScreenDialogWindowSource : IDialogWindowSource
{
    private readonly IFrameSource _frames;
    private readonly DialogRoiCalibration _calibration;
    private readonly Func<PixelRect?> _clientArea;

    /// <param name="frames">Where the pixels come from.</param>
    /// <param name="calibration">The region and empty-baseline colors the operator confirmed against a crop.</param>
    /// <param name="clientArea">
    /// Where the client draws, re-read on each call because a window moves.
    /// Returning null means the window was not located, which is a refusal.
    /// </param>
    public ScreenDialogWindowSource(
        IFrameSource frames,
        DialogRoiCalibration calibration,
        Func<PixelRect?> clientArea)
    {
        _frames = frames ?? throw new ArgumentNullException(nameof(frames));
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _clientArea = clientArea ?? throw new ArgumentNullException(nameof(clientArea));
    }

    /// <inheritdoc />
    public DialogWindowObservation Read()
    {
        // Checked first so an uncalibrated runtime reports the calibration
        // rather than whatever the capture happened to do.
        if (!_calibration.IsCalibrated)
            return Refused(DialogRoiCalibration.NotCalibratedReason);

        if (_clientArea() is not { } clientArea)
            return Refused("client_window_not_located");

        if (!_frames.TryAcquire(out CaptureFrame frame) || !frame.HasPixels)
            return Refused("no_frame_pixels");

        if (_calibration.Resolve(clientArea) is not { } roi)
            return Refused(DialogRoiCalibration.NotCalibratedReason);

        // Off the frame is a refusal, not a clamp -- a clamped region is a
        // different region from the calibrated one.
        if (!roi.IsWithin(frame.Width, frame.Height))
            return Refused("dialog_roi_outside_frame", frame.CapturedUtc);

        byte[] crop = ScreenVitalReader.Crop(frame, roi);
        DialogWindowReading reading = DialogWindowReader.Read(
            crop, roi.Width, roi.Height,
            _calibration.BaselineMeanB, _calibration.BaselineMeanG, _calibration.BaselineMeanR);

        return new DialogWindowObservation(reading, frame.CapturedUtc);
    }

    private static DialogWindowObservation Refused(string reason, DateTime? atUtc = null)
        => new(new DialogWindowReading(DialogWindowState.Unreadable, null, reason), atUtc ?? DateTime.UtcNow);
}
