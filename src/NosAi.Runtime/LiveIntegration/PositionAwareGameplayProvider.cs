using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;

namespace NosAi.LiveIntegration;

/// <summary>
/// Adds the character's own position to another provider's observation, from a
/// reader of the client's memory.
/// </summary>
/// <remarks>
/// <para>
/// C1-1, the half the wire cannot supply. The server never sends the player's
/// position — every <c>mv</c> in 117 KB of capture is another entity, because
/// position is client-authoritative (docs/PROTOCOLLO_NOSTALE.md) — so
/// <see cref="NetworkGameplayProvider"/> publishes it UNKNOWN with
/// <c>player_position_not_on_wire</c>, and this decorator is where a reader that
/// can answer is joined in. It is the same shape as
/// <see cref="TargetAwareGameplayProvider"/>, for the same reason: the two
/// sources are genuinely separate, and a runtime that has no memory reader
/// simply does not wrap the network provider and keeps the UNKNOWN it had.
/// </para>
/// <para>
/// <b>Until a reader is bound, the position stays UNKNOWN with its own reason.</b>
/// Not the map origin, not the last known square: the position is what a click
/// is aimed from, and an unknown origin silently treated as (0, 0) aims at a
/// real point on screen. The reader itself is
/// <see cref="MemoryGameplayProvider"/>, which is LIVE only while its identity,
/// range, map and continuity checks all hold and UNKNOWN with the failing check's
/// reason the moment one gives; this decorator adds no check of its own and
/// removes none. Binding it into the running host is a separate piece of work,
/// because the host is where the wire's own id
/// (<c>NetworkWorldFeed.PlayerEntityId</c>) and the reader meet.
/// </para>
/// <para>
/// A reader that throws costs the position, not the observation: the vitals in
/// the inner reading are still real, and turning them UNKNOWN because a pointer
/// chain broke would hide a good HP behind an unrelated fault. The exception's
/// type is carried in the reason so the fault is visible where the position is.
/// </para>
/// </remarks>
public sealed class PositionAwareGameplayProvider : IGameplayProvider
{
    private readonly IGameplayProvider _inner;
    private readonly IPlayerPositionProvider _position;

    /// <param name="inner">The provider that reads everything else.</param>
    /// <param name="position">The reader of the character's own position.</param>
    public PositionAwareGameplayProvider(IGameplayProvider inner, IPlayerPositionProvider position)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _position = position ?? throw new ArgumentNullException(nameof(position));
    }

    /// <inheritdoc />
    public string Name => $"{_inner.Name}+player-position";

    /// <inheritdoc />
    public GameplayObservation Observe()
    {
        GameplayObservation observation = _inner.Observe();

        // A position the inner provider established stands: this fills a gap and
        // never overrides an answer. Nothing on the wire supplies one today.
        if (observation.PlayerPosition.HasValue)
            return observation;

        ClassifiedValue<MapPoint> position;
        try
        {
            position = _position.ReadPosition();
        }
        catch (Exception ex)
        {
            position = ClassifiedValue<MapPoint>.Unknown($"player_position_reader_failed:{ex.GetType().Name}");
        }

        return observation with { PlayerPosition = position };
    }
}
