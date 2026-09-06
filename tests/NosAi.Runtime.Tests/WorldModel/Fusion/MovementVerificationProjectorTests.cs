using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-04/A2: the pure bridge from the real execution verdict
/// (<c>MovementVerification</c>, produced by
/// <see cref="NosAi.Runtime.Navigation.MovementVerifier"/>) into the canonical
/// World Model evidence contract (<c>MovementExecutionEvidence</c>, AP-04/A1),
/// without <c>NosAi.Core</c> ever referencing <c>NosAi.Runtime</c>.
/// </summary>
public sealed class MovementVerificationProjectorTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly DateTime FixedInstant = DateTime.UnixEpoch;
    private static readonly MapPoint Requested = new(4, 7);
    private static readonly TileCoordinate RequestedTile = new(4, 7);

    private static MovementVerification Verification(
        MovementOutcome outcome,
        MapPoint? observed = null,
        string? detail = null) =>
        new(outcome, detail, observed, TimeSpan.FromMilliseconds(120), observed is null ? 0 : 1);

    // -- every outcome maps one-for-one --------------------------------------

    [Theory]
    [InlineData(MovementOutcome.Succeeded, MovementExecutionResult.Succeeded)]
    [InlineData(MovementOutcome.Stalled, MovementExecutionResult.Stalled)]
    [InlineData(MovementOutcome.Displaced, MovementExecutionResult.Displaced)]
    [InlineData(MovementOutcome.Unobserved, MovementExecutionResult.Unobserved)]
    [InlineData(MovementOutcome.Aborted, MovementExecutionResult.Aborted)]
    public void Project_EveryOutcome_MapsToTheMatchingResult(
        MovementOutcome outcome, MovementExecutionResult expected)
    {
        MovementVerification verification = Verification(outcome);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.Equal(expected, evidence.Result);
    }

    // -- an observed cell becomes a Live fact --------------------------------

    [Fact]
    public void Project_SucceededWithObservedCell_ProducesALiveFactAtTheObservedCell()
    {
        // The verifier only reports Succeeded on an arrival at the requested cell,
        // so observed == requested here; both must be carried faithfully.
        var observed = new MapPoint(Requested.X, Requested.Y);
        MovementVerification verification = Verification(MovementOutcome.Succeeded, observed);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.True(evidence.Observed.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Observed.Source);
        Assert.Equal(RequestedTile, evidence.Observed.Value);
        Assert.Equal(FixedInstant, evidence.Observed.ObservedAtUtc);
    }

    [Fact]
    public void Project_StalledWithObservedCell_ProducesALiveFactAtTheObservedCell()
    {
        // A stall still has the character watched: the last accepted reading said
        // where it was (the origin it never left, not the cell asked for). That
        // cell is real evidence even though the step did not happen, so it must
        // project as Live -- and must not be confused with the requested cell.
        var origin = new MapPoint(4, 6);
        MovementVerification verification =
            Verification(MovementOutcome.Stalled, origin, MovementVerifier.StalledReason);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.True(evidence.Observed.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Live, evidence.Observed.Source);
        Assert.Equal(new TileCoordinate(4, 6), evidence.Observed.Value);
        Assert.NotEqual(evidence.Requested, evidence.Observed.Value);
        Assert.Equal(RequestedTile, evidence.Requested);
    }

    // -- nothing observed stays Unknown, carrying the reason -----------------

    [Fact]
    public void Project_UnobservedWithNoReading_ProducesUnknownCarryingTheDetail()
    {
        MovementVerification verification =
            Verification(MovementOutcome.Unobserved, observed: null, MovementVerifier.NoFreshReadingReason);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.False(evidence.Observed.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Unknown, evidence.Observed.Source);
        Assert.Equal(MovementVerifier.NoFreshReadingReason, evidence.Observed.Reason);
        Assert.Equal(MovementVerifier.NoFreshReadingReason, evidence.Detail);
    }

    [Fact]
    public void Project_AbortedWithNoReading_ProducesUnknownCarryingTheDetail()
    {
        const string refusal = "step_live_input_not_armed";
        MovementVerification verification =
            Verification(MovementOutcome.Aborted, observed: null, refusal);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.False(evidence.Observed.HasValue);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Unknown, evidence.Observed.Source);
        Assert.Equal(refusal, evidence.Observed.Reason);
        Assert.Equal(refusal, evidence.Detail);
    }

    [Fact]
    public void Project_UnobservedWithNullDetail_FallsBackToTheNamedSentinelReason()
    {
        MovementVerification verification = Verification(MovementOutcome.Unobserved);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.False(evidence.Observed.HasValue);
        Assert.Equal("movement_not_observed", evidence.Observed.Reason);
        Assert.Null(evidence.Detail);
    }

    // -- Requested always reflects what was asked, never what was seen -------

    [Fact]
    public void Project_Requested_AlwaysReflectsTheRequestedParameter_NeverTheObservedCell()
    {
        // The character was asked to step onto (4,7) and was instead seen on (9,3):
        // a displacement. The evidence must record that (4,7) is what was
        // requested -- the verifier's own record only knows where it landed.
        var observed = new MapPoint(9, 3);
        MovementVerification verification = Verification(MovementOutcome.Displaced, observed, "movement_landed_elsewhere:9,3_not_4,7");

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.Equal(RequestedTile, evidence.Requested);
        Assert.NotEqual(RequestedTile, evidence.Observed.Value);
        Assert.Equal(new TileCoordinate(9, 3), evidence.Observed.Value);
        Assert.Equal(MovementExecutionResult.Displaced, evidence.Result);
    }

    [Fact]
    public void Project_SucceededOnAnUnexpectedCell_StillRecordsWhatWasRequested()
    {
        // Edge case: a verifier outcome that claims success while the observed
        // cell differs from the requested one should never happen (the verifier
        // only reports Succeeded on arrival at `to`), but the projector must not
        // resolve the contradiction by rewriting either side -- the requested
        // cell stays the caller's own, faithfully.
        var observed = new MapPoint(8, 8);
        MovementVerification verification = Verification(MovementOutcome.Succeeded, observed);

        MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
            TestMapId, Requested, in verification, FixedInstant);

        Assert.Equal(RequestedTile, evidence.Requested);
        Assert.Equal(new TileCoordinate(8, 8), evidence.Observed.Value);
    }
}

