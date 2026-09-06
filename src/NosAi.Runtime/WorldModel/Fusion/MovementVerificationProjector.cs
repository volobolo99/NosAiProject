using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Pure, mechanical bridge from a real execution verdict
/// (<c>NosAi.Runtime.Navigation.MovementVerification</c>, produced and
/// Gate-1-verified by <see cref="PathWalkController"/>/<see cref="MovementVerifier"/>/
/// <see cref="SingleStepExecutor"/>) into the canonical World Model movement
/// evidence contract (<c>NosAi.Core.WorldModel.Exploration.MovementExecutionEvidence</c>,
/// AP-04/A1) -- the same role AP-03/A2's <see cref="MapGridObservationProjector"/>
/// plays for map geometry, and the reason <c>NosAi.Core</c> never references
/// <c>NosAi.Runtime</c> (it has zero dependencies by design): this projection is
/// where the two meet.
/// </summary>
/// <remarks>
/// <b>What is mirrored and what is not.</b> Every member of
/// <see cref="MovementOutcome"/> maps to the matching
/// <see cref="MovementExecutionResult"/> one-for-one, so a verdict can never be
/// silently renamed as it crosses the boundary -- the switch below throws on any
/// future value the two enums have not been kept in step on. The observed cell
/// becomes a <c>Live</c> <see cref="WorldFact{T}"/> only when the verifier
/// actually observed one; an <c>Unobserved</c>/<c>Aborted</c> step (no reading
/// postdating the act, or nothing emitted) projects to an <c>Unknown</c> fact
/// carrying the verifier's own named reason -- never a fabricated tile.
/// </remarks>
public static class MovementVerificationProjector
{
    /// <summary>Converts a runtime grid point into the World Model's tile coordinate. Both are integer cells in the same space; no float round-trip.</summary>
    public static TileCoordinate ToTileCoordinate(MapPoint point) =>
        new(point.X, point.Y);

    /// <summary>
    /// Projects one step's verdict into canonical evidence.
    /// </summary>
    /// <param name="mapId">The map the step happened on.</param>
    /// <param name="requested">
    /// The cell <see cref="WalkCommand.Execute"/> asked the character to step onto
    /// (WalkCommand's own <c>to</c>, not <paramref name="verification"/>.Observed):
    /// the verifier only carries what was <i>observed</i>, never what was asked for.
    /// </param>
    /// <param name="verification">The real verdict, produced by <see cref="MovementVerifier.Verify"/>.</param>
    /// <param name="observedAtUtc">The instant this evidence is produced, used to stamp every fact it creates.</param>
    public static MovementExecutionEvidence Project(
        MapId mapId,
        MapPoint requested,
        in MovementVerification verification,
        DateTime observedAtUtc)
    {
        WorldFact<TileCoordinate> observed = verification.Observed is { } at
            ? WorldFact<TileCoordinate>.Live(ToTileCoordinate(at), confidence: 1d, observedAtUtc)
            : WorldFact<TileCoordinate>.Unknown(verification.Detail ?? "movement_not_observed", observedAtUtc);

        return new MovementExecutionEvidence(
            mapId,
            ToTileCoordinate(requested),
            observed,
            ToResult(verification.Outcome),
            verification.Detail,
            observedAtUtc);
    }

    private static MovementExecutionResult ToResult(MovementOutcome outcome) => outcome switch
    {
        MovementOutcome.Succeeded => MovementExecutionResult.Succeeded,
        MovementOutcome.Stalled => MovementExecutionResult.Stalled,
        MovementOutcome.Displaced => MovementExecutionResult.Displaced,
        MovementOutcome.Unobserved => MovementExecutionResult.Unobserved,
        MovementOutcome.Aborted => MovementExecutionResult.Aborted,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome,
            "Unknown MovementOutcome; MovementExecutionResult must be extended to match before this can be projected.")
    };
}
