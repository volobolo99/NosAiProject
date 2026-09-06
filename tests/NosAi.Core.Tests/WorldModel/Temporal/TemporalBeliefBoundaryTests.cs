using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Temporal;

/// <summary>
/// AP-01/A5 independent audit: overflow/NaN/Infinity coverage for
/// <see cref="TemporalBelief"/> beyond A3's own <c>TemporalBeliefTests.cs</c>,
/// specifically the categories named by the AP-01/A5 command
/// ("overflow/NaN/infinito su float/double (posizioni, confidence,
/// velocità)"). These are downstream CONSEQUENCES of ordinary IEEE-754
/// float/double arithmetic that neither <see cref="TemporalBelief"/> nor
/// <see cref="WorldFact{T}"/> ever checks for non-finite results; they are
/// recorded here as accepted, pinned-down behaviour rather than as new
/// separate defects, to keep this audit's set of intentionally-failing
/// tests limited to the two already reported in
/// docs/agents/phases/AP-01/AP-01_A5_AUDIT.md (the NaN-confidence clamp gap
/// in <c>WorldFact&lt;T&gt;</c> and its consequence in <c>FactFusion</c>).
/// </summary>
public sealed class TemporalBeliefBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// <c>float.MaxValue - (-float.MaxValue)</c> overflows float's range and
    /// becomes <see cref="float.PositiveInfinity"/> (confirmed against the
    /// real runtime during this audit) -- ordinary float arithmetic, not a
    /// bug introduced by <see cref="TemporalBelief"/>. <see cref="TemporalBelief.EstimateVelocity"/>
    /// does not detect or reject the resulting Infinity: it is returned as
    /// an ordinary <see cref="DataSourceKind.Derived"/>, <c>HasValue ==
    /// true</c> fact, exactly as if it were a normal finite velocity.
    /// </summary>
    [Fact]
    public void EstimateVelocity_ExtremeOppositeSignPositions_OverflowsToInfinity_NotFlaggedAsUnknown()
    {
        WorldFact<WorldPosition> previous = WorldFact<WorldPosition>.Live(new WorldPosition(-float.MaxValue, 0), 1.0, Now);
        WorldFact<WorldPosition> current = WorldFact<WorldPosition>.Live(new WorldPosition(float.MaxValue, 0), 1.0, Now + TimeSpan.FromSeconds(1));

        WorldFact<WorldVelocity> velocity = TemporalBelief.EstimateVelocity(previous, current, TimeSpan.FromSeconds(5));

        Assert.True(velocity.HasValue);
        Assert.Equal(DataSourceKind.Derived, velocity.Source);
        Assert.True(float.IsPositiveInfinity(velocity.Value.DxPerSecond));
    }

    /// <summary>
    /// <c>Infinity * 0 == NaN</c> under IEEE-754 (confirmed against the
    /// real runtime). Reachable in one call from an Infinite velocity (see
    /// the overflow above) combined with a zero <c>horizon</c> -- both
    /// individually valid, in-contract inputs to
    /// <see cref="TemporalBelief.PredictPosition"/> (a zero horizon is
    /// explicitly allowed; only a negative one throws).
    /// <see cref="TemporalBelief.PredictPosition"/> does not detect or
    /// reject the resulting NaN: it is returned as an ordinary
    /// <see cref="DataSourceKind.Simulated"/>, <c>HasValue == true</c> fact
    /// whose position silently contains a NaN coordinate.
    /// </summary>
    [Fact]
    public void PredictPosition_InfiniteVelocityWithAZeroHorizon_ProducesNaN_InfinityTimesZero()
    {
        WorldFact<WorldVelocity> infiniteVelocity = WorldFact<WorldVelocity>.Derived(
            new WorldVelocity(float.PositiveInfinity, 0), 1.0, Now);
        WorldFact<WorldPosition> lastKnown = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);

        WorldFact<WorldPosition> predicted = TemporalBelief.PredictPosition(lastKnown, infiniteVelocity, TimeSpan.Zero, Now);

        Assert.True(predicted.HasValue);
        Assert.True(float.IsNaN(predicted.Value.X));
    }

    // ---- Known defect: Unknown() early returns leak real wall-clock time --

    /// <summary>
    /// KNOWN A3 DEFECT (src/NosAi.Core/WorldModel/Temporal/TemporalBelief.cs,
    /// <c>EstimateVelocity</c>): all three of this method's early-return
    /// <c>WorldFact&lt;WorldVelocity&gt;.Unknown(reason)</c> calls -- for
    /// "insufficient_position_history", "non_increasing_observation_order"
    /// and "observation_gap_too_large_for_a_reliable_estimate" -- omit the
    /// optional <c>observedAtUtc</c> argument, so
    /// <see cref="WorldFact{T}.Unknown"/> falls back to its own default,
    /// <c>DateTime.UtcNow</c> (REAL wall-clock time), instead of any instant
    /// derived from this method's own inputs (e.g. <c>current.ObservedAtUtc</c>,
    /// the convention every other <c>Unknown(...)</c> call site in this
    /// codebase already follows -- see <c>GameplayObservationProjector</c>,
    /// which never omits the argument).
    ///
    /// This directly breaks <see cref="WorldModelTemporalEnricher"/>'s own
    /// documented guarantee ("the same two snapshots always enrich to the
    /// same result") for the single most common case this method exists to
    /// handle: an entity's FIRST sighting, which always takes the
    /// "insufficient_position_history" branch. Discovered empirically while
    /// writing
    /// <c>WorldModelTemporalEnricherDeterminismAndDuplicateIdTests.ThreeConsecutiveCycles_ReplayedTwice_ProduceBitForBitEqualFinalSnapshots</c>
    /// in this same audit: an early draft of that test left the Player's
    /// position Unknown throughout and intermittently failed with a
    /// snapshot-equality diff that gave no useful detail (<c>EquatableArray&lt;T&gt;</c>'s
    /// default <c>ToString</c> does not print its elements), because the two
    /// otherwise-identical runs each captured a different real wall-clock
    /// instant for that one Unknown velocity fact. That test was corrected
    /// to give the Player a known position instead (sidestepping this
    /// defect); this test isolates and pins down the defect itself, on the
    /// primitive directly.
    ///
    /// INTENTIONALLY FAILING until fixed -- do not delete/weaken/skip. Fix
    /// direction (left for A6/A3, out of A5's file ownership): pass
    /// <c>current.ObservedAtUtc</c> explicitly to all three
    /// <c>WorldFact&lt;WorldVelocity&gt;.Unknown(...)</c> calls in this
    /// method.
    /// </summary>
    [Fact]
    public void EstimateVelocity_InsufficientHistoryResult_LeaksRealWallClockTime_InsteadOfADeterministicInstant_KnownA3RobustnessGap()
    {
        // Timestamped far in the future so a real wall-clock leak is
        // unmistakable: a deterministic implementation stamping this
        // Unknown result from its own inputs (e.g. current.ObservedAtUtc)
        // would also report a year-2099 instant; the real implementation
        // does not.
        var farFuture = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        WorldFact<WorldPosition> unknownPrevious = WorldFact<WorldPosition>.Unknown("no_prior_sighting", farFuture);
        WorldFact<WorldPosition> unknownCurrent = WorldFact<WorldPosition>.Unknown("no_prior_sighting", farFuture);

        WorldFact<WorldVelocity> velocity = TemporalBelief.EstimateVelocity(unknownPrevious, unknownCurrent, TimeSpan.FromSeconds(5));

        Assert.True(velocity.ObservedAtUtc.Year > 2050,
            $"Expected a deterministic instant derived from the method's own inputs (year 2099); " +
            $"got {velocity.ObservedAtUtc:O}, which is real wall-clock time (DateTime.UtcNow) leaking " +
            $"through WorldFact<WorldVelocity>.Unknown(reason)'s default observedAtUtc parameter.");
    }

    /// <summary>
    /// Same root cause, a second call site: <see cref="TemporalBelief.PredictPosition"/>'s
    /// "no_last_known_position_to_extrapolate_from" early return also omits
    /// <c>observedAtUtc</c>, even though this method already receives an
    /// explicit <c>asOfUtc</c> parameter it could have used instead. See the
    /// sibling test above for the full defect writeup; not duplicated here.
    /// INTENTIONALLY FAILING until fixed -- do not delete/weaken/skip.
    /// </summary>
    [Fact]
    public void PredictPosition_NoLastKnownPositionResult_LeaksRealWallClockTime_InsteadOfUsingItsOwnAsOfUtcParameter_KnownA3RobustnessGap()
    {
        var farFuture = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        WorldFact<WorldPosition> unknownLastKnown = WorldFact<WorldPosition>.Unknown("never_observed", farFuture);
        WorldFact<WorldVelocity> velocity = WorldFact<WorldVelocity>.Derived(new WorldVelocity(1, 1), 1.0, farFuture);

        WorldFact<WorldPosition> predicted = TemporalBelief.PredictPosition(unknownLastKnown, velocity, TimeSpan.FromSeconds(1), farFuture);

        Assert.True(predicted.ObservedAtUtc.Year > 2050,
            $"Expected PredictPosition to stamp this Unknown result using its own asOfUtc parameter " +
            $"(year 2099); got {predicted.ObservedAtUtc:O}, which is real wall-clock time leaking " +
            $"through WorldFact<WorldPosition>.Unknown(reason)'s default observedAtUtc parameter.");
    }
}
