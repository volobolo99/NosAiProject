using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>The limits of the map the character is on, when they are known.</summary>
public readonly record struct MapBounds(int Width, int Height)
{
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}

/// <summary>Reads the player's own position.</summary>
/// <remarks>
/// Kept apart from <see cref="IGameplayProvider"/> because it answers the one
/// question that provider cannot: the server never sends the player's position,
/// so it is not on the wire at all (docs/PROTOCOLLO_NOSTALE.md).
/// </remarks>
public interface IPlayerPositionProvider
{
    /// <summary>The position, or why it is not known.</summary>
    ClassifiedValue<MapPoint> ReadPosition();
}

/// <summary>
/// The player's own position, read from the client's memory and checked against
/// what must be true of it.
/// </summary>
/// <remarks>
/// <para>
/// F1-10, and the reason ADR-0014 could lift ADR-0012's prohibition without
/// lifting its argument. A moved offset does not fail: it returns four readable
/// bytes and a plausible number. The classification is only honest if something
/// can tell the two apart, so the validity check <b>is</b> the provider — reading
/// the bytes is the easy half.
/// </para>
/// <para>
/// Three checks, all three required:
/// </para>
/// <list type="number">
/// <item><b>Range.</b> NosTale map coordinates run to two and three digits — the
/// captures show <c>121 110</c> and <c>109 63</c>. A value outside
/// <see cref="MaxPlausibleCoordinate"/> is a different field, not a distant
/// character.</item>
/// <item><b>Continuity.</b> A step between consecutive readings larger than the
/// character's speed allows in the time between them is an offset that moved, not
/// somebody who ran. The speed comes from <c>cond</c>.</item>
/// <item><b>Map coherence.</b> When the current map is known, a coordinate
/// outside it is not a position.</item>
/// </list>
/// <para>
/// <c>LIVE</c> while all of them hold; <c>UNKNOWN</c> with the failing check's own
/// reason the moment one gives. <b>Never the last good value</b> — the case
/// ADR-0014 names in full, because a retained coordinate is exactly what makes a
/// moved offset invisible.
/// </para>
/// </remarks>
public sealed class MemoryGameplayProvider : IPlayerPositionProvider
{
    /// <summary>
    /// The largest coordinate a NosTale map is taken to have.
    /// </summary>
    /// <remarks>
    /// The captures show two and three digits, and no map in them approaches this.
    /// It is deliberately loose: the check is here to reject a field that is not a
    /// coordinate at all — a pointer, a timestamp, an item count — not to police
    /// the edges of a map, which is what the map-bounds check is for.
    /// </remarks>
    public const int MaxPlausibleCoordinate = 1000;

    /// <summary>Tiles per second at speed 1, used to turn <c>cond</c> into a bound.</summary>
    /// <remarks>
    /// Deliberately generous. This check exists to catch a reading that jumped
    /// across the address space, which is orders of magnitude out, not to measure
    /// movement precisely — and a bound that is too tight would reject a real
    /// character after a lag spike.
    /// </remarks>
    public const double TilesPerSecondPerSpeedUnit = 1.0;

    /// <summary>Allowed on top of speed × elapsed, for a reading either side of a jitter.</summary>
    public const double ContinuitySlackTiles = 4.0;

    private readonly Func<IntPtr?> _moduleBase;
    private readonly Func<PlayerPositionOffsets> _offsets;
    private readonly Func<IntPtr, DateTime, ClassifiedValue<int?>> _readCoordinate;
    private readonly Func<int?> _movementSpeed;
    private readonly Func<MapBounds?> _mapBounds;
    private readonly TimeProvider _clock;

    private MapPoint? _previous;
    private DateTime _previousAtUtc;

