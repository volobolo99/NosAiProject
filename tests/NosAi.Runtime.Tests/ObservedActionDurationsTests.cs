using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The durations the runtime measures, fed back into the prediction that used to
/// be a constant.
/// </summary>
/// <remarks>
/// <para>
/// <c>SimulationEngine.Simulate</c> answered <c>ExpectedTimeMs</c> from one
/// literal per action type, and <c>Gate3Runtime</c> measured the real duration on
/// every executed round and discarded it. <c>NosAi.Core.Statistics</c> held the
/// incremental mean that turns the second into the first, and had no caller.
/// </para>
/// <para>
/// The wiring is only worth anything if something reads the result, which nothing
/// did: <c>ExpectedTimeMs</c> had zero consumers outside its own record.
/// <c>TacticalRankingEngine</c> now reads it -- and only when it is measured, so
/// the constant never becomes a preference.
/// </para>
/// </remarks>
public sealed class ObservedActionDurationsTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static ActionCandidate Attack() => new(
        Guid.NewGuid(), ActionType.UseBasicAttack, new ActionTarget.Entity(101, new MapPoint(1, 0)),
        0, TrustTier.Tier1_Assisted, "test");

    [Fact]
    public void WithNoMeasurement_ThereIsNoMean()
    {
        var durations = new ObservedActionDurations();

        Assert.Null(durations.MeanMs(ActionType.UseBasicAttack));
        Assert.Equal(0, durations.SampleCount(ActionType.UseBasicAttack));
    }

    /// <summary>The mean is the mean, and it is kept per action type.</summary>
    [Fact]
    public void TheMeanIsPerActionType()
    {
        var durations = new ObservedActionDurations();
        durations.Record(ActionType.UseBasicAttack, 100, At);
        durations.Record(ActionType.UseBasicAttack, 300, At);
        durations.Record(ActionType.UseSkill, 900, At);

        Assert.Equal(200, durations.MeanMs(ActionType.UseBasicAttack));
        Assert.Equal(900, durations.MeanMs(ActionType.UseSkill));
        Assert.Null(durations.MeanMs(ActionType.MoveToPosition));
    }

    /// <summary>
    /// A zero duration is what an effector that never ran reports, so counting it
    /// would teach the mean that a suppressed act is an instantaneous one.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveDurationIsNotAMeasurement(int reported)
    {
        var durations = new ObservedActionDurations();
        durations.Record(ActionType.UseBasicAttack, reported, At);

        Assert.Null(durations.MeanMs(ActionType.UseBasicAttack));
    }

    /// <summary>
    /// The history is bounded, so a long-running loop does not turn a measurement
    /// into a leak -- and the mean follows a machine that changed.
    /// </summary>
    [Fact]
    public void OnlyTheMostRecentSamplesAreKept()
    {
        var durations = new ObservedActionDurations();
        for (int i = 0; i < ObservedActionDurations.SamplesPerActionType; i++)
            durations.Record(ActionType.UseBasicAttack, 1000, At);

        Assert.Equal(ObservedActionDurations.SamplesPerActionType, durations.SampleCount(ActionType.UseBasicAttack));
        Assert.Equal(1000, durations.MeanMs(ActionType.UseBasicAttack));

        // The same number of new, faster rounds displaces every old one.
        for (int i = 0; i < ObservedActionDurations.SamplesPerActionType; i++)
            durations.Record(ActionType.UseBasicAttack, 100, At);

        Assert.Equal(ObservedActionDurations.SamplesPerActionType, durations.SampleCount(ActionType.UseBasicAttack));
        Assert.Equal(100, durations.MeanMs(ActionType.UseBasicAttack));
    }

    /// <summary>
    /// The prediction says the constant is a constant, and says the mean is a
    /// measurement.
    /// </summary>
    [Fact]
    public void ThePredictionReportsWhetherItsDurationWasMeasured()
    {
        var durations = new ObservedActionDurations();
        var engine = new SimulationEngine(durations: durations);
        ActionCandidate candidate = Attack();

        PredictedOutcome beforeAnyRound = engine.Simulate(candidate, 1000, 100, 1000);
        Assert.False(beforeAnyRound.ExpectedTimeIsMeasured);
        Assert.Equal(600, beforeAnyRound.ExpectedTimeMs);

        durations.Record(ActionType.UseBasicAttack, 120, At);

        PredictedOutcome afterOneRound = engine.Simulate(candidate, 1000, 100, 1000);
        Assert.True(afterOneRound.ExpectedTimeIsMeasured);
        Assert.Equal(120, afterOneRound.ExpectedTimeMs);
    }

    /// <summary>
    /// The wiring is not inert: a measured duration changes the ranking, and an
    /// unmeasured one does not.
    /// </summary>
    /// <remarks>
    /// Before this, <c>ExpectedTimeMs</c> had no consumer at all. Ranking on the
    /// old constant would have been ranking on a literal -- a preference between
    /// action types wearing a measurement's clothes -- which is why the term is
    /// gated on the fact having been measured rather than on it existing.
    /// </remarks>
    [Fact]
    public void AMeasuredDurationChangesTheRanking_AnUnmeasuredOneDoesNot()
    {
        var ranking = new TacticalRankingEngine();
        ActionCandidate candidate = Attack();
        var candidates = new[] { candidate };

        PredictedOutcome unmeasured = new SimulationEngine().Simulate(candidate, 1000, 100, 1000);

        var durations = new ObservedActionDurations();
        durations.Record(ActionType.UseBasicAttack, 4000, At);
        PredictedOutcome slowAndMeasured = new SimulationEngine(durations: durations)
            .Simulate(candidate, 1000, 100, 1000);

        float withoutMeasurement = ranking.RankCandidates(
            candidates,
            new Dictionary<Guid, PredictedOutcome> { [candidate.CandidateId] = unmeasured },
            1000, 1000)[0].UtilityScore;

        float withMeasurement = ranking.RankCandidates(
            candidates,
            new Dictionary<Guid, PredictedOutcome> { [candidate.CandidateId] = slowAndMeasured },
            1000, 1000)[0].UtilityScore;

        Assert.True(withMeasurement < withoutMeasurement,
            $"a measured four-second action should rank below the same one unmeasured: "
            + $"{withMeasurement} vs {withoutMeasurement}");

        // And never by more than the cap: being slow must not outweigh survival.
        Assert.True(withoutMeasurement - withMeasurement <= TacticalRankingEngine.SlowestPenalty + 0.0001f);
    }
}
