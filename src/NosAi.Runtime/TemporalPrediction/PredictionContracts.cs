// ============================================================================
// NosAi.Runtime — Temporal Prediction (Simulation/Prediction pipeline stage)
// ----------------------------------------------------------------------------
// Canonical pipeline (docs/ROADMAP_ESECUTIVA.md § 1.1):
//   Observe -> Sensor Fusion -> World Model -> Simulation/Prediction ->
//   Ranking/Utility -> Strategic Orchestrator -> HTN/GOAP -> Guard ->
//   Trust/Authorization -> Safety -> Execute -> Verify -> Re-observe.
//
// This namespace implements the Simulation/Prediction stage: a short-horizon,
// deterministic forecast of the state Gate 3 already classified
// (NosAi.Runtime.Gate3.Gate3WorldState), for a downstream ranking/utility
// stage to reason over. It is advisory-only by construction (see the remarks
// on ITemporalPredictionEngine in TemporalPredictionEngine.cs) and never a
// source of execution authority (INV-01).
//
// Distinct from NosAi.Runtime.Gate3.SimulationEngine / PredictedOutcome
// (already committed, already wired into Gate3ExecutionOrchestrator): that
// pair simulates the *effect of one candidate action* from a fixed
// per-action-type delta table. This module instead forecasts how the *world
// itself* evolves over a short horizon regardless of which action is chosen —
// where an entity will be, what the player's vitals trend looks like — so a
// ranking stage can ask "will this still be true by the time an action
// lands" before it ever picks a candidate. The two are complementary:
// PredictedOutcome answers "what happens if I act"; TemporalWorldPrediction
// answers "what will be true even if I do nothing yet".
// ============================================================================

using System.Runtime.InteropServices;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.TemporalPrediction;

/// <summary>
/// Versioning for the temporal prediction contract, following the same
/// "named string constant carried on every published value" convention as
/// <see cref="NosAi.Runtime.Gate1.Gate1SnapshotContract.Version"/>.
/// </summary>
public static class TemporalPredictionContract
{
    /// <summary>
    /// v1: constant-velocity (Kalman 2D) position/velocity projection for the
    /// player and every currently-selectable entity, plus a linear-rate
    /// projection for HP/MP. No cooldown, buff/debuff or map-topology
    /// prediction yet — Gate 3 currently exposes no classified, timestamped
    /// observation of those to project from (see the design note's
    /// "scope deferred" section for why).
    /// </summary>
    public const string Version = "prediction.temporal.v1";
}

/// <summary>
/// Which deterministic method produced one <see cref="Predicted{T}"/> value.
/// Carried on every prediction so a consumer — and a reviewer — can see
/// exactly how a number was derived and never mistake a documented modelling
/// assumption for a measurement.
/// </summary>
public enum PredictionModel : byte
{
    /// <summary>No model was applied; the value is <c>Unknown</c>.</summary>
    None = 0,

    /// <summary>
    /// Exactly one sample exists. There is no velocity/rate evidence to
    /// extrapolate from, so the last observed value is held forward
    /// unchanged. This is a documented hypothesis ("nothing suggests
    /// otherwise yet"), not a measurement — callers can tell it apart from
    /// <see cref="ConstantVelocityKalman2D"/> / <see cref="LinearRate"/> by
    /// this tag, and it always carries a reduced confidence and an explicit
    /// <see cref="Predicted{T}.Warning"/>.
    /// </summary>
    HoldLastValue = 1,

    /// <summary>
    /// Two or more position samples exist. Velocity is estimated by the
    /// constant-velocity 2D Kalman filter already vetted in
    /// <see cref="NosAi.Runtime.Perception.Kalman2DFilter"/> (reused here as
    /// a library, not reimplemented), and position is projected forward
    /// linearly from the filtered state.
    /// </summary>
    ConstantVelocityKalman2D = 2,

    /// <summary>
    /// Two or more scalar samples exist (e.g. HP, MP). The rate of change
    /// between the two most recent samples is extrapolated linearly to the
    /// target time.
    /// </summary>
    LinearRate = 3
}

