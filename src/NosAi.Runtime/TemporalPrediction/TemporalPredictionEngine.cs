using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.TemporalPrediction;

/// <summary>
/// Tunable envelope for the temporal prediction engine. Both bounds are
/// fail-closed limits, not defaults to aim for: a call outside either returns
/// <see cref="TemporalWorldPrediction.CannotPredict"/> rather than a
/// low-confidence guess.
/// </summary>
/// <param name="MaxInputAge">
/// How old the input <see cref="Gate3WorldState"/>'s oldest reading
/// (<see cref="Gate3WorldState.ObservedAtUtc"/>) may be. Beyond this the
/// world model itself is considered too stale to project from at all — the
/// same reasoning <see cref="Gate3WorldState.IsActionable"/> applies to
/// acting, applied here to predicting.
/// </param>
/// <param name="MaxHorizon">
/// The longest forecast this engine will produce. Short on purpose: a
/// constant-velocity/constant-rate projection is a local linear
/// approximation, and the project's own skepticism of hidden nondeterminism
/// argues for admitting the model's limits via a hard cutoff rather than
/// papering over them with a confidence number nobody is required to check.
/// </param>
/// <param name="StaleTrackPruneAge">
/// How long an entity may go unmentioned in
/// <see cref="Gate3WorldState.Entities"/> before its internal Kalman track
/// is discarded. Bounds the engine's own memory use; it is not a freshness
/// policy for the prediction itself (that is
/// <see cref="MaxInputAge"/>/<see cref="MaxHorizon"/>, acting on each
/// entity's own last-seen instant).
/// </param>
public sealed record TemporalPredictionOptions(
    TimeSpan MaxInputAge,
    TimeSpan MaxHorizon,
    TimeSpan StaleTrackPruneAge,
    double KalmanProcessNoise = 1.0,
    double KalmanMeasurementNoise = 4.0)
{
    /// <summary>
    /// 2 s input freshness, 5 s horizon, 30 s track memory — generous enough
    /// for a human-speed MMO client, short enough that a linear
    /// approximation of monster movement stays plausible. Unvalidated
    /// against a real client; see the design note's verification-level
    /// caveat.
    /// </summary>
    public static TemporalPredictionOptions Default { get; } = new(
        MaxInputAge: TimeSpan.FromSeconds(2),
        MaxHorizon: TimeSpan.FromSeconds(5),
        StaleTrackPruneAge: TimeSpan.FromSeconds(30));
}

/// <summary>
/// Produces a short-horizon <see cref="TemporalWorldPrediction"/> from a
/// <see cref="Gate3WorldState"/>. The Simulation/Prediction stage: the only
/// thing downstream of it in the canonical pipeline is Ranking/Utility.
/// </summary>
/// <remarks>
/// <b>Advisory-only, by construction, not by convention.</b> This interface
/// exposes exactly one method, and it returns an immutable data record with
/// no handle, token or capability anywhere in its object graph — there is
/// nothing here an effector could be wired to even by mistake. Nothing in
/// this namespace implements <c>IActionEffector</c> or references
/// <c>NosAi.Runtime.Safety</c>/<c>NosAi.Runtime.Security</c>, and nothing
/// should ever be added here that does.
/// </remarks>
public interface ITemporalPredictionEngine
{
    /// <param name="state">
    /// The already-classified state to project from. Read-only: this call
    /// never mutates it and never republishes one of its fields under a
    /// different source than the one it was given.
    /// </param>
    /// <param name="horizon">How far past <paramref name="nowUtc"/> to project.</param>
    /// <param name="nowUtc">
    /// The instant the caller considers "now". Passed explicitly, like
    /// <see cref="Gate3WorldState.IsActionable"/>'s own <c>nowUtc</c>
    /// parameter, so a test can pin it and so this engine never reads the
    /// system clock behind the caller's back.
    /// </param>
    TemporalWorldPrediction Predict(Gate3WorldState state, TimeSpan horizon, DateTime nowUtc);
}

