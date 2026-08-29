namespace NosAi.Runtime.Telemetry;

public sealed record TelemetryMetrics(
    long TimestampTicks,
    long CycleIndex,
    double XpYieldRate,
    double TimeEfficiencyRatio,
    double SafetyFactor,
    double ResourceConservationIndex,
    double ObjectiveCompletionMetric,
    double GlobalMasteryScore,
    int ActiveTrustTier,
    int VetoCount,
    int RecoveryCount);

public sealed class TelemetryCollector
{
    private readonly object _lock = new();
    private readonly List<TelemetryMetrics> _history = [];

    public void Record(TelemetryMetrics metrics)
    {
        lock (_lock)
            _history.Add(metrics);
    }

    public IReadOnlyList<TelemetryMetrics> Snapshot()
    {
        lock (_lock)
            return _history.ToArray();
    }

    public static double CalculateMastery(double xp, double efficiency, double safety, double resources, double objective)
        => Math.Clamp(0.30 * xp + 0.20 * efficiency + 0.20 * safety + 0.15 * resources + 0.15 * objective, 0, 100);
}
