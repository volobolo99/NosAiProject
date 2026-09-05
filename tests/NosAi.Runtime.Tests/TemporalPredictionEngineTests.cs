using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.TemporalPrediction;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The temporal prediction engine (Simulation/Prediction stage): confidence
/// decay over the horizon, per-field fail-closed refusal on stale/missing
/// input, and that every produced value is stamped SIMULATED and versioned —
/// never LIVE, never a fabricated zero.
/// </summary>
public sealed class TemporalPredictionEngineTests
{
    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TemporalPredictionOptions Options = new(
        MaxInputAge: TimeSpan.FromSeconds(2),
        MaxHorizon: TimeSpan.FromSeconds(5),
        StaleTrackPruneAge: TimeSpan.FromSeconds(30));

    private static Gate3WorldState PlannableState(
        int hp, int maxHp, int mp, DateTime vitalsAt,
        MapPoint? playerAt = null, DateTime? playerAtObserved = null,
        IReadOnlyList<SelectableEntity>? entities = null)
        => new(
            ClassifiedValue<int>.Live(hp, vitalsAt),
            ClassifiedValue<int>.Live(maxHp, vitalsAt),
            ClassifiedValue<int>.Live(mp, vitalsAt),
            ClassifiedValue<bool>.Live(false, vitalsAt),
            ClassifiedValue<bool>.Live(false, vitalsAt),
            Entities: entities,
            PlayerPosition: playerAt is { } p ? ClassifiedValue<MapPoint>.Live(p, playerAtObserved ?? vitalsAt) : null);

    [Fact]
    public void First_observation_holds_position_and_refuses_velocity()
    {
        var engine = new TemporalPredictionEngine(Options);
        Gate3WorldState state = PlannableState(800, 1000, 100, T0, new MapPoint(10, 10));

        TemporalWorldPrediction prediction = engine.Predict(state, TimeSpan.FromMilliseconds(500), T0);

        Assert.True(prediction.IsUsable);
        Assert.Equal(TemporalPredictionContract.Version, prediction.SchemaVersion);
        Assert.True(prediction.PlayerPosition.HasValue);
        Assert.Equal(PredictionModel.HoldLastValue, prediction.PlayerPosition.Model);
        Assert.Equal(new MapPoint(10, 10), prediction.PlayerPosition.Value);
        Assert.False(prediction.PlayerVelocity.HasValue);
        Assert.Equal("insufficient_samples_for_velocity", prediction.PlayerVelocity.FailureReason);
    }

