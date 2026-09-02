// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — Freshness of occupancy at the moment of the act (C-P4)
// ============================================================================
//
// docs/CONTROLLO_PERSONAGGIO_ROADMAP.md P4: "l'atto richiede un'osservazione che
// copra la cella di destinazione entro la soglia dichiarata. Assenza di
// osservazione e osservazione scaduta sono lo stesso ingresso, e non sono
// « libero »."
//
// StaticGeometryLayer already draws the line this builds on: unknown geometry is
// a fact about a place and is answered in space; unknown occupancy is a fact
// about a moment and can only be answered in time. It names the age limit "the
// cycle already enforces before acting" — and no such limit existed. This is it.

using System.Globalization;
using NosAi.Runtime.Autonomy;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// What the runtime had been told about things that move, and when it was told.
/// </summary>
/// <remarks>
/// <para>
/// <b>The stamp is not derived from the entities, and that is the whole point.</b>
/// An empty list cannot say whether the runtime looked and saw nothing or never
/// looked at all, and taking the newest entity sighting as the view's age would
/// answer "never looked" with silence — an empty list would carry no age, and a
/// map that had genuinely emptied would look exactly like a feed that had stopped.
/// So the age of the <i>view</i> is carried separately by whoever refreshes it.
/// </para>
/// <para>
/// A null list is nothing has looked; a null stamp is a feed that did not say when.
/// Both refuse. They are given different names because they need different repairs,
/// not because one of them is closer to being clear.
/// </para>
/// </remarks>
/// <param name="Entities">Tracked entities as of <paramref name="ObservedAtUtc"/>, or null when nothing has looked.</param>
/// <param name="ObservedAtUtc">When the view was last refreshed by the feed, not when something last moved.</param>
public readonly record struct OccupancyView(
    IReadOnlyList<SelectableEntity>? Entities,
    DateTime? ObservedAtUtc);

/// <summary>Whether a destination cell may be treated as clear, and the evidence either way.</summary>
/// <param name="IsClear">True only when a fresh view says nothing that moves is on the cell.</param>
/// <param name="RefusalReason">Which condition failed, named. Null when clear.</param>
/// <param name="Occupancy">
/// What the cell is, in the vocabulary <see cref="StaticGeometryLayer"/> composes with.
/// A refusal for staleness reports <see cref="DynamicOccupancy.Suspected"/> rather than
/// <see cref="DynamicOccupancy.Occupied"/>: not knowing is not a sighting.
/// </param>
/// <param name="ViewAge">How old the view was, or null when it carried no stamp.</param>
public readonly record struct OccupancyVerdict(
    bool IsClear,
    string? RefusalReason,
    DynamicOccupancy Occupancy,
    TimeSpan? ViewAge);

/// <summary>
/// The condition an act has to meet that a plan does not: the destination must be
/// covered by an observation recent enough to be evidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why planning and acting differ here.</b> <see cref="StaticGeometryLayer"/>
/// deliberately lets a planner treat open ground with nothing observed on it as
/// walkable, and gives the reason: the runtime never observes most cells of a map,
/// so blocking on the absence of a sighting would block every cell and return no
/// route, forever. That is right for a route and wrong for a step. A plan is a
/// proposal and is revalidated; a step is emitted, and the claim it needs — <i>this
/// square is clear now</i> — is one a plan never had to make.
/// </para>
/// <para>
/// <b>Two ages, because they answer different questions.</b> The view's age asks
/// whether the runtime is still hearing from the world at all; a sighting's age asks
/// whether one entity's last known square is still where it is. Collapsing them into
/// one number breaks whichever way it is set: at the view's bound every stationary
/// monster becomes a suspicion — a monster that has not moved is not mentioned on
/// the wire, which is the trap <see cref="TargetSelectionPolicy"/> names — and at the
/// sighting's bound a feed that died thirty seconds ago still authorises acts.
/// </para>
/// <para>
/// <b>What an unfresh answer is worth.</b> Nothing, and specifically not "clear".
/// Absence of observation and expired observation reach the caller as the same
/// answer — refuse — and they carry different reason codes only so the operator can
/// see which one to fix.
/// </para>
/// </remarks>
public static class OccupancyFreshness
{
    /// <summary>Reported when nothing has ever looked at the world.</summary>
    public const string NeverObservedReason = "occupancy_never_observed";

    /// <summary>Reported when the view arrived without an instant attached to it.</summary>
    public const string ViewNotStampedReason = "occupancy_view_not_stamped";

    /// <summary>Reported when the view is stamped ahead of now.</summary>
    public const string ViewFromTheFutureReason = "occupancy_view_from_the_future";

    /// <summary>Reported when the view is older than the runtime is willing to act on.</summary>
    public const string ViewStalePrefix = "occupancy_view_stale";

    /// <summary>Reported when a fresh sighting puts something on the destination.</summary>
    public const string DestinationOccupiedPrefix = "occupancy_destination_occupied";

