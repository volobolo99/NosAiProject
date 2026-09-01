using NosAi.Runtime.Gate3;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Turns a map coordinate into a desktop pixel, using the transform the operator
/// measured.
/// </summary>
/// <remarks>
/// <para>
/// F2-3, and the place where a mistake makes the runtime click somewhere it was
/// not asked to. Every step that could go wrong refuses by name instead of
/// producing a point: no calibration, no client window, a client resized since
/// the calibration, or a point that lands outside the client area.
/// </para>
/// <para>
/// The calibration is stored in client-relative pixels and the window's position
/// is added here, on every call, because a window that has been dragged has not
/// invalidated the mapping — only moved it.
/// </para>
/// </remarks>
public sealed class CalibratedScreenProjection : IScreenProjection
{
    /// <summary>The projected point fell outside the area the client draws in.</summary>
    public const string OutsideClientAreaReason = "point_outside_client_area";

    /// <summary>The window is a different size than when the samples were taken.</summary>
    public const string ClientResizedReason = "screen_projection_client_size_changed";

    /// <summary>The client window could not be found, so there is nothing to be inside of.</summary>
    public const string WindowNotLocatedReason = "client_window_not_located";

    private readonly ScreenProjectionCalibration _calibration;
    private readonly Func<PixelRect?> _clientArea;

    /// <param name="clientArea">
    /// Re-read on every call, because a window moves and is resized while the
    /// runtime is running.
    /// </param>
    public CalibratedScreenProjection(
        ScreenProjectionCalibration calibration,
        Func<PixelRect?> clientArea)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _clientArea = clientArea ?? throw new ArgumentNullException(nameof(clientArea));
    }

    /// <inheritdoc />
    public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
    {
        screenX = 0;
        screenY = 0;

        if (!_calibration.IsCalibrated)
        {
            failureReason = ScreenProjectionCalibration.NotCalibratedReason;
            return false;
        }

        if (_clientArea() is not { } area || area.Width <= 0 || area.Height <= 0)
        {
            failureReason = WindowNotLocatedReason;
            return false;
        }

        // A resized client is a different zoom and a different layout, so the
        // measured transform no longer describes what is on screen. Scaling it
        // would assume the very structure this calibration exists to measure.
        if (area.Width != _calibration.ClientWidth || area.Height != _calibration.ClientHeight)
        {
            failureReason = ClientResizedReason;
            return false;
        }

        if (_calibration.Project(new NosAi.Runtime.Autonomy.MapPoint(mapX, mapY)) is not { } relative)
        {
            failureReason = ScreenProjectionCalibration.NotCalibratedReason;
            return false;
        }

        int candidateX = area.X + (int)Math.Round(relative.X);
        int candidateY = area.Y + (int)Math.Round(relative.Y);

        // The domain check the card asks for: a point outside the client area is a
        // refusal, not a click on the nearest border. Clamping would turn "that
        // coordinate is not on screen" into a real click at a place nobody chose.
        if (candidateX < area.X || candidateX >= area.Right
            || candidateY < area.Y || candidateY >= area.Bottom)
        {
            failureReason = OutsideClientAreaReason;
            return false;
        }

        screenX = candidateX;
        screenY = candidateY;
        failureReason = null;
        return true;
    }
}
