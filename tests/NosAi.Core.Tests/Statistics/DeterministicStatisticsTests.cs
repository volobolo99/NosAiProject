using NosAi.Core.Statistics;

namespace NosAi.Core.Tests.Statistics;

public sealed class DeterministicStatisticsTests
{
    [Fact]
    public void MeanEstimator_IsStable()
    {
        var samples = new[]
        {
            new StatSample(DateTimeOffset.UnixEpoch, 10, "Network"),
            new StatSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 20, "Network"),
            new StatSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 30, "Network")
        };

        Assert.Equal(20, new MeanStatisticEstimator().Estimate(samples), 6);
    }

    [Fact]
    public void LinearPredictor_ExtrapolatesObservedSlope()
    {
        var samples = new[]
        {
            new StatSample(DateTimeOffset.UnixEpoch, 10, "Memory"),
            new StatSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 30, "Memory")
        };

        var result = new LinearStatePredictor().Predict(samples, TimeSpan.FromSeconds(1));

        Assert.True(result.IsValid);
        Assert.Equal(40, result.Value, 6);
        Assert.Equal(0.5, result.Confidence, 6);
    }

    [Fact]
    public void LinearPredictor_RejectsInsufficientSamples()
    {
        var samples = new[] { new StatSample(DateTimeOffset.UnixEpoch, 10, "Screen") };
        var result = new LinearStatePredictor().Predict(samples, TimeSpan.FromSeconds(1));
        Assert.False(result.IsValid);
    }
}
