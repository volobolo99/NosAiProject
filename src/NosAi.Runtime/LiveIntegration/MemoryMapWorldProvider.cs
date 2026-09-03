using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>
/// One pass over the client's map id and standing cell, each classified on its
/// own so a readable id is not reused as a position.
/// </summary>
/// <param name="MapId">The map the character is on, or why it is not known.</param>
/// <param name="StandingCell">The square the character is on, or why it is not known.</param>
public readonly record struct MapWorldObservation(
    ClassifiedValue<int> MapId,
    ClassifiedValue<MapPoint> StandingCell)
{
    /// <summary>Nothing was read. Both fields carry the same reason.</summary>
    public static MapWorldObservation Unknown(string reason) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<MapPoint>.Unknown(reason));
}

/// <summary>
/// Adds the map id and the standing cell to another provider's observation,
/// from a reader of the client's memory.
/// </summary>
/// <remarks>
/// <para>
/// S5, the half the wire cannot supply. The server never sends the current map
/// id as a standing fact — <c>c_map</c> names a change, and no capture in the
/// archive contains one — and it never sends the player's own position. This
/// decorator is where a reader that can answer both is joined in, using the
/// same two calls <c>--grid-check</c> already makes:
/// <see cref="NosTaleClientLayout.TryReadMapId"/> and
/// <see cref="NosTaleClientLayout.TryReadPlayer"/>.
/// </para>
/// <para>
/// <b>It fills a gap and never overwrites an answer.</b> The same shape as
/// <see cref="MemoryTargetGameplayProvider"/> and
/// <see cref="PositionAwareGameplayProvider"/>. An inner observation that
/// already carries a map id or a standing cell came from a source that is not
/// this one.
/// </para>
/// <para>
/// <b>It does not write <see cref="GameplayObservation.PlayerPosition"/>.</b>
/// That field is what aiming uses, and it stays behind
/// <see cref="MemoryGameplayProvider"/>'s identity, range, map and continuity
/// checks. The standing cell is the grid-check reading: LIVE while the layout
/// chain holds, even when the wire has not yet named this character. Mixing the
/// two would either hide the standing-cell proof or let an unchecked coordinate
/// into the selector.
/// </para>
/// <para>
/// A reader that throws costs the map world, not the observation: the vitals in
/// the inner reading are still real. The exception's type is carried in the
/// reason so the fault is visible where the map id and the standing cell are.
/// </para>
/// </remarks>
public sealed class MemoryMapWorldProvider : IGameplayProvider
{
    /// <summary>Reported when the client's memory could not be reached at all.</summary>
    public const string SessionUnavailableReason = "map_world_session_unavailable";

    /// <summary>
    /// Reported when a reader throws. The inner observation is kept; only the
    /// map world is named as failed.
    /// </summary>
    public const string ReaderFailedPrefix = "map_world_reader_failed";

    /// <summary>
    /// The snapshot's own token for a missing client PID, reused so a panel
    /// that already shows <c>process_not_attached</c> does not grow a second
    /// spelling of the same fact.
    /// </summary>
    public const string ProcessNotAttachedReason = "process_not_attached";

    /// <summary>Attach succeeded but the map-id read did not name its own refusal.</summary>
    public const string MapIdUnreadable = "map_id_unreadable";

    /// <summary>Attach succeeded but the player object did not name its own refusal.</summary>
    public const string PlayerUnreadable = "player_object_unreadable";

    private readonly IGameplayProvider _inner;
    private readonly Func<MapWorldObservation> _read;

    /// <param name="inner">The provider that reads everything else.</param>
    /// <param name="read">
    /// One pass over map id and standing cell. A delegate rather than a session
    /// so this can be exercised without a client, and so a runtime whose attach
    /// has gone away reports UNKNOWN instead of holding a dead handle.
    /// </param>
    public MemoryMapWorldProvider(IGameplayProvider inner, Func<MapWorldObservation> read)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _read = read ?? throw new ArgumentNullException(nameof(read));
    }

    /// <inheritdoc />
    public string Name => $"{_inner.Name}+map-world";

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        GameplayObservation observation = _inner.Observe();

        MapWorldObservation world;
        try
        {
            world = _read();
        }
        catch (Exception ex)
        {
            string reason = $"{ReaderFailedPrefix}:{ex.GetType().Name}";
            world = MapWorldObservation.Unknown(reason);
        }

        if (observation.MapId.HasValue && observation.StandingCell.HasValue)
            return observation;

        return observation with
        {
            MapId = observation.MapId.HasValue ? observation.MapId : world.MapId,
            StandingCell = observation.StandingCell.HasValue ? observation.StandingCell : world.StandingCell,
        };
    }
}