/// <summary>See <see cref="ITemporalPredictionEngine"/>.</summary>
public sealed class TemporalPredictionEngine : ITemporalPredictionEngine
{
    private sealed class PositionTrack
    {
        public required Kalman2DFilter Filter { get; init; }
        public DateTime? LastObservedAtUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public int SampleCount { get; set; }
    }

    private sealed class ScalarTrack
    {
        public double LastValue { get; set; }
        public double RatePerSecond { get; set; }
        public DateTime? LastObservedAtUtc { get; set; }
        public int SampleCount { get; set; }
    }

    private readonly TemporalPredictionOptions _options;
    private readonly object _gate = new();

    private readonly PositionTrack _player;
    private readonly ScalarTrack _hp = new();
    private readonly ScalarTrack _mp = new();
    private readonly Dictionary<long, PositionTrack> _entities = new();
    private readonly List<long> _pruneScratch = new();

    public TemporalPredictionEngine(TemporalPredictionOptions? options = null)
    {
        _options = options ?? TemporalPredictionOptions.Default;
        _player = NewTrack();
    }

    private PositionTrack NewTrack() => new()
    {
        Filter = new Kalman2DFilter(_options.KalmanProcessNoise, _options.KalmanMeasurementNoise)
    };

    public TemporalWorldPrediction Predict(Gate3WorldState state, TimeSpan horizon, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (horizon <= TimeSpan.Zero || horizon > _options.MaxHorizon)
            return TemporalWorldPrediction.CannotPredict(
                $"horizon_out_of_range:{horizon.TotalMilliseconds}ms", horizon, nowUtc);

        if (!state.IsPlannable)
            return TemporalWorldPrediction.CannotPredict(
                state.UnusableReason ?? "world_state_not_plannable", horizon, nowUtc);

        if (state.ObservedAtUtc is not { } stateObservedAt)
            return TemporalWorldPrediction.CannotPredict("world_state_never_observed", horizon, nowUtc);

        TimeSpan stateAge = nowUtc - stateObservedAt;
        if (stateAge < TimeSpan.Zero)
            return TemporalWorldPrediction.CannotPredict(
                $"observation_in_future:{stateAge.TotalMilliseconds}ms", horizon, nowUtc);
        if (stateAge > _options.MaxInputAge)
            return TemporalWorldPrediction.CannotPredict(
                $"input_stale:{stateAge.TotalMilliseconds}ms", horizon, nowUtc);

        lock (_gate)
        {
            if (state.PlayerPosition is { HasValue: true } position)
                IngestPosition(_player, position.Value, position.ObservedAtUtc, nowUtc);
            else
                _player.LastSeenUtc = nowUtc;

            Predicted<MapPoint> playerPosition = ProjectPosition(_player, horizon, nowUtc);
            Predicted<Velocity2D> playerVelocity = ProjectVelocity(_player, horizon, nowUtc);

            Predicted<int> playerHp = ProjectScalar(_hp, state.Hp, horizon, nowUtc);
            if (state.MaxHp.HasValue)
                playerHp = ClampScalar(playerHp, 0, state.MaxHp.Value);

            Predicted<int> playerMp = ProjectScalar(_mp, state.Mp, horizon, nowUtc);
            if (playerMp.HasValue && playerMp.Value < 0)
                playerMp = playerMp with { Value = 0 };

            List<PredictedEntityState> entityPredictions;
            if (state.Entities is { Count: > 0 } entities)
            {
                entityPredictions = new List<PredictedEntityState>(entities.Count);
                foreach (SelectableEntity entity in entities)
                {
                    if (!_entities.TryGetValue(entity.EntityId, out PositionTrack? track))
                    {
                        track = NewTrack();
                        _entities[entity.EntityId] = track;
                    }

                    IngestPosition(track, entity.At, entity.ObservedAtUtc, nowUtc);
                    Predicted<MapPoint> entityPosition = ProjectPosition(track, horizon, nowUtc);
                    Predicted<Velocity2D> entityVelocity = ProjectVelocity(track, horizon, nowUtc);
                    entityPredictions.Add(new PredictedEntityState(entity.EntityId, entity.Vnum, entityPosition, entityVelocity));
                }
            }
            else
            {
                entityPredictions = new List<PredictedEntityState>(0);
            }

            PruneStaleEntityTracks(nowUtc);

            return new TemporalWorldPrediction(
                TemporalPredictionContract.Version,
                nowUtc,
                horizon,
                playerPosition,
                playerVelocity,
                playerHp,
                playerMp,
                entityPredictions,
                UnusableReason: null);
        }
    }

