namespace NosAi.Runtime.Observability;

/// <summary>
/// A clock that follows a recording's own timestamps instead of the system
/// clock, for <c>--decide-replay --as-of-capture</c>. It answers "what would the
/// runtime have decided while this was happening", not "is this reading current
/// now" — which, on a recording, is always no (ADR-0016).
/// </summary>
/// <remarks>
/// <para>
/// It never runs on its own: it advances only when a packet advances it, and it
/// never moves backwards, so an out-of-order timestamp cannot make an older
/// reading look newer than one already seen. Before the first packet it answers
/// with that first packet's instant, not <see cref="DateTime.UtcNow"/>, so a
/// freshness rule judging against it is judging against the capture, not against
/// the operator's clock.
/// </para>
/// <para>
/// The system clock and this clock answer different questions, and the
/// difference is the point of <c>--as-of-capture</c>: the system clock says every
/// recorded reading is months old and therefore stale; this one says a reading is
/// stale only when the wire itself went quiet for longer than
/// <c>NetworkGameplayProvider.DefaultMaxVitalsAge</c>.
/// </para>
/// </remarks>
public sealed class CaptureClock : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    /// <param name="firstPacketInstant">The instant of the first packet in the capture.</param>
    public CaptureClock(DateTimeOffset firstPacketInstant)
    {
        _now = firstPacketInstant.ToUniversalTime();
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <summary>
    /// Moves the clock to <paramref name="instant"/> unless that goes backwards.
    /// An out-of-order timestamp never returns the clock to an earlier time.
    /// </summary>
    public void Advance(DateTimeOffset instant)
    {
        DateTimeOffset utc = instant.ToUniversalTime();
        lock (_gate)
        {
            if (utc > _now)
                _now = utc;
        }
    }
}
