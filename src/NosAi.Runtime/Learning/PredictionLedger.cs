using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate4;

namespace NosAi.Runtime.Learning;

/// <summary>Something the runtime expected to happen, recorded before it acted.</summary>
/// <remarks>
/// Written before the action, never after. A prediction recorded once the outcome
/// is known is not a prediction, and a system that scores itself that way will
/// always look calibrated.
/// </remarks>
public sealed record Prediction(
    Guid Id,
    string ContextKey,
    string Quantity,
    double Expected,
    double Tolerance,
    DateTime MadeAtUtc);

/// <summary>What actually happened, and how well it was known.</summary>
public sealed record Observation(
    Guid PredictionId,
    double Actual,
    DataSourceKind Source,
    DateTime ObservedAtUtc,
    string? Note = null);

/// <summary>Why an observation did or did not teach the model anything.</summary>
public enum LearningOutcome
{
    /// <summary>The prediction is still open; nothing has been observed.</summary>
    Pending = 0,

    /// <summary>Observed live and within tolerance: the belief moves toward success.</summary>
    Confirmed = 1,

    /// <summary>Observed live and outside tolerance: the belief moves toward failure.</summary>
    Refuted = 2,

    /// <summary>
    /// Observed, but not from a source that can teach. Counted, never learned from.
    /// </summary>
    NotLearnable = 3,

    /// <summary>No prediction with that identifier was ever made.</summary>
    Unmatched = 4
}

/// <summary>How well one context's predictions have held up.</summary>
public sealed record Calibration(
    string ContextKey,
    BetaBinomialEvidence Evidence,
    int Confirmed,
    int Refuted,
    int Ignored,
    double MeanAbsoluteError)
{
    /// <summary>The believed probability that the next prediction here holds.</summary>
    public double ExpectedAccuracy => Evidence.ExpectedSuccessRate;

    /// <summary>
    /// How much the belief is worth trusting: it is a prior until evidence arrives.
    /// </summary>
    /// <remarks>
    /// Exposed so a caller can tell a rate resting on two observations from the same
    /// rate resting on two hundred. A confidence that does not say how much it is
    /// standing on is the kind of number that gets over-trusted.
    /// </remarks>
    public int Trials => Evidence.TotalTrials;
}