    /// <summary>Reported when a stale sighting last put something on the destination.</summary>
    public const string DestinationSuspectedPrefix = "occupancy_destination_suspected";

    /// <summary>
    /// How old the view may be and still count as hearing from the world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounded above by how long it takes something to walk into the destination: a
    /// character crosses a cell in a few hundred milliseconds, so a second of silence
    /// already covers one to three cells of movement the runtime did not see. Tighter
    /// would be defensible on that argument alone and is not chosen, because the bound
    /// below is a real one: the world feed speaks when the server speaks, and a
    /// threshold under its cadence would refuse every act in a quiet corner of a map.
    /// </para>
    /// <para>
    /// <b>This number has not been measured against a real feed.</b> If the feed turns
    /// out to say nothing at all while nothing moves, this bound will refuse acts in
    /// quiet areas, and the repair is a heartbeat in the feed — a stamp saying "still
    /// connected, still nothing" — not a larger number here. Raising it would buy
    /// permission to act by widening the interval the runtime is blind for, which is
    /// the trade this check exists to refuse.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultMaxViewAge = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// How old one entity's position may be before its square is a suspicion.
    /// </summary>
    /// <remarks>
    /// The same thirty seconds <see cref="TargetSelectionPolicy"/> allows, for the same
    /// reason and not by coincidence: a monster that has stood still has not been
    /// mentioned since it stopped, so a short bound would turn every stationary entity
    /// into a suspicion and block the cells around it. Beyond this the entity may have
    /// walked away, and its last square is neither occupied nor clear.
    /// </remarks>
    public static readonly TimeSpan DefaultMaxSightingAge = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether the destination may be treated as clear, given what was observed and when.
    /// </summary>
    /// <param name="destination">The cell the act would move onto.</param>
    /// <param name="view">What was seen, and when the seeing was last refreshed.</param>
    /// <param name="nowUtc">The instant to measure ages from.</param>
    /// <param name="maxViewAge">Null takes <see cref="DefaultMaxViewAge"/>.</param>
    /// <param name="maxSightingAge">Null takes <see cref="DefaultMaxSightingAge"/>.</param>
    public static OccupancyVerdict Evaluate(
        MapPoint destination,
        in OccupancyView view,
        DateTime nowUtc,
        TimeSpan? maxViewAge = null,
        TimeSpan? maxSightingAge = null)
    {
        TimeSpan viewBound = maxViewAge ?? DefaultMaxViewAge;
        TimeSpan sightingBound = maxSightingAge ?? DefaultMaxSightingAge;

        if (view.Entities is not { } entities)
            return Refuse(NeverObservedReason, DynamicOccupancy.Suspected, null);

        if (view.ObservedAtUtc is not { } observedAt)
            return Refuse(ViewNotStampedReason, DynamicOccupancy.Suspected, null);

        TimeSpan age = nowUtc - observedAt;

        // A stamp ahead of now is two clocks disagreeing, not a very fresh reading.
        // Gate3WorldState.IsActionable takes the same position on the same question.
        if (age < TimeSpan.Zero)
            return Refuse(ViewFromTheFutureReason, DynamicOccupancy.Suspected, age);

        if (age > viewBound)
        {
            return Refuse(
                string.Create(CultureInfo.InvariantCulture,
                    $"{ViewStalePrefix}:{age.TotalMilliseconds:F0}ms_of_{viewBound.TotalMilliseconds:F0}ms"),
                DynamicOccupancy.Suspected,
                age);
        }

        // The view is evidence. Now: is anything standing on the square?
        for (var i = 0; i < entities.Count; i++)
        {
            SelectableEntity entity = entities[i];
            if (entity.At != destination)
                continue;

            TimeSpan sightingAge = nowUtc - entity.ObservedAtUtc;

            // Fresh enough to be a sighting: the square is taken.
            if (sightingAge >= TimeSpan.Zero && sightingAge <= sightingBound)
            {
                return Refuse(
                    string.Create(CultureInfo.InvariantCulture,
                        $"{DestinationOccupiedPrefix}:{entity.EntityId}"),
                    DynamicOccupancy.Occupied,
                    age);
            }

            // Too old to be a sighting, and a named entity was last seen here. It may
            // have walked off; it may not. Suspected blocks, and says which of the two
            // answers this is rather than borrowing the other one's confidence.
            return Refuse(
                string.Create(CultureInfo.InvariantCulture,
                    $"{DestinationSuspectedPrefix}:{entity.EntityId}"),
                DynamicOccupancy.Suspected,
                age);
        }

        return new OccupancyVerdict(true, null, DynamicOccupancy.Clear, age);
    }

    private static OccupancyVerdict Refuse(string reason, DynamicOccupancy occupancy, TimeSpan? age) =>
        new(false, reason, occupancy, age);
}
