using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Temporal;

public sealed class TemporalBeliefTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);

    // ---- DecayConfidence ----------------------------------------------

    [Fact]
    public void DecayConfidence_UnknownFact_IsReturnedUnchanged()
    {
        WorldFact<int> unknown = WorldFact<int>.Unknown("no_reading");

        WorldFact<int> result = TemporalBelief.DecayConfidence(unknown, Now, MaxAge);

        Assert.Equal(unknown, result);
    }

    [Fact]
    public void DecayConfidence_AtHalfTheMaxAge_HalvesConfidence()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now - TimeSpan.FromSeconds(5));

        WorldFact<int> decayed = TemporalBelief.DecayConfidence(fact, Now, MaxAge);

        Assert.Equal(0.5, decayed.Confidence, precision: 6);
    }

    [Fact]
    public void DecayConfidence_AtExactlyMaxAge_ReachesZero()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now - MaxAge);

        WorldFact<int> decayed = TemporalBelief.DecayConfidence(fact, Now, MaxAge);

        Assert.Equal(0.0, decayed.Confidence, precision: 6);
    }

    [Fact]
    public void DecayConfidence_NeverChangesValueSourceOrInstant()
    {
        DateTime observedAt = Now - TimeSpan.FromSeconds(3);
        WorldFact<int> fact = WorldFact<int>.Live(42, 1.0, observedAt);

        WorldFact<int> decayed = TemporalBelief.DecayConfidence(fact, Now, MaxAge);

        Assert.Equal(42, decayed.Value);
        Assert.Equal(DataSourceKind.Live, decayed.Source);
        Assert.Equal(observedAt, decayed.ObservedAtUtc);
    }

    [Fact]
    public void DecayConfidence_AFutureInstant_IsLeftUnchanged()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now + TimeSpan.FromSeconds(1));

        WorldFact<int> decayed = TemporalBelief.DecayConfidence(fact, Now, MaxAge);

        Assert.Equal(1.0, decayed.Confidence);
    }

    [Fact]
    public void DecayConfidence_AlreadyBeyondMaxAge_IsLeftToFactFusionsHardCutoff()
    {
        WorldFact<int> stale = WorldFact<int>.Live(1, 1.0, Now - MaxAge - TimeSpan.FromSeconds(1));

        WorldFact<int> result = TemporalBelief.DecayConfidence(stale, Now, MaxAge);

        Assert.Equal(stale, result);
    }

    [Fact]
    public void DecayConfidence_RejectsNonPositiveMaxAge()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => TemporalBelief.DecayConfidence(fact, Now, TimeSpan.Zero));
    }

    // ---- EstimateVelocity ------------------------------------------------

    [Fact]
    public void EstimateVelocity_TwoKnownPositions_DerivesRatePerSecond()
    {
        WorldFact<WorldPosition> previous = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldPosition> current = WorldFact<WorldPosition>.Live(new WorldPosition(10, 0), 1.0, Now + TimeSpan.FromSeconds(2));

        WorldFact<WorldVelocity> velocity = TemporalBelief.EstimateVelocity(previous, current, TimeSpan.FromSeconds(5));

        Assert.True(velocity.HasValue);
        Assert.Equal(DataSourceKind.Derived, velocity.Source);
        Assert.Equal(5f, velocity.Value.DxPerSecond);
        Assert.Equal(0f, velocity.Value.DyPerSecond);
    }

    [Fact]
    public void EstimateVelocity_ConfidenceIsTheMinimumOfBothInputs()
    {
        WorldFact<WorldPosition> previous = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 0.4, Now);
        WorldFact<WorldPosition> current = WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 0.9, Now + TimeSpan.FromSeconds(1));

        WorldFact<WorldVelocity> velocity = TemporalBelief.EstimateVelocity(previous, current, TimeSpan.FromSeconds(5));

        Assert.Equal(0.4, velocity.Confidence);
    }

    [Fact]
    public void EstimateVelocity_EitherPositionUnknown_ResultsInUnknown()
    {
        WorldFact<WorldPosition> known = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldPosition> unknown = WorldFact<WorldPosition>.Unknown("no_reader_bound");

        Assert.False(TemporalBelief.EstimateVelocity(unknown, known, TimeSpan.FromSeconds(5)).HasValue);
        Assert.False(TemporalBelief.EstimateVelocity(known, unknown, TimeSpan.FromSeconds(5)).HasValue);
    }

    [Fact]
    public void EstimateVelocity_NonIncreasingInstants_ResultsInUnknown()
    {
        WorldFact<WorldPosition> a = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldPosition> b = WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1.0, Now);

        Assert.False(TemporalBelief.EstimateVelocity(a, b, TimeSpan.FromSeconds(5)).HasValue);
    }

    [Fact]
    public void EstimateVelocity_GapLargerThanBound_ResultsInUnknown()
    {
        WorldFact<WorldPosition> previous = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldPosition> current = WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1.0, Now + TimeSpan.FromSeconds(100));

        Assert.False(TemporalBelief.EstimateVelocity(previous, current, TimeSpan.FromSeconds(5)).HasValue);
    }

    // ---- PredictPosition ---------------------------------------------------

    [Fact]
    public void PredictPosition_ExtrapolatesAlongTheEstimatedVelocity()
    {
        WorldFact<WorldPosition> lastKnown = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldVelocity> velocity = WorldFact<WorldVelocity>.Derived(new WorldVelocity(2, 3), 1.0, Now);

        WorldFact<WorldPosition> predicted = TemporalBelief.PredictPosition(lastKnown, velocity, TimeSpan.FromSeconds(2), Now);

        Assert.True(predicted.HasValue);
        Assert.Equal(DataSourceKind.Simulated, predicted.Source);
        Assert.Equal(new WorldPosition(4, 6), predicted.Value);
    }

    [Fact]
    public void PredictPosition_IsAlwaysSimulated_NeverLiveOrDerived()
    {
        WorldFact<WorldPosition> lastKnown = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldVelocity> unknownVelocity = WorldFact<WorldVelocity>.Unknown("not_yet_derived");

        WorldFact<WorldPosition> predicted = TemporalBelief.PredictPosition(lastKnown, unknownVelocity, TimeSpan.FromSeconds(1), Now);

        Assert.Equal(DataSourceKind.Simulated, predicted.Source);
        Assert.Equal(new WorldPosition(0, 0), predicted.Value);
    }

    [Fact]
    public void PredictPosition_NoLastKnownPosition_IsUnknown()
    {
        WorldFact<WorldPosition> unknown = WorldFact<WorldPosition>.Unknown("never_observed");
        WorldFact<WorldVelocity> velocity = WorldFact<WorldVelocity>.Derived(new WorldVelocity(1, 1), 1.0, Now);

        Assert.False(TemporalBelief.PredictPosition(unknown, velocity, TimeSpan.FromSeconds(1), Now).HasValue);
    }

    [Fact]
    public void PredictPosition_RejectsNegativeHorizon()
    {
        WorldFact<WorldPosition> lastKnown = WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now);
        WorldFact<WorldVelocity> velocity = WorldFact<WorldVelocity>.Unknown("reason");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TemporalBelief.PredictPosition(lastKnown, velocity, TimeSpan.FromSeconds(-1), Now));
    }

    [Fact]
    public void PredictPosition_NeverOverwritesTheRealPositionField_ItIsAStandaloneResult()
    {
        // Documents the architectural boundary: PredictPosition returns a value
        // for a caller to use, it never mutates a WorldModelSnapshot's own
        // Player/Mob Position field. This test exists so a future change that
        // tries to "helpfully" fold prediction into the enrichment pipeline
        // has to consciously break it rather than drift into it.
        WorldFact<WorldPosition> lastKnown = WorldFact<WorldPosition>.Live(new WorldPosition(5, 5), 1.0, Now);
        WorldFact<WorldVelocity> velocity = WorldFact<WorldVelocity>.Derived(new WorldVelocity(1, 0), 1.0, Now);

        WorldFact<WorldPosition> predicted = TemporalBelief.PredictPosition(lastKnown, velocity, TimeSpan.FromSeconds(1), Now);

        Assert.NotEqual(lastKnown, predicted);
        Assert.Equal(new WorldPosition(5, 5), lastKnown.Value);
    }
}