    // -------------------------------------------------------------- ingest

    /// <summary>
    /// Folds one new position sighting into the track, advancing the Kalman
    /// filter by the elapsed time since the previous sighting before
    /// correcting it with the new measurement. A sighting no newer than the
    /// one already folded in is evidence of nothing and is ignored, rather
    /// than double-counted as if the world had been observed twice.
    /// </summary>
    private static void IngestPosition(PositionTrack track, MapPoint position, DateTime observedAtUtc, DateTime nowUtc)
    {
        track.LastSeenUtc = nowUtc;

        if (track.LastObservedAtUtc is { } previous)
        {
            if (observedAtUtc <= previous) return;
            double dt = (observedAtUtc - previous).TotalSeconds;
            if (dt > 0) track.Filter.Predict(dt);
            track.Filter.Update(position.X, position.Y);
        }
        else
        {
            track.Filter.Initialize(position.X, position.Y);
        }

        track.LastObservedAtUtc = observedAtUtc;
        track.SampleCount++;
    }

    // ------------------------------------------------------------- project

    private Predicted<MapPoint> ProjectPosition(PositionTrack track, TimeSpan horizon, DateTime nowUtc)
    {
        if (track.LastObservedAtUtc is not { } basis || !track.Filter.IsInitialized)
            return Predicted<MapPoint>.Unknown("no_observation_yet", horizon, null, nowUtc);

        TimeSpan age = nowUtc - basis;
        double sampleFactor = track.SampleCount >= 2 ? 1.0 : 0.5;
        double confidence = ProjectionConfidence(age, horizon, sampleFactor);
        if (confidence <= 0.0)
            return Predicted<MapPoint>.Unknown(
                "confidence_exhausted", horizon, basis, nowUtc,
                warning: $"age={age.TotalMilliseconds}ms horizon={horizon.TotalMilliseconds}ms");

        double projectSeconds = age.TotalSeconds + horizon.TotalSeconds;
        var projected = new MapPoint(
            (int)Math.Round(track.Filter.X + track.Filter.VelocityX * projectSeconds),
            (int)Math.Round(track.Filter.Y + track.Filter.VelocityY * projectSeconds));

        PredictionModel model = track.SampleCount >= 2
            ? PredictionModel.ConstantVelocityKalman2D
            : PredictionModel.HoldLastValue;
        string? warning = track.SampleCount >= 2
            ? null
            : "single_sample_zero_velocity_assumption";

        return Predicted<MapPoint>.Of(projected, confidence, horizon, basis, nowUtc, model, warning);
    }

    private Predicted<Velocity2D> ProjectVelocity(PositionTrack track, TimeSpan horizon, DateTime nowUtc)
    {
        if (track.LastObservedAtUtc is not { } basis || !track.Filter.IsInitialized)
            return Predicted<Velocity2D>.Unknown("no_observation_yet", horizon, null, nowUtc);

        if (track.SampleCount < 2)
            // One sample carries no velocity evidence at all -- unlike position,
            // there is no honest fallback value here (a fabricated 0 would read
            // as "known to be stationary"), so this stays Unknown, never zero.
            return Predicted<Velocity2D>.Unknown(
                "insufficient_samples_for_velocity", horizon, basis, nowUtc,
                warning: "only one position observed; velocity is not yet defined");

        TimeSpan age = nowUtc - basis;
        double confidence = ProjectionConfidence(age, horizon, sampleFactor: 1.0);
        if (confidence <= 0.0)
            return Predicted<Velocity2D>.Unknown(
                "confidence_exhausted", horizon, basis, nowUtc,
                warning: $"age={age.TotalMilliseconds}ms horizon={horizon.TotalMilliseconds}ms");

        var velocity = new Velocity2D(track.Filter.VelocityX, track.Filter.VelocityY);
        return Predicted<Velocity2D>.Of(velocity, confidence, horizon, basis, nowUtc, PredictionModel.ConstantVelocityKalman2D);
    }

