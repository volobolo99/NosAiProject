namespace NosAi.Core.Statistics;

public sealed class MeanStatisticEstimator : IStatisticEstimator
{
    public double Estimate(IReadOnlyList<StatSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0) return double.NaN;
        double mean = 0;
        for (var i = 0; i < samples.Count; i++)
            mean += (samples[i].Value - mean) / (i + 1);
        return mean;
    }
}

public sealed class LinearStatePredictor : IStatePredictor
{
    private readonly int _minimumSamples;

    public LinearStatePredictor(int minimumSamples = 2)
    {
        if (minimumSamples < 2) throw new ArgumentOutOfRangeException(nameof(minimumSamples));
        _minimumSamples = minimumSamples;
    }

    public PredictionResult Predict(IReadOnlyList<StatSample> samples, TimeSpan horizon)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (horizon < TimeSpan.Zero || samples.Count < _minimumSamples)
            return new PredictionResult(double.NaN, 0, horizon, false);

        var first = samples[0];
        var last = samples[^1];
        var elapsed = (last.Timestamp - first.Timestamp).TotalSeconds;
        if (elapsed <= 0 || double.IsNaN(elapsed) || double.IsInfinity(elapsed))
            return new PredictionResult(double.NaN, 0, horizon, false);

        var slope = (last.Value - first.Value) / elapsed;
        var predicted = last.Value + slope * horizon.TotalSeconds;
        if (double.IsNaN(predicted) || double.IsInfinity(predicted))
            return new PredictionResult(double.NaN, 0, horizon, false);

        var confidence = Math.Clamp(1.0 - 1.0 / samples.Count, 0, 1);
        return new PredictionResult(predicted, confidence, horizon, true);
    }
}
