namespace NosAi.Core.Statistics;

public sealed class LinearStatePredictor : IStatePredictor
{
    public PredictionResult Predict(IReadOnlyList<StatSample> samples, TimeSpan horizon)
    {
        if (samples.Count < 2 || horizon < TimeSpan.Zero)
            return new PredictionResult(0, 0, horizon, false);

        var first = samples[0];
        var last = samples[^1];
        var elapsed = (last.Timestamp - first.Timestamp).TotalSeconds;
        if (elapsed <= 0)
            return new PredictionResult(last.Value, 0, horizon, false);

        var slope = (last.Value - first.Value) / elapsed;
        var predicted = last.Value + slope * horizon.TotalSeconds;
        var confidence = Math.Clamp(Math.Min(1.0, samples.Count / 20.0), 0.0, 1.0);
        return new PredictionResult(predicted, confidence, horizon, true);
    }
}
