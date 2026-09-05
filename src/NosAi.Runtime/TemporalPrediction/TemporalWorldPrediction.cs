using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.TemporalPrediction;

/// <summary>
/// One tracked entity's projected kinematics at
/// <see cref="TemporalWorldPrediction.Horizon"/> past
/// <see cref="TemporalWorldPrediction.GeneratedAtUtc"/>.
/// </summary>
/// <param name="EntityId">
/// Same id as the <see cref="NosAi.Runtime.Autonomy.SelectableEntity"/> this
/// was projected from.
/// </param>
/// <param name="Vnum">
/// Carried through unchanged from the observation; see
/// <see cref="NosAi.Runtime.Autonomy.SelectableEntity"/>'s own remarks for
/// why it is never interpreted here either.
/// </param>
public sealed record PredictedEntityState(
    long EntityId,
    int? Vnum,
    Predicted<MapPoint> Position,
    Predicted<Velocity2D> Velocity);

/// <summary>
/// The Simulation/Prediction stage's whole output for one cycle: a short-
/// horizon, advisory-only forecast of the state
/// <see cref="NosAi.Runtime.Gate3.Gate3WorldState"/> already classified.
/// Consumed by a downstream ranking/utility stage — never by an effector
/// (see <see cref="TemporalPredictionEngine"/>'s remarks for the
/// advisory-only boundary this type is deliberately shaped to hold: nothing
/// in its object graph is a handle, a token or a capability).
/// </summary>
/// <param name="SchemaVersion">
/// Always <see cref="TemporalPredictionContract.Version"/>; carried on the
/// value itself so a stored or replayed prediction is self-describing.
/// </param>
/// <param name="UnusableReason">
/// Null when at least the player position/vitals projection could be
/// attempted; otherwise names why nothing in this cycle could be projected
/// at all (e.g. the input world state was itself unplannable, or the
/// requested horizon was out of range). Every per-field
/// <see cref="Predicted{T}"/> still carries its own reason independently of
/// this one — a global reason here does not explain a single field's local
/// refusal, and a single field's refusal does not set this one (the same
/// "only the reader of an absent fact pays for it" rule
/// <see cref="NosAi.Runtime.Gate3.Gate3WorldState.UnusableReason"/> follows).
/// </param>
public sealed record TemporalWorldPrediction(
    string SchemaVersion,
    DateTime GeneratedAtUtc,
    TimeSpan Horizon,
    Predicted<MapPoint> PlayerPosition,
    Predicted<Velocity2D> PlayerVelocity,
    Predicted<int> PlayerHp,
    Predicted<int> PlayerMp,
    IReadOnlyList<PredictedEntityState> Entities,
    string? UnusableReason)
{
    /// <summary>Whether this cycle produced anything usable at all.</summary>
    public bool IsUsable => UnusableReason is null;

    /// <summary>
    /// The refusal shape: every field Unknown, entities empty, and a named
    /// top-level reason. Used whenever the input itself rules out projecting
    /// anything — fail-closed, never a fabricated forecast.
    /// </summary>
    public static TemporalWorldPrediction CannotPredict(string reason, TimeSpan horizon, DateTime generatedAtUtc) => new(
        TemporalPredictionContract.Version,
        generatedAtUtc,
        horizon,
        Predicted<MapPoint>.Unknown(reason, horizon, null, generatedAtUtc),
        Predicted<Velocity2D>.Unknown(reason, horizon, null, generatedAtUtc),
        Predicted<int>.Unknown(reason, horizon, null, generatedAtUtc),
        Predicted<int>.Unknown(reason, horizon, null, generatedAtUtc),
        Array.Empty<PredictedEntityState>(),
        reason);
}
