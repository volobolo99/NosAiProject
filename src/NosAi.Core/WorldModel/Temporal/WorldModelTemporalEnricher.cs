namespace NosAi.Core.WorldModel.Temporal;

/// <summary>
/// Applies <see cref="TemporalBelief"/> across two consecutive Sensor Fusion
/// cycles (AP-01/A2's <c>GameplayObservationProjector</c> output), producing
/// a <see cref="WorldModelSnapshot"/> whose positional facts carry
/// continuously-decayed confidence and whose entities carry an estimated
/// <see cref="WorldVelocity"/> -- the "derived-state" half of AP-01/A3.
///
/// Pure and stateless: the caller (a future runtime loop, AP-01/A4's job)
/// owns remembering the previous snapshot and supplying it back in; this
/// type never holds state of its own, so the same two snapshots always
/// enrich to the same result (AP-01's "replay deterministico" requirement).
/// </summary>
public static class WorldModelTemporalEnricher
{
    /// <param name="previous">The snapshot from the prior fusion cycle. Pass <see cref="WorldModelSnapshot.Unknown"/> on the very first cycle -- every entity will then correctly derive an Unknown velocity, having no prior sighting to compare against.</param>
    /// <param name="current">The snapshot Sensor Fusion just produced for this cycle. Its own facts are the ones enriched; <paramref name="current"/> itself is never mutated (it is an immutable record).</param>
    /// <param name="nowUtc">The instant confidence decay is measured against.</param>
    /// <param name="maxAge">The freshness window passed to <see cref="TemporalBelief.DecayConfidence{T}"/>.</param>
    /// <param name="maxObservationGap">The maximum gap passed to <see cref="TemporalBelief.EstimateVelocity"/>.</param>
    public static WorldModelSnapshot Enrich(
        WorldModelSnapshot previous,
        WorldModelSnapshot current,
        DateTime nowUtc,
        TimeSpan maxAge,
        TimeSpan maxObservationGap)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        Player enrichedPlayer = current.Player with
        {
            Position = TemporalBelief.DecayConfidence(current.Player.Position, nowUtc, maxAge),
            Velocity = TemporalBelief.EstimateVelocity(previous.Player.Position, current.Player.Position, maxObservationGap)
        };

        EquatableArray<Mob> enrichedMobs = EnrichMobs(previous.Mobs, current.Mobs, nowUtc, maxAge, maxObservationGap);

        return current with
        {
            Player = enrichedPlayer,
            Mobs = enrichedMobs
        };
    }

    private static EquatableArray<Mob> EnrichMobs(
        EquatableArray<Mob> previous,
        EquatableArray<Mob> current,
        DateTime nowUtc,
        TimeSpan maxAge,
        TimeSpan maxObservationGap)
    {
        if (current.Count == 0)
            return current;

        var previousById = new Dictionary<EntityId, Mob>(previous.Count);
        foreach (Mob mob in previous)
            previousById[mob.Id] = mob;

        var enriched = new Mob[current.Count];
        for (int i = 0; i < current.Count; i++)
        {
            Mob mob = current[i];
            WorldFact<WorldPosition> decayedPosition = TemporalBelief.DecayConfidence(mob.Position, nowUtc, maxAge);
            // Stamped from mob.Position.ObservedAtUtc -- one of this method's
            // own inputs -- rather than omitted, for the same reason as
            // TemporalBelief's early returns (AP-01/A6): omitting it would
            // leak real DateTime.UtcNow into a supposedly pure function's
            // result (AP-02/A5 audit finding).
            WorldFact<WorldVelocity> velocity = previousById.TryGetValue(mob.Id, out Mob? matched)
                ? TemporalBelief.EstimateVelocity(matched.Position, mob.Position, maxObservationGap)
                : WorldFact<WorldVelocity>.Unknown("no_prior_sighting_of_this_entity", mob.Position.ObservedAtUtc);

            enriched[i] = mob with
            {
                Position = decayedPosition,
                Velocity = velocity,
                IsHostile = CarryHostilityForward(mob.IsHostile, matched)
            };
        }

        return EquatableArray<Mob>.From(enriched);
    }

    /// <summary>
    /// Keeps an already-established hostility when the current cycle did not
    /// re-establish it, as an explicitly <see cref="DataSourceKind.Cached"/>
    /// fact rather than a fresh one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sensor Fusion can only establish a mob's hostility from the wire's
    /// record of it having hit the character, and the wire names one
    /// aggressor: the most recent. Without this, a mob would be hostile for
    /// exactly the one cycle in which it struck, and unknown again the next --
    /// so every consumer would see the fact flicker rather than hold.
    /// </para>
    /// <para>
    /// <b>Only ever carried forward, never invented and never reversed.</b>
    /// A current cycle that states hostility wins outright, because it is the
    /// fresher observation. A prior Unknown carries nothing: this widens no
    /// claim, it only stops one already made from being dropped. And nothing
    /// here ever turns a hostility into <see langword="false"/> -- an entity
    /// that fought is one that fights, and a later cycle simply not seeing it
    /// hit anyone is not evidence to the contrary.
    /// </para>
    /// <para>
    /// The instant stays the <b>original</b> observation's, never
    /// <paramref name="previous"/>'s own re-stamping and never now, so the
    /// fact keeps ageing across cycles instead of looking freshly observed on
    /// every one -- that age is what a freshness gate reads. The confidence is
    /// kept as observed for the same reason: a mob does not become less
    /// hostile with time, only harder to still find, which is the position's
    /// decay to express.
    /// </para>
    /// </remarks>
    private static WorldFact<bool> CarryHostilityForward(WorldFact<bool> current, Mob? previous)
    {
        if (current.HasValue || previous is null || !previous.IsHostile.HasValue)
            return current;

        return WorldFact<bool>.Cached(
            previous.IsHostile.Value,
            previous.IsHostile.Confidence,
            previous.IsHostile.ObservedAtUtc,
            previous.IsHostile.Reason ?? "hostility_established_in_an_earlier_cycle");
    }
}
