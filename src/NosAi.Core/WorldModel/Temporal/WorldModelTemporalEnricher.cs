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

            enriched[i] = mob with { Position = decayedPosition, Velocity = velocity };
        }

        return EquatableArray<Mob>.From(enriched);
    }
}
