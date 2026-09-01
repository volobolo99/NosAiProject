using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.LiveIntegration;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Turns a map coordinate into a desktop pixel, using the transform that was
/// measured and the square the character is standing on right now.
/// </summary>
/// <remarks>
/// <para>
/// F2-3, and the place where a mistake makes the runtime click somewhere it was
/// not asked to. Every step that could go wrong refuses by name instead of
/// producing a point: no calibration, no client window, a client resized since
/// the calibration, an unknown character position, or a point that lands outside
/// the client area.
/// </para>
/// <para>
/// <b>Two moving parts, not one.</b> The calibration alone cannot place a map
/// coordinate on screen, because the camera follows the character: the same
/// square is drawn wherever the character's own position puts it. So the
/// calibration supplies the <i>shape</i> of the projection and the character's
/// live position supplies its <i>origin</i>, and the second is re-read on every
/// call. A cached position would aim at the square the target occupied when the
/// character was somewhere else.
/// </para>
/// <para>
/// <b>An unknown position is a refusal.</b> It cannot be treated as the map
/// origin, which is what a zero default would silently mean — ADR-0014's rule
/// that unknown is not zero, applied at the point where the mistake would become
/// a real click.
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

    /// <summary>
    /// Where the character is standing is not known, so no offset can be formed.
    /// </summary>
    public const string PlayerPositionUnknownReason = "player_position_unknown";

    private readonly ScreenProjectionCalibration _calibration;
    private readonly Func<PixelRect?> _clientArea;
    private readonly Func<ClassifiedValue<MapPoint>> _playerPosition;

    /// <param name="clientArea">
    /// Re-read on every call, because a window moves and is resized while the
    /// runtime is running.
    /// </param>
    /// <param name="playerPosition">
    /// The square the character is on, normally
    /// <see cref="NosAi.LiveIntegration.MemoryGameplayProvider.ReadPosition"/>. Classified rather than
    /// nullable so the refusal can carry <i>why</i> it is unknown — a broken
    /// pointer chain and a client sitting at the login screen are different
    /// problems and the operator has to be able to tell them apart.
    /// </param>
    public CalibratedScreenProjection(
        ScreenProjectionCalibration calibration,
        Func<PixelRect?> clientArea,
        Func<ClassifiedValue<MapPoint>> playerPosition)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
        _clientArea = clientArea ?? throw new ArgumentNullException(nameof(clientArea));
        _playerPosition = playerPosition ?? throw new ArgumentNullException(nameof(playerPosition));
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

        ClassifiedValue<MapPoint> player = _playerPosition();
        if (!player.HasValue)
        {
            failureReason = player.FailureReason is { Length: > 0 } why
                ? $"{PlayerPositionUnknownReason}:{why}"
                : PlayerPositionUnknownReason;
            return false;
        }

        var offset = new MapPoint(mapX - player.Value.X, mapY - player.Value.Y);
        if (_calibration.ProjectDelta(offset) is not { } relative)
        {
            failureReason = ScreenProjectionCalibration.NotCalibratedReason;
            return false;
        }

        int candidateX = area.X + (int)Math.Round(relative.X);
        int candidateY = area.Y + (int)Math.Round(relative.Y);

        // The domain check the card asks for: a point outside the client area is a
        // refusal, not a click on the nearest border. Clamping would turn "that
        // coordinate is not on screen" into a real click at a place nobody chose,
        // and with a camera that follows the character an off-screen target is an
        // ordinary event rather than an error — something far away is simply not
        // drawn, and walking closer is the answer, not clicking the edge.
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
