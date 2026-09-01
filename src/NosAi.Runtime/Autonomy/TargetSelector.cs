using System.Globalization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Autonomy;

/// <summary>
/// One entity the runtime could aim at, as it was last observed.
/// </summary>
/// <param name="HpRatio">
/// Null when nothing has reported this entity's health — a <c>mv</c> carries a
/// position and no health, and most sightings are moves. Null is not full health
/// and it is not zero; it excludes the entity from rules that read health and
/// from none of the others.
/// </param>
/// <param name="ObservedAtUtc">
/// When the position was last stated. It travels with the entity because a
/// selection acts on <i>where it was</i>: the click lands on a square, and a
/// square a monster has walked off is empty ground.
/// </param>
public readonly record struct SelectableEntity(
    long EntityId,
    MapPoint At,
    double? HpRatio,
    DateTime ObservedAtUtc);

/// <summary>What the operator considers worth aiming at.</summary>
/// <param name="MaxRangeTiles">
/// How far away an entity may be and still be chosen. Not a claim about attack
/// range: it keeps the choice to entities the client is plausibly still drawing,
/// because one that is not on screen projects outside the client area and the
/// click is refused anyway.
/// </param>
/// <param name="MaxSightingAge">
/// How old a position may be. Generous on purpose — a monster that has stood
/// still has not been mentioned on the wire for as long as it has stood there,
/// and rejecting it would make every stationary monster unselectable. The bound
/// exists for the other case: an entity that walked away, whose last known square
/// is now empty ground.
/// </param>
public sealed record TargetSelectionPolicy(
    double MaxRangeTiles = 12.0,
    TimeSpan? MaxSightingAge = null)
{
    /// <summary>The default nobody has tuned against a real fight yet.</summary>
    public static TargetSelectionPolicy Default { get; } = new();

    /// <summary>The age bound, with the default filled in.</summary>
    public TimeSpan EffectiveMaxSightingAge => MaxSightingAge ?? TimeSpan.FromSeconds(30);
}

/// <summary>Which entity was chosen, and the sentence explaining why.</summary>
public sealed record TargetChoice(SelectableEntity Entity, double DistanceTiles, string Rationale);

/// <summary>
/// Chooses which entity to aim at, from what has actually been observed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> The planner could say <i>there is a target</i> and
/// never <i>which one</i>: every entity candidate it built carried
/// <see cref="ActionTarget.Entity.Unidentified"/>, and the effector refused them
/// all with <c>target_entity_unresolved</c>. So the loop could attack a target
/// somebody else had selected and could never select one itself. This is the
/// missing step — it turns sightings into a named entity with a square, which is
/// what an aimed click needs.
/// </para>
/// <para>
/// <b>Nearest, and deterministic.</b> The nearest entity is the shortest walk and
/// the one most likely to be drawn on screen at all. Ties break on the lower
/// entity id rather than on list order, because a planner that alternates between
/// two equidistant monsters on successive cycles commits to neither and fights
/// nothing.
/// </para>
/// <para>
/// <b>Every rejection is named.</b> Not selecting is an ordinary outcome — an
/// empty map, everything out of range, positions too old to trust — and each one
/// says which, because "no target" and "the sightings are stale" call for
/// different things from the operator.
/// </para>
/// <para>
/// <b>Where the sightings come from matters.</b> These are the wire's, and the
/// wire mentions an entity only when something happens to it. A capture that
/// started mid-session held 25 <c>in</c> against 7685 <c>mv</c>, so anything
/// already standing on screen when the runtime attached is invisible here until
/// it moves. The client's own entity lists have all of them at once; reading them
/// is blocked on confirming the scene manager
/// (<c>scene_manager_not_confirmed</c>), and until then this selects from a
/// partial view and the age bound is what keeps that honest.
/// </para>
/// </remarks>
public static class TargetSelector
{
    /// <summary>No entity has been observed at all.</summary>
    public const string NothingObservedReason = "no_entities_observed";

    /// <summary>Where the character stands is unknown, so nothing has a distance.</summary>
    public const string PlayerPositionUnknownReason = "player_position_unknown";

