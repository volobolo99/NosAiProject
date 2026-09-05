namespace NosAi.Core.WorldModel.Temporal;

/// <summary>
/// Pure, stateless primitives for reasoning about a <see cref="WorldFact{T}"/>
/// across time: how much to still trust an aging observation, and how to
/// derive movement from two of them (docs/agents/AGENT_COMMAND_REGISTRY.md
/// AP-01/A3: "temporal belief, prediction and derived-state algorithms").
///
/// None of this replaces <see cref="WorldFact{T}.IsFresh"/>'s hard
/// fresh/stale cutoff, which stays the sole authority for "is this fact too
/// old to use at all" (that is what
/// <c>NosAi.Runtime.WorldModel.Fusion.FactFusion</c> already gates
/// candidates on). What this type adds is a *continuous* signal within that
/// still-fresh window, and a *derived* kinematic fact computed from two
/// positions -- neither of which the World Model contracts (AP-01/A1) or
/// Sensor Fusion (AP-01/A2) can produce on their own, since A2 only ever
/// sees one observation instant at a time.
///
/// This is deliberately narrower than the canonical pipeline's own
/// "Simulation/Prediction" stage (docs/NOSAI_ARCHITECTURE_BASELINE.md S:2:
/// <c>World Model -&gt; Prediction / Local Simulation -&gt; Utility/Risk
/// Ranking</c>): that stage evaluates hypothetical *action outcomes* and
/// belongs to combat/navigation planning (AP-05/AP-08), which do not exist
/// yet and which this type must not anticipate. <see cref="PredictPosition"/>
/// only extrapolates where an already-tracked entity likely is right now
/// from its last known kinematics -- it never evaluates a candidate action.
/// </summary>
public static class TemporalBelief
{
    /// <summary>
    /// Smoothly reduces <paramref name="fact"/>'s confidence as it ages
    /// toward <paramref name="maxAge"/>, reaching zero exactly at that
    /// boundary. Does not change <see cref="WorldFact{T}.Source"/>,
    /// <see cref="WorldFact{T}.Value"/> or <see cref="WorldFact{T}.ObservedAtUtc"/>:
    /// decay only ever makes a fact easier to outrank in a later
    /// <c>FactFusion.Resolve</c> call, it never invents a different
    /// observation.
    /// </summary>
    /// <param name="fact">The fact to decay. Returned unchanged when already <see cref="WorldFact{T}.HasValue"/> is false, or when its instant is not older than <paramref name="nowUtc"/>, or when it is already beyond <see cref="WorldFact{T}.IsFresh"/>'s cutoff (that boundary is FactFusion's job, not this method's).</param>
    /// <param name="nowUtc">The instant to measure age against.</param>
    /// <param name="maxAge">The same freshness window a caller would pass to <see cref="WorldFact{T}.IsFresh"/>. Must be positive.</param>
    public static WorldFact<T> DecayConfidence<T>(WorldFact<T> fact, DateTime nowUtc, TimeSpan maxAge)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (maxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge), "A decay window must be positive.");

        if (!fact.HasValue)
            return fact;

        TimeSpan age = nowUtc - fact.ObservedAtUtc;
        if (age <= TimeSpan.Zero)
            return fact;
        if (!fact.IsFresh(maxAge, nowUtc))
            return fact;

        double remaining = 1.0 - age.TotalSeconds / maxAge.TotalSeconds;
        double decayed = fact.Confidence * Math.Clamp(remaining, 0d, 1d);
        return fact with { Confidence = decayed };
    }

    /// <summary>
    /// Derives a velocity from two classified positions of the *same*
    /// entity taken at two instants. Always <see cref="DataSourceKind.Derived"/>
    /// -- velocity is never itself observed -- with a confidence no higher
    /// than either input's own confidence, so an estimate built on a shaky
    /// reading cannot look more trustworthy than the readings it came from.
    /// </summary>
    /// <param name="previous">The earlier classified position.</param>
    /// <param name="current">The later classified position.</param>
    /// <param name="maxObservationGap">How far apart the two instants may be and still yield a reliable estimate -- a long gap means the entity could have changed direction many times in between, so the average rate over that gap is not a usable "current" velocity.</param>
    public static WorldFact<WorldVelocity> EstimateVelocity(
        WorldFact<WorldPosition> previous,
        WorldFact<WorldPosition> current,
        TimeSpan maxObservationGap)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        // Every early return below stamps its Unknown result from
        // current.ObservedAtUtc -- an instant already present on one of this
        // method's own inputs -- rather than omitting the argument and
        // letting WorldFact<T>.Unknown fall back to its own default
        // (real DateTime.UtcNow). Omitting it here previously leaked real
        // wall-clock time into a supposedly pure function's result, breaking
        // WorldModelTemporalEnricher's "same inputs, same output" guarantee
        // on exactly the most common case this method exists to handle: an
        // entity's first sighting (AP-01/A5 audit finding).
        if (!previous.HasValue || !current.HasValue)
            return WorldFact<WorldVelocity>.Unknown("insufficient_position_history", current.ObservedAtUtc);

        TimeSpan elapsed = current.ObservedAtUtc - previous.ObservedAtUtc;
        if (elapsed <= TimeSpan.Zero)
            return WorldFact<WorldVelocity>.Unknown("non_increasing_observation_order", current.ObservedAtUtc);
        if (elapsed > maxObservationGap)
            return WorldFact<WorldVelocity>.Unknown("observation_gap_too_large_for_a_reliable_estimate", current.ObservedAtUtc);

        double seconds = elapsed.TotalSeconds;
        var velocity = new WorldVelocity(
            (float)((current.Value.X - previous.Value.X) / seconds),
            (float)((current.Value.Y - previous.Value.Y) / seconds));

        double confidence = Math.Min(previous.Confidence, current.Confidence);
        return WorldFact<WorldVelocity>.Derived(velocity, confidence, current.ObservedAtUtc);
    }

    /// <summary>
    /// Advisory-only extrapolation of where an entity likely is right now,
    /// from its last known position and estimated velocity. Always
    /// <see cref="DataSourceKind.Simulated"/> -- this is never a real
    /// observation and must never be written back over a
    /// <see cref="WorldPosition"/> field that real Sensor Fusion owns
    /// (docs/ROADMAP_ESECUTIVA.md invariant: "Prediction is advisory only").
    /// </summary>
    /// <param name="lastKnown">The most recent real (Live/Derived/Cached) position.</param>
    /// <param name="velocity">The entity's estimated velocity, or Unknown to extrapolate as stationary.</param>
    /// <param name="horizon">How far into the future to extrapolate. Must not be negative.</param>
    /// <param name="asOfUtc">The instant this prediction is being made, used to stamp the predicted instant (<paramref name="asOfUtc"/> + <paramref name="horizon"/>).</param>
    public static WorldFact<WorldPosition> PredictPosition(
        WorldFact<WorldPosition> lastKnown,
        WorldFact<WorldVelocity> velocity,
        TimeSpan horizon,
        DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(lastKnown);
        ArgumentNullException.ThrowIfNull(velocity);
        if (horizon < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(horizon), "A prediction horizon cannot be negative.");

        // Stamped from this method's own asOfUtc parameter -- not omitted --
        // for the same reason as EstimateVelocity's early returns above
        // (AP-01/A5 audit finding): this method already receives an
        // explicit instant, so falling back to WorldFact<T>.Unknown's own
        // DateTime.UtcNow default would leak real wall-clock time into an
        // otherwise pure function's result.
        if (!lastKnown.HasValue)
            return WorldFact<WorldPosition>.Unknown("no_last_known_position_to_extrapolate_from", asOfUtc);

        DateTime predictedAt = asOfUtc + horizon;

        if (!velocity.HasValue)
            return WorldFact<WorldPosition>.Simulated(lastKnown.Value, lastKnown.Confidence, predictedAt, "no_velocity_estimate_available_assumed_stationary");

        double seconds = horizon.TotalSeconds;
        var predicted = new WorldPosition(
            lastKnown.Value.X + velocity.Value.DxPerSecond * (float)seconds,
            lastKnown.Value.Y + velocity.Value.DyPerSecond * (float)seconds);

        double confidence = Math.Min(lastKnown.Confidence, velocity.Confidence);
        return WorldFact<WorldPosition>.Simulated(predicted, confidence, predictedAt);
    }
}
