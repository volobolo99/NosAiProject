namespace NosAi.Core.Statistics;

public readonly record struct StatSample(
    DateTimeOffset Timestamp,
    double Value,
    string Provenance);

public sealed record StatSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, double> Values);

public readonly record struct PredictionResult(
    double Value,
    double Confidence,
    TimeSpan Horizon,
    bool IsValid);

public interface IStatisticEstimator
{
    double Estimate(IReadOnlyList<StatSample> samples);
}

public interface IStatePredictor
{
    PredictionResult Predict(IReadOnlyList<StatSample> samples, TimeSpan horizon);
}
