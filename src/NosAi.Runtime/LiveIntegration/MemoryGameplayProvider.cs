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
/// lifting its argument. A wrong pointer chain does not fail: it returns readable
/// bytes and a plausible number. The classification is only honest if something
/// can tell the two apart, so the checks <b>are</b> the provider — reading the
/// bytes is the easy half.
/// </para>
/// <para>
/// Four checks, in the order that costs least to disprove:
/// </para>
/// <list type="number">
/// <item><b>Identity.</b> The character id the client holds must equal the id the
/// <i>server</i> sent on the wire. Two independent sources agreeing on one number
/// is the strongest thing available here, and it is the one check a wrong pointer
/// chain cannot pass by luck: a stray address yielding a plausible coordinate
/// will not also yield this session's character id.</item>
/// <item><b>Range.</b> NosTale coordinates run to two and three digits — the
/// captures show <c>121 110</c> and <c>109 63</c>. Outside the bound is a
/// different field, not a distant character.</item>
/// <item><b>Map coherence.</b> When the current map is known, a coordinate
/// outside it is not a position.</item>
/// <item><b>Continuity.</b> A step larger than the speed from <c>cond</c> allows
/// in the elapsed time is a pointer that moved, not somebody who ran.</item>
/// </list>
/// <para>
/// <c>LIVE</c> while all of them hold; <c>UNKNOWN</c> with the failing check's own
/// reason the moment one gives. <b>Never the last good value</b> — the case
/// ADR-0014 names in full, because a retained coordinate is exactly what makes a
/// broken chain invisible.
/// </para>
/// </remarks>
public sealed class MemoryGameplayProvider : IPlayerPositionProvider
{
    /// <summary>
    /// The largest coordinate a NosTale map is taken to have.
    /// </summary>
    /// <remarks>
    /// Deliberately loose. This rejects a field that is not a coordinate at all;
    /// policing the edges of a map is the map-bounds check's job. The client
    /// stores both coordinates as <c>uint16</c>, so the type alone already rules
    /// out anything above 65535.
    /// </remarks>
    public const int MaxPlausibleCoordinate = 1000;

    /// <summary>Tiles per second at speed 1, used to turn <c>cond</c> into a bound.</summary>
    /// <remarks>
    /// Deliberately generous. This catches a reading that jumped across the
    /// address space, which is orders of magnitude out, not a character moving
    /// slightly faster than expected — and a tight bound would reject a real
    /// character after a lag spike.
    /// </remarks>
    public const double TilesPerSecondPerSpeedUnit = 1.0;

    /// <summary>Allowed on top of speed × elapsed, for a reading either side of a jitter.</summary>
    public const double ContinuitySlackTiles = 4.0;

    private readonly Func<ProcessMemoryReader?> _reader;
    private readonly Func<(IntPtr Base, long Size)?> _module;
    private readonly Func<long?> _expectedCharacterId;
    private readonly Func<int?> _movementSpeed;
    private readonly Func<MapBounds?> _mapBounds;
    private readonly TimeProvider _clock;

    private NosTaleClientLayout? _layout;
    private MapPoint? _previous;
    private DateTime _previousAtUtc;

    /// <param name="reader">
    /// An open read handle to the client, or null when it is not attached.
    /// </param>
    /// <param name="module">
    /// Base and size of the client's main module, re-read because ASLR moves it
    /// on every start of the client.
    /// </param>
    /// <param name="expectedCharacterId">
    /// The character id the server sent, normally
    /// <c>NetworkObservationReport.PlayerEntityId</c>. Null until the wire has
    /// named it, and null means this reading cannot be confirmed — which is a
    /// refusal, not a reason to skip the check.
    /// </param>
    /// <param name="movementSpeed">
    /// The character's speed from <c>cond</c>, or null when nothing has reported
    /// it. Null is not "stationary": without it the continuity check cannot run.
    /// </param>
    /// <param name="mapBounds">
    /// The current map's limits, or null when the map is not known. The card makes
    /// this check conditional in so many words, so an unknown map skips it.
    /// </param>
    public MemoryGameplayProvider(
        Func<ProcessMemoryReader?> reader,
        Func<(IntPtr Base, long Size)?> module,
        Func<long?> expectedCharacterId,
        Func<int?>? movementSpeed = null,
        Func<MapBounds?>? mapBounds = null,
        TimeProvider? clock = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _expectedCharacterId = expectedCharacterId ?? throw new ArgumentNullException(nameof(expectedCharacterId));
        _movementSpeed = movementSpeed ?? (static () => null);
        _mapBounds = mapBounds ?? (static () => null);
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ClassifiedValue<MapPoint> ReadPosition()
    {
        if (_reader() is not { } reader)
        {
            Forget();
            return Unknown("client_not_attached");
        }

        if (_module() is not { } module)
        {
            Forget();
            return Unknown("client_module_not_located");
        }

        // Resolved once and kept: the signature is in the image, which does not
        // move while the process lives. The pointer chain behind it is followed
        // fresh on every read, because that does move.
        if (_layout is null
            && !NosTaleClientLayout.TryResolve(
                reader, module.Base, module.Size, out _layout, out string? resolveFailure))
        {
            Forget();
            return Unknown(resolveFailure ?? "player_manager_not_resolved");
        }

        DateTime now = _clock.GetUtcNow().UtcDateTime;

        if (!_layout!.TryReadPlayer(reader, out PlayerObjectReading player, out string? readFailure))
        {
            // A broken chain invalidates the layout as well: the next read
            // re-scans rather than following a pointer that has stopped meaning
            // what it meant.
            _layout = null;
            Forget();
            return Unknown(readFailure ?? "player_object_unreadable");
        }

        // 1. Identity, first, because it is the check a wrong chain cannot pass by
        //    luck and it costs one comparison.
        if (_expectedCharacterId() is not { } expectedId)
        {
            Forget();
            return Unknown("character_id_not_observed_on_wire");
        }

        if (player.CharacterId != expectedId)
        {
            _layout = null;
            Forget();
            return Unknown($"character_id_mismatch:{player.CharacterId}_not_{expectedId}");
        }

        // 2. Range.
        if (!IsPlausibleCoordinate(player.X) || !IsPlausibleCoordinate(player.Y))
        {
            Forget();
            return Unknown($"position_out_of_range:{player.X},{player.Y}");
        }

        var position = new MapPoint(player.X, player.Y);

        // 3. Map coherence, before continuity because it needs no history.
        if (_mapBounds() is { } bounds && !bounds.Contains(player.X, player.Y))
        {
            Forget();
            return Unknown($"position_outside_map:{player.X},{player.Y}");
        }

        // 4. Continuity, against the previous accepted reading.
        if (_previous is { } previous)
        {
            if (_movementSpeed() is not { } speed)
            {
                // The check cannot run, so it has not passed. A reading validated
                // by three checks out of four is not LIVE by ADR-0014's bar.
                Forget();
                return Unknown("movement_speed_unknown");
            }

            double elapsedSeconds = Math.Max(0, (now - _previousAtUtc).TotalSeconds);
            double allowed = (speed * TilesPerSecondPerSpeedUnit * elapsedSeconds) + ContinuitySlackTiles;
            double travelled = Distance(previous, position);

            if (travelled > allowed)
            {
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
    /// under suspicion, which is how a broken chain settles into looking correct.
    /// </remarks>
    private void Forget()
    {
        _previous = null;
        _previousAtUtc = default;
    }

    private static ClassifiedValue<MapPoint> Unknown(string reason)
        => ClassifiedValue<MapPoint>.Unknown(reason);
}
