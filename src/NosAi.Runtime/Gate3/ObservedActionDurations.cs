using NosAi.Core.Statistics;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// How long each kind of action has actually taken, kept so the simulation can
/// predict a measured number instead of a constant.
/// </summary>
/// <remarks>
/// <para>
/// <c>SimulationEngine.Simulate</c> answered <c>ExpectedTimeMs</c> from a literal
/// per action type -- 800 ms for every skill, 600 for every basic attack -- while
/// the runtime measured the real duration on every executed round
/// (<c>Gate3Runtime.cs:723</c>, <c>result with { ActualDurationMs = ... }</c>) and
/// threw it away. Two numbers about the same thing, one invented and one
/// observed, and the invented one was the one that travelled.
/// </para>
/// <para>
/// The mean is <see cref="MeanStatisticEstimator"/> from
/// <c>NosAi.Core.Statistics</c>, which computed exactly this and had no caller.
/// It is an incremental mean, so a long series does not lose precision to
/// accumulated addition.
/// </para>
/// <para>
/// <b>Bounded on purpose.</b> Samples are capped per action type and the oldest
/// is dropped. An unbounded history is how a long-running loop turns a
/// measurement into a leak -- the telemetry module removed the same day kept a
/// <c>List</c> with no cap and nobody had noticed, because nothing constructed
/// it. Dropping the oldest also lets the mean follow a machine that changed:
/// a duration measured on a different client build is not evidence about this
/// one forever.
/// </para>
/// </remarks>
public sealed class ObservedActionDurations
{
    /// <summary>How many measurements are kept per action type.</summary>
    /// <remarks>
    /// Enough that one slow round does not move the mean much, few enough that
    /// the mean still follows a real change within a session. Not tuned against
    /// a measured distribution, because there is not one yet -- which is why it
    /// is a named constant and not a literal buried in the code.
    /// </remarks>
    public const int SamplesPerActionType = 64;

    private readonly MeanStatisticEstimator _mean = new();
    private readonly Dictionary<ActionType, List<StatSample>> _samples = new();
    private readonly object _sync = new();

    /// <summary>Records one executed action's real duration.</summary>
    /// <param name="type">The action that was executed.</param>
    /// <param name="actualDurationMs">Milliseconds measured around the effector call.</param>
    /// <param name="at">When the round happened.</param>
    /// <remarks>
    /// A non-positive duration is refused rather than averaged in. Zero is what
    /// an effector that never ran reports (<c>Gate3Effector.cs:99</c> and
    /// <c>:189</c> both construct their result with <c>ActualDurationMs: 0</c>),
    /// so counting it would teach the mean that a suppressed action is an
    /// instantaneous one.
    /// </remarks>
    public void Record(ActionType type, int actualDurationMs, DateTimeOffset at)
    {
        if (actualDurationMs <= 0)
            return;

        lock (_sync)
        {
            if (!_samples.TryGetValue(type, out List<StatSample>? series))
                _samples[type] = series = new List<StatSample>(SamplesPerActionType);

            series.Add(new StatSample(at, actualDurationMs, Provenance: "gate3_executed_round"));
            if (series.Count > SamplesPerActionType)
                series.RemoveAt(0);
        }
    }

    /// <summary>
    /// The mean measured duration for this action type, or <see langword="null"/>
    /// when none has been measured.
    /// </summary>
    /// <remarks>
    /// Null rather than a default: a caller that cannot tell an observed duration
    /// from a fallback would put the fallback where the observation goes, which
    /// is the confusion this whole type exists to end.
    /// </remarks>
    public int? MeanMs(ActionType type)
    {
        StatSample[] series;
        lock (_sync)
        {
            if (!_samples.TryGetValue(type, out List<StatSample>? stored) || stored.Count == 0)
                return null;
            series = stored.ToArray();
        }

        double mean = _mean.Estimate(series);
        return double.IsNaN(mean) ? null : (int)Math.Round(mean);
    }

    /// <summary>How many measurements back this action type's mean, for reporting.</summary>
    public int SampleCount(ActionType type)
    {
        lock (_sync)
            return _samples.TryGetValue(type, out List<StatSample>? series) ? series.Count : 0;
    }
}