    /// <param name="moduleBase">
    /// Base address of the module the offsets are relative to, or null when the
    /// client is not attached. Re-read every time: ASLR moves it on every start.
    /// </param>
    /// <param name="readCoordinate">
    /// Reads one validated 32-bit value, normally
    /// <see cref="ProcessMemoryReader.ReadValidatedInt32"/> with the range check
    /// already bound in.
    /// </param>
    /// <param name="movementSpeed">
    /// The character's speed from <c>cond</c>, or null when nothing has reported
    /// it. Null is not "stationary": without it the continuity check cannot run.
    /// </param>
    /// <param name="mapBounds">
    /// The current map's limits, or null when the map is not known. The card makes
    /// this check conditional in so many words — an unknown map skips it, where an
    /// unknown speed does not, because a speed of "unknown" still leaves a check
    /// that ought to have run.
    /// </param>
    public MemoryGameplayProvider(
        Func<IntPtr?> moduleBase,
        Func<PlayerPositionOffsets> offsets,
        Func<IntPtr, DateTime, ClassifiedValue<int?>> readCoordinate,
        Func<int?>? movementSpeed = null,
        Func<MapBounds?>? mapBounds = null,
        TimeProvider? clock = null)
    {
        _moduleBase = moduleBase ?? throw new ArgumentNullException(nameof(moduleBase));
        _offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
        _readCoordinate = readCoordinate ?? throw new ArgumentNullException(nameof(readCoordinate));
        _movementSpeed = movementSpeed ?? (static () => null);
        _mapBounds = mapBounds ?? (static () => null);
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ClassifiedValue<MapPoint> ReadPosition()
    {
        PlayerPositionOffsets offsets = _offsets();
        if (offsets.UnusableReason is { } offsetsReason)
            return Unknown(offsetsReason);

        if (_moduleBase() is not { } moduleBase || moduleBase == IntPtr.Zero)
            return Unknown("client_module_not_attached");

        DateTime now = _clock.GetUtcNow().UtcDateTime;

        ClassifiedValue<int?> x = _readCoordinate(moduleBase + offsets.OffsetX, now);
        if (!x.HasValue || x.Value is not { } mapX)
            return Unknown(x.FailureReason ?? "player_x_unreadable");

        ClassifiedValue<int?> y = _readCoordinate(moduleBase + offsets.OffsetY, now);
        if (!y.HasValue || y.Value is not { } mapY)
            return Unknown(y.FailureReason ?? "player_y_unreadable");

        // 1. Range. A value this far out is a different field, not a far-off
        //    character: a pointer, a timestamp, a count.
        if (!IsPlausibleCoordinate(mapX) || !IsPlausibleCoordinate(mapY))
            return Unknown($"position_out_of_range:{mapX},{mapY}");

        var position = new MapPoint(mapX, mapY);

        // 3. Map coherence, checked before continuity because it needs no history:
        //    a coordinate off the map is not a position whatever it was before.
        if (_mapBounds() is { } bounds && !bounds.Contains(mapX, mapY))
            return Unknown($"position_outside_map:{mapX},{mapY}");

        // 2. Continuity, against the previous accepted reading.
        if (_previous is { } previous)
        {
            if (_movementSpeed() is not { } speed)
            {
                // The check cannot run, so it has not passed. A reading validated
                // by two checks out of three is not LIVE by ADR-0014's bar, and
                // saying so is what keeps that bar meaningful.
                Forget();
                return Unknown("movement_speed_unknown");
            }

            double elapsedSeconds = Math.Max(0, (now - _previousAtUtc).TotalSeconds);
            double allowed = (speed * TilesPerSecondPerSpeedUnit * elapsedSeconds) + ContinuitySlackTiles;
            double travelled = Distance(previous, position);

            if (travelled > allowed)
            {
                // Not a character that ran: an offset that moved. The previous
                // reading is dropped too, because one of the two is wrong and
                // there is no way to tell which.
                Forget();
                return Unknown($"position_moved_too_far:{travelled:F1}_over_{allowed:F1}");
            }
        }

        _previous = position;
        _previousAtUtc = now;
        return ClassifiedValue<MapPoint>.Live(position, now);
    }

    /// <summary>Whether a value could be a map coordinate at all.</summary>
    public static bool IsPlausibleCoordinate(int value)
        => value >= 0 && value <= MaxPlausibleCoordinate;

    private static double Distance(MapPoint a, MapPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// Drops the remembered reading.
    /// </summary>
    /// <remarks>
    /// Keeping it would make the next reading continuous with a value already
    /// under suspicion, which is how a moved offset settles into looking correct.
    /// </remarks>
    private void Forget()
    {
        _previous = null;
        _previousAtUtc = default;
    }

    private static ClassifiedValue<MapPoint> Unknown(string reason)
        => ClassifiedValue<MapPoint>.Unknown(reason);
}
