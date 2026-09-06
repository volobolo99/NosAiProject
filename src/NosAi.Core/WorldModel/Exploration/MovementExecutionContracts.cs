namespace NosAi.Core.WorldModel.Exploration;

/// <summary>
/// What became of one attempted movement step, as a canonical World Model
/// fact. Mirrors <c>NosAi.Runtime.Navigation.MovementOutcome</c> in meaning
/// without this project referencing it directly: <c>NosAi.Core</c> has zero
/// dependencies by design (docs/NOSAI_ARCHITECTURE_BASELINE.md), so the real
/// enum stays in <c>NosAi.Runtime</c> and AP-04/A2 (DeepSeek) is the bridge
/// that projects a real <c>MovementVerification</c> into this shape -- the
/// same relationship AP-03/A2's <c>MapGridObservationProjector</c> has with
/// <c>MapGrid</c>.
/// </summary>
public enum MovementExecutionResult
{
    /// <summary>A reading taken after the step put the character on the requested tile.</summary>
    Succeeded = 0,

    /// <summary>The character was observed and had not moved when the verification window closed.</summary>
    Stalled = 1,

    /// <summary>The character moved, but not onto the tile that was requested.</summary>
    Displaced = 2,

    /// <summary>No observation confirming or refuting the step arrived before the verification window closed.</summary>
    Unobserved = 3,

    /// <summary>Nothing was emitted: a guard refused the step, or it was abandoned before execution.</summary>
    Aborted = 4
}

/// <summary>
/// Evidence of one attempted movement step, fed back from execution
/// (<c>NosAi.Runtime.Navigation.PathWalkController</c>/<c>MovementVerifier</c>,
/// already real and Gate-1-verified) into the World Model, so exploration and
/// navigation can tell progress from stalling without re-deriving anything
/// the walk layer already verified.
/// </summary>
/// <param name="MapId">Which map this step happened on.</param>
/// <param name="Requested">The tile the step asked to reach.</param>
/// <param name="Observed">
/// The tile actually observed after the step, with its own provenance and
/// confidence. <c>Unknown</c> exactly when <see cref="Result"/> is
/// <see cref="MovementExecutionResult.Unobserved"/> or
/// <see cref="MovementExecutionResult.Aborted"/> -- nothing was confirmed to
/// compare against, and this contract never fabricates a tile that was not
/// actually observed.
/// </param>
/// <param name="Result">What the verifier concluded about this step.</param>
/// <param name="Detail">The named reason (a guard's refusal, a stall/displacement detail), or <see langword="null"/> for a plain success.</param>
/// <param name="ObservedAtUtc">When this evidence was produced.</param>
public sealed record MovementExecutionEvidence(
    MapId MapId,
    TileCoordinate Requested,
    WorldFact<TileCoordinate> Observed,
    MovementExecutionResult Result,
    string? Detail,
    DateTime ObservedAtUtc)
{
    /// <summary>True only for an arrival on the tile that was requested.</summary>
    public bool Succeeded => Result == MovementExecutionResult.Succeeded;

    /// <summary>The step was never attempted: a guard refused it, or it was abandoned before emission.</summary>
    public static MovementExecutionEvidence NotAttempted(
        MapId mapId,
        TileCoordinate requested,
        string reason,
        DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        return new MovementExecutionEvidence(
            mapId,
            requested,
            WorldFact<TileCoordinate>.Unknown(reason, now),
            MovementExecutionResult.Aborted,
            reason,
            now);
    }
}