/// <summary>
/// Records what the runtime expected, compares it with what happened, and learns.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one rule that makes this honest.</b> Only an outcome observed
/// <see cref="DataSourceKind.Live"/> updates a belief. An outcome that came from a
/// simulation, a cache or nowhere at all is counted and kept visible, but it must
/// never move the model: a system that learns from its own predictions converges,
/// confidently and quickly, on its own fantasy. That failure looks exactly like
/// success from the inside, which is why the rule is enforced here rather than left
/// to callers to remember.
/// </para>
/// <para>
/// <b>Why Beta-Binomial.</b> Gate 4 already carries
/// <see cref="BetaBinomialEvidence"/> and a UCB1 selector over it, both tested. What
/// was missing was not the mathematics but the ledger feeding it: Gate 4 learns
/// which quest strategy works, and nothing was learning whether a prediction about
/// the next moment was any good. This reuses that evidence rather than growing a
/// second, differently-wrong copy of it.
/// </para>
/// </remarks>
public sealed class PredictionLedger
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Prediction> _open = new();
    private readonly Dictionary<string, BetaBinomialEvidence> _evidence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Confirmed, int Refuted, int Ignored, double ErrorSum)> _tally =
        new(StringComparer.Ordinal);

    /// <summary>Predictions recorded and not yet resolved.</summary>
    public int OpenPredictions
    {
        get { lock (_lock) return _open.Count; }
    }

    /// <summary>
    /// Records what is expected, before acting.
    /// </summary>
    /// <param name="tolerance">
    /// How far the outcome may fall from <paramref name="expected"/> and still count
    /// as confirmed. A prediction with no tolerance is unfalsifiable in one direction
    /// and useless in the other, so it must be stated.
    /// </param>
    public Prediction Predict(
        string contextKey, string quantity, double expected, double tolerance, DateTime? atUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);
        if (tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "La tolleranza non può essere negativa.");

        var prediction = new Prediction(
            Guid.NewGuid(), contextKey, quantity, expected, tolerance, atUtc ?? DateTime.UtcNow);

        lock (_lock)
            _open[prediction.Id] = prediction;

        return prediction;
    }

    /// <summary>
    /// Settles a prediction against what was observed.
    /// </summary>
    public LearningOutcome Record(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_lock)
        {
            if (!_open.Remove(observation.PredictionId, out Prediction? prediction))
                return LearningOutcome.Unmatched;

            (int confirmed, int refuted, int ignored, double errorSum) =
                _tally.TryGetValue(prediction.ContextKey, out var current)
                    ? current
                    : (0, 0, 0, 0.0);

            // The rule. A simulated outcome is evidence about the simulation, not
            // about the game, and letting it move the belief would teach the model
            // that its own guesses are true.
            if (observation.Source != DataSourceKind.Live)
            {
                _tally[prediction.ContextKey] = (confirmed, refuted, ignored + 1, errorSum);
                return LearningOutcome.NotLearnable;
            }

            double error = Math.Abs(observation.Actual - prediction.Expected);
            bool held = error <= prediction.Tolerance;

            BetaBinomialEvidence evidence = _evidence.TryGetValue(prediction.ContextKey, out var previous)
                ? previous
                : BetaBinomialEvidence.CreateUniformPrior();
            _evidence[prediction.ContextKey] = evidence.RecordTrial(held);

            _tally[prediction.ContextKey] = held
                ? (confirmed + 1, refuted, ignored, errorSum + error)
                : (confirmed, refuted + 1, ignored, errorSum + error);

            return held ? LearningOutcome.Confirmed : LearningOutcome.Refuted;
        }
    }

    /// <summary>
    /// What is believed about one context, or null when nothing has been learned.
    /// </summary>
    /// <remarks>
    /// Null rather than a uniform prior dressed up as a result: "we have no evidence"
    /// and "we believe it is a coin flip" are different claims about the world.
    /// </remarks>
    public Calibration? CalibrationOf(string contextKey)
    {
        lock (_lock)
        {
            if (!_tally.TryGetValue(contextKey, out var tally))
                return null;

            BetaBinomialEvidence evidence = _evidence.TryGetValue(contextKey, out var e)
                ? e
                : BetaBinomialEvidence.CreateUniformPrior();

            int learned = tally.Confirmed + tally.Refuted;
            double mae = learned == 0 ? 0 : tally.ErrorSum / learned;

            return new Calibration(
                contextKey, evidence, tally.Confirmed, tally.Refuted, tally.Ignored, mae);
        }
    }

    /// <summary>Every context that has seen an outcome, worst calibrated first.</summary>
    /// <remarks>
    /// Ordered by accuracy ascending on purpose: the useful question is not where the
    /// model is right but where it keeps being wrong.
    /// </remarks>
    public IReadOnlyList<Calibration> Weakest(int limit = 10)
    {
        lock (_lock)
        {
            return _tally.Keys
                .Select(CalibrationOf)
                .Where(c => c is not null && c.Trials > 0)
                .Select(c => c!)
                .OrderBy(c => c.ExpectedAccuracy)
                .ThenByDescending(c => c.Trials)
                .Take(limit)
                .ToArray();
        }
    }

    /// <summary>
    /// Drops predictions never settled, so they cannot sit open forever.
    /// </summary>
    /// <returns>How many were abandoned; they are not counted as either outcome.</returns>
    /// <remarks>
    /// An unsettled prediction is neither right nor wrong, and quietly scoring it
    /// either way would bend the calibration toward whichever was chosen.
    /// </remarks>
    public int Expire(TimeSpan olderThan, DateTime? nowUtc = null)
    {
        DateTime cutoff = (nowUtc ?? DateTime.UtcNow) - olderThan;
        lock (_lock)
        {
            Guid[] stale = _open.Where(p => p.Value.MadeAtUtc < cutoff).Select(p => p.Key).ToArray();
            foreach (Guid id in stale)
                _open.Remove(id);
            return stale.Length;
        }
    }
}