    /// <summary>
    /// Picks the entity to aim at, or says why none was picked.
    /// </summary>
    /// <param name="observed">
    /// Everything seen, in any order and including entities that are dead, far
    /// away or long stale. Filtering is this method's job precisely so that the
    /// reason for excluding each one is stated here rather than lost upstream.
    /// </param>
    /// <param name="playerPosition">
    /// Where the character is. Unknown is a refusal: without it nothing has a
    /// distance, and treating it as the map origin would make the farthest entity
    /// look like the nearest.
    /// </param>
    public static bool TrySelect(
        IReadOnlyList<SelectableEntity> observed,
        ClassifiedValue<MapPoint> playerPosition,
        DateTime nowUtc,
        TargetSelectionPolicy policy,
        out TargetChoice? choice,
        out string failureReason)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(playerPosition);
        ArgumentNullException.ThrowIfNull(policy);

        choice = null;

        if (!playerPosition.HasValue)
        {
            failureReason = playerPosition.FailureReason is { Length: > 0 } why
                ? $"{PlayerPositionUnknownReason}:{why}"
                : PlayerPositionUnknownReason;
            return false;
        }

        if (observed.Count == 0)
        {
            failureReason = NothingObservedReason;
            return false;
        }

        MapPoint from = playerPosition.Value;
        TimeSpan maxAge = policy.EffectiveMaxSightingAge;

        SelectableEntity? best = null;
        double bestDistance = double.MaxValue;
        var stale = 0;
        var dead = 0;
        var outOfRange = 0;
        double nearestOutOfRange = double.MaxValue;

        foreach (SelectableEntity entity in observed)
        {
            // A known-dead entity is not a target. Unknown health is not death and
            // does not exclude anything: most sightings are moves, which carry no
            // health at all, and skipping them would leave almost nothing.
            if (entity.HpRatio is <= 0)
            {
                dead++;
                continue;
            }

            if (nowUtc - entity.ObservedAtUtc > maxAge)
            {
                stale++;
                continue;
            }

            double distance = Distance(from, entity.At);
            if (distance > policy.MaxRangeTiles)
            {
                outOfRange++;
                nearestOutOfRange = Math.Min(nearestOutOfRange, distance);
                continue;
            }

            if (best is null
                || distance < bestDistance
                || (distance == bestDistance && entity.EntityId < best.Value.EntityId))
            {
                best = entity;
                bestDistance = distance;
            }
        }

        if (best is not { } chosen)
        {
            // Which exclusion emptied the list is the useful part: an operator who
            // is told "nothing in range" walks somewhere, and one told "every
            // position is stale" knows the observation channel is behind.
            // Invariant, because a refusal reason is an identifier that is matched
            // and logged, not prose. Left to the current culture it reads
            // "no_entity_in_range:40,0_..." on an Italian machine and
            // "...:40.0_..." on an English one, so the same refusal would be two
            // different strings depending on who ran it.
            failureReason = (outOfRange, stale, dead) switch
            {
                ( > 0, _, _) => string.Create(
                    CultureInfo.InvariantCulture,
                    $"no_entity_in_range:{nearestOutOfRange:F1}_of_{policy.MaxRangeTiles:F0}_tiles"),
                (0, > 0, _) => $"all_sightings_stale:{stale}",
                (0, 0, > 0) => $"all_observed_entities_dead:{dead}",
                _ => NothingObservedReason,
            };
            return false;
        }

        string health = chosen.HpRatio is { } ratio
            ? string.Create(CultureInfo.InvariantCulture, $"{ratio * 100:F0}% vita")
            : "vita non nota";
        choice = new TargetChoice(
            chosen,
            bestDistance,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Bersaglio piu' vicino: entita' {chosen.EntityId} a {bestDistance:F1} caselle, {health}"));
        failureReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Straight-line distance in tiles.
    /// </summary>
    /// <remarks>
    /// Geometric rather than a movement cost. How this client counts a diagonal
    /// step has not been measured, and a wrong movement metric would silently
    /// reorder the candidates; a straight line assumes nothing about walking and
    /// is enough to answer "which of these is closest".
    /// </remarks>
    private static double Distance(MapPoint a, MapPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