    private Predicted<int> ProjectScalar(ScalarTrack track, ClassifiedValue<int> observed, TimeSpan horizon, DateTime nowUtc)
    {
        if (observed.HasValue)
        {
            if (track.LastObservedAtUtc is not { } previous)
            {
                track.LastValue = observed.Value;
                track.SampleCount = 1;
                track.LastObservedAtUtc = observed.ObservedAtUtc;
            }
            else if (observed.ObservedAtUtc > previous)
            {
                double dt = (observed.ObservedAtUtc - previous).TotalSeconds;
                if (dt > 0) track.RatePerSecond = (observed.Value - track.LastValue) / dt;
                track.LastValue = observed.Value;
                track.SampleCount++;
                track.LastObservedAtUtc = observed.ObservedAtUtc;
            }
        }

        if (track.LastObservedAtUtc is not { } basis)
            return Predicted<int>.Unknown("no_observation_yet", horizon, null, nowUtc);

        TimeSpan age = nowUtc - basis;
        double sampleFactor = track.SampleCount >= 2 ? 1.0 : 0.5;
        double confidence = ProjectionConfidence(age, horizon, sampleFactor);
        if (confidence <= 0.0)
            return Predicted<int>.Unknown(
                "confidence_exhausted", horizon, basis, nowUtc,
                warning: $"age={age.TotalMilliseconds}ms horizon={horizon.TotalMilliseconds}ms");

        double projectSeconds = age.TotalSeconds + horizon.TotalSeconds;
        double predictedValue = track.SampleCount >= 2
            ? track.LastValue + track.RatePerSecond * projectSeconds
            : track.LastValue;

        PredictionModel model = track.SampleCount >= 2 ? PredictionModel.LinearRate : PredictionModel.HoldLastValue;
        string? warning = track.SampleCount >= 2 ? null : "single_sample_zero_rate_assumption";

        return Predicted<int>.Of((int)Math.Round(predictedValue), confidence, horizon, basis, nowUtc, model, warning);
    }

    private static Predicted<int> ClampScalar(Predicted<int> value, int min, int max)
    {
        if (!value.HasValue) return value;
        int clamped = Math.Clamp(value.Value, min, max);
        return clamped == value.Value ? value : value with { Value = clamped };
    }

    /// <summary>
    /// confidence = clamp01(1 - (age + horizon) / (MaxInputAge + MaxHorizon)) * sampleFactor
    ///
    /// A single linear envelope shared by every projected field: the further
    /// a projection reaches past real evidence -- whether that distance is
    /// "the input was already stale" or "the horizon requested is long" --
    /// the less this engine will vouch for it, until at the edge of the
    /// configured envelope it refuses outright (confidence 0 becomes
    /// <c>Unknown</c> at the call site, never a low-confidence number nobody
    /// is required to check). sampleFactor demotes a single-sample
    /// hold-last-value hypothesis without a separate code path.
    /// </summary>
    private double ProjectionConfidence(TimeSpan age, TimeSpan horizon, double sampleFactor)
    {
        double envelope = (_options.MaxInputAge + _options.MaxHorizon).TotalSeconds;
        if (envelope <= 0) return 0.0;
        double projection = age.TotalSeconds + horizon.TotalSeconds;
        double linear = 1.0 - projection / envelope;
        if (linear < 0) linear = 0;
        if (linear > 1) linear = 1;
        return linear * sampleFactor;
    }

    private void PruneStaleEntityTracks(DateTime nowUtc)
    {
        if (_entities.Count == 0) return;
        _pruneScratch.Clear();
        foreach (KeyValuePair<long, PositionTrack> pair in _entities)
            if (nowUtc - pair.Value.LastSeenUtc > _options.StaleTrackPruneAge)
                _pruneScratch.Add(pair.Key);
        foreach (long id in _pruneScratch)
            _entities.Remove(id);
    }
}