/// <summary>
/// A 2D velocity estimate, in map tiles per second (see
/// <see cref="MapPoint"/> for the tile unit this is measured against). Wire-
/// shaped (<c>Pack = 1</c>) like the project's other small POD structs (e.g.
/// <c>NosFrameHeader</c>) so it sits in an array with no padding when a
/// caller batches many entities.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct Velocity2D : IEquatable<Velocity2D>
{
    public double Vx { get; }
    public double Vy { get; }

    public Velocity2D(double vx, double vy)
    {
        Vx = vx;
        Vy = vy;
    }

    public double MagnitudeTilesPerSecond => Math.Sqrt(Vx * Vx + Vy * Vy);

    public bool Equals(Velocity2D other) => Vx.Equals(other.Vx) && Vy.Equals(other.Vy);
    public override bool Equals(object? obj) => obj is Velocity2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Vx, Vy);
    public override string ToString() => $"({Vx:F3}, {Vy:F3}) tiles/s";
}

/// <summary>
/// One predicted value: never a bare <typeparamref name="T"/>, always
/// stamped with how confident the projection is, how far it was projected,
/// which deterministic model produced it, and — when it could not be
/// produced — why.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <see cref="ClassifiedValue{T}"/>: that type's
/// <see cref="ClassifiedValue{T}.Source"/> already distinguishes
/// Live/Derived/Cached/Simulated/Unknown observations, but it carries no
/// notion of "confidence that decays over a horizon" or "which deterministic
/// method produced this", and a real observation never needs either.
/// Bolting confidence onto <see cref="ClassifiedValue{T}"/> would either
/// change a type every other consumer already treats as "presence is
/// sufficient", or silently drop confidence here — the exact hidden
/// uncertainty this module exists to avoid. <see cref="Predicted{T}"/> is the
/// prediction-specific sibling, deliberately shaped to read the same way
/// (Value / Source / HasValue / FailureReason / Warning).
/// </para>
/// <para>
/// <see cref="Source"/> is always <see cref="DataSourceKind.Simulated"/> when
/// <see cref="HasValue"/> is true, and always
/// <see cref="DataSourceKind.Unknown"/> otherwise — a predicted value is
/// never <c>Live</c>, and an unproducible one is never a fabricated zero.
/// </para>
/// </remarks>
public readonly record struct Predicted<T>(
    T Value,
    DataSourceKind Source,
    double Confidence,
    TimeSpan Horizon,
    DateTime? BasisObservedAtUtc,
    DateTime GeneratedAtUtc,
    PredictionModel Model,
    bool HasPredictedValue,
    string? FailureReason = null,
    string? Warning = null)
{
    /// <summary>
    /// True only for a real projection. False for every <c>Unknown</c>
    /// result, mirroring <see cref="ClassifiedValue{T}.HasValue"/>: callers
    /// must check this before reading <see cref="Value"/>.
    /// </summary>
    public bool HasValue => HasPredictedValue && Source != DataSourceKind.Unknown;

    /// <summary>A real projection, stamped SIMULATED.</summary>
    public static Predicted<T> Of(
        T value,
        double confidence,
        TimeSpan horizon,
        DateTime basisObservedAtUtc,
        DateTime generatedAtUtc,
        PredictionModel model,
        string? warning = null)
    {
        if (double.IsNaN(confidence) || confidence < 0.0 || confidence > 1.0)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "confidence must be in [0,1]");
        if (model == PredictionModel.None)
            throw new ArgumentOutOfRangeException(nameof(model), model, "a produced value must name the model that produced it");

        return new Predicted<T>(
            value, DataSourceKind.Simulated, confidence, horizon,
            basisObservedAtUtc, generatedAtUtc, model, HasPredictedValue: true,
            FailureReason: null, Warning: warning);
    }

    /// <summary>Nothing could be predicted. Never a default value in disguise.</summary>
    public static Predicted<T> Unknown(
        string reason,
        TimeSpan horizon,
        DateTime? basisObservedAtUtc,
        DateTime generatedAtUtc,
        string? warning = null)
        => new(
            default!, DataSourceKind.Unknown, 0.0, horizon,
            basisObservedAtUtc, generatedAtUtc, PredictionModel.None, HasPredictedValue: false,
            FailureReason: reason, Warning: warning);
}