    [Fact]
    public void Second_observation_projects_constant_velocity_forward()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);

        engine.Predict(PlannableState(800, 1000, 100, T0, new MapPoint(0, 0)), TimeSpan.FromMilliseconds(100), T0);

        // The character walked 4 tiles east in one second.
        TemporalWorldPrediction prediction = engine.Predict(
            PlannableState(800, 1000, 100, t1, new MapPoint(4, 0), t1),
            TimeSpan.FromSeconds(1), t1);

        Assert.True(prediction.PlayerVelocity.HasValue);
        Assert.Equal(PredictionModel.ConstantVelocityKalman2D, prediction.PlayerVelocity.Model);
        Assert.True(prediction.PlayerVelocity.Value.Vx > 0, "expected eastward velocity");

        Assert.True(prediction.PlayerPosition.HasValue);
        Assert.True(prediction.PlayerPosition.Value.X > 4, "one more second of eastward motion should read further east than the last sighting");
    }

    [Fact]
    public void Stale_input_is_refused_not_extrapolated()
    {
        var engine = new TemporalPredictionEngine(Options);
        Gate3WorldState state = PlannableState(800, 1000, 100, T0, new MapPoint(0, 0));

        TemporalWorldPrediction prediction = engine.Predict(
            state, TimeSpan.FromSeconds(1), T0 + Options.MaxInputAge + TimeSpan.FromMilliseconds(1));

        Assert.False(prediction.IsUsable);
        Assert.StartsWith("input_stale", prediction.UnusableReason);
        Assert.False(prediction.PlayerPosition.HasValue);
        Assert.False(prediction.PlayerHp.HasValue);
    }

    [Fact]
    public void Horizon_beyond_the_configured_ceiling_is_refused()
    {
        var engine = new TemporalPredictionEngine(Options);
        Gate3WorldState state = PlannableState(800, 1000, 100, T0, new MapPoint(0, 0));

        TemporalWorldPrediction prediction = engine.Predict(state, Options.MaxHorizon + TimeSpan.FromSeconds(1), T0);

        Assert.False(prediction.IsUsable);
        Assert.StartsWith("horizon_out_of_range", prediction.UnusableReason);
    }

    [Fact]
    public void Zero_or_negative_horizon_is_refused()
    {
        var engine = new TemporalPredictionEngine(Options);
        Gate3WorldState state = PlannableState(800, 1000, 100, T0, new MapPoint(0, 0));

        TemporalWorldPrediction prediction = engine.Predict(state, TimeSpan.Zero, T0);

        Assert.False(prediction.IsUsable);
        Assert.StartsWith("horizon_out_of_range", prediction.UnusableReason);
    }

    [Fact]
    public void Unplannable_world_state_is_refused_with_its_own_reason()
    {
        var engine = new TemporalPredictionEngine(Options);
        Gate3WorldState unobserved = Gate3WorldState.Unobserved("no_perception_backend");

        TemporalWorldPrediction prediction = engine.Predict(unobserved, TimeSpan.FromSeconds(1), T0);

        Assert.False(prediction.IsUsable);
        Assert.Equal("no_perception_backend", prediction.UnusableReason);
        Assert.Empty(prediction.Entities);
    }

    [Fact]
    public void Every_produced_field_is_stamped_simulated_never_live()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);
        engine.Predict(PlannableState(800, 1000, 100, T0, new MapPoint(0, 0)), TimeSpan.FromMilliseconds(100), T0);

        TemporalWorldPrediction prediction = engine.Predict(
            PlannableState(820, 1000, 100, t1, new MapPoint(1, 0), t1), TimeSpan.FromSeconds(1), t1);

        Assert.Equal(DataSourceKind.Simulated, prediction.PlayerPosition.Source);
        Assert.Equal(DataSourceKind.Simulated, prediction.PlayerHp.Source);
        Assert.NotEqual(DataSourceKind.Live, prediction.PlayerPosition.Source);
        Assert.NotEqual(DataSourceKind.Live, prediction.PlayerHp.Source);
    }

    [Fact]
    public void Hp_rate_is_extrapolated_and_clamped_to_max_hp()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);

        engine.Predict(PlannableState(500, 1000, 100, T0), TimeSpan.FromMilliseconds(100), T0);

        // Regenerating 150 HP/s; four more seconds of horizon would overshoot
        // MaxHp (1250) and must be reported clamped at the cap, never over it.
        TemporalWorldPrediction prediction = engine.Predict(
            PlannableState(650, 1000, 100, t1), TimeSpan.FromSeconds(4), t1);

        Assert.True(prediction.PlayerHp.HasValue);
        Assert.Equal(PredictionModel.LinearRate, prediction.PlayerHp.Model);
        Assert.Equal(1000, prediction.PlayerHp.Value);
    }

    [Fact]
    public void Entity_position_is_tracked_independently_of_the_player()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);

        var firstSighting = new SelectableEntity(EntityId: 42, At: new MapPoint(0, 0), HpRatio: 1.0, ObservedAtUtc: T0, Vnum: 7);
        var secondSighting = new SelectableEntity(EntityId: 42, At: new MapPoint(0, 3), HpRatio: 1.0, ObservedAtUtc: t1, Vnum: 7);

        engine.Predict(PlannableState(800, 1000, 100, T0, entities: new[] { firstSighting }), TimeSpan.FromMilliseconds(100), T0);
        TemporalWorldPrediction prediction = engine.Predict(
            PlannableState(800, 1000, 100, t1, entities: new[] { secondSighting }), TimeSpan.FromSeconds(1), t1);

        PredictedEntityState entity = Assert.Single(prediction.Entities);
        Assert.Equal(42, entity.EntityId);
        Assert.Equal(7, entity.Vnum);
        Assert.True(entity.Velocity.HasValue);
        Assert.True(entity.Velocity.Value.Vy > 0, "expected the entity to be moving in +Y");
    }

    [Fact]
    public void An_entity_that_stops_appearing_is_absent_next_cycle()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);

        var sighting = new SelectableEntity(1, new MapPoint(5, 5), 1.0, T0, 99);
        engine.Predict(PlannableState(800, 1000, 100, T0, entities: new[] { sighting }), TimeSpan.FromMilliseconds(100), T0);

        TemporalWorldPrediction prediction = engine.Predict(
            PlannableState(800, 1000, 100, t1, entities: Array.Empty<SelectableEntity>()), TimeSpan.FromMilliseconds(100), t1);

        Assert.Empty(prediction.Entities);
    }

    [Fact]
    public void Confidence_decays_as_the_requested_horizon_grows()
    {
        var engine = new TemporalPredictionEngine(Options);
        DateTime t1 = T0.AddSeconds(1);
        engine.Predict(PlannableState(800, 1000, 100, T0, new MapPoint(0, 0)), TimeSpan.FromMilliseconds(100), T0);
        engine.Predict(PlannableState(800, 1000, 100, t1, new MapPoint(1, 0), t1), TimeSpan.FromMilliseconds(100), t1);

        DateTime queryAt = t1.AddMilliseconds(1);
        TemporalWorldPrediction shortHorizon = engine.Predict(
            PlannableState(800, 1000, 100, t1, new MapPoint(1, 0), t1), TimeSpan.FromMilliseconds(200), queryAt);
        TemporalWorldPrediction longHorizon = engine.Predict(
            PlannableState(800, 1000, 100, t1, new MapPoint(1, 0), t1), TimeSpan.FromSeconds(4), queryAt);

        Assert.True(shortHorizon.PlayerPosition.HasValue);
        Assert.True(longHorizon.PlayerPosition.HasValue);
        Assert.True(shortHorizon.PlayerPosition.Confidence > longHorizon.PlayerPosition.Confidence);
    }

    [Fact]
    public void Predicted_value_construction_rejects_a_confidence_outside_zero_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Predicted<int>.Of(1, 1.5, TimeSpan.FromSeconds(1), T0, T0, PredictionModel.LinearRate));
    }

    [Fact]
    public void Predicted_value_construction_requires_a_named_model()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Predicted<int>.Of(1, 0.5, TimeSpan.FromSeconds(1), T0, T0, PredictionModel.None));
    }
}
