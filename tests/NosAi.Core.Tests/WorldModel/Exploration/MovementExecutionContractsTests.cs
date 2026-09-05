using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Exploration;

public sealed class MovementExecutionEvidenceTests
{
    private static readonly MapId TestMapId = new("map-1");
    private static readonly TileCoordinate RequestedTile = new(4, 7);

    [Fact]
    public void NotAttempted_ResultIsAborted()
    {
        MovementExecutionEvidence evidence = MovementExecutionEvidence.NotAttempted(TestMapId, RequestedTile, "guard_refused");

        Assert.Equal(MovementExecutionResult.Aborted, evidence.Result);
    }

    [Fact]
    public void NotAttempted_ObservedIsUnknown()
    {
        MovementExecutionEvidence evidence = MovementExecutionEvidence.NotAttempted(TestMapId, RequestedTile, "guard_refused");

        Assert.False(evidence.Observed.HasValue);
    }

    [Fact]
    public void NotAttempted_DetailCarriesTheReason()
    {
        MovementExecutionEvidence evidence = MovementExecutionEvidence.NotAttempted(TestMapId, RequestedTile, "guard_refused");

        Assert.Equal("guard_refused", evidence.Detail);
    }

    [Fact]
    public void NotAttempted_UsesTheGivenInstant_NeverWallClock()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;

        MovementExecutionEvidence evidence = MovementExecutionEvidence.NotAttempted(TestMapId, RequestedTile, "reason", fixedInstant);

        Assert.Equal(fixedInstant, evidence.ObservedAtUtc);
        Assert.Equal(fixedInstant, evidence.Observed.ObservedAtUtc);
    }

    [Fact]
    public void NotAttempted_SucceededIsFalse()
    {
        MovementExecutionEvidence evidence = MovementExecutionEvidence.NotAttempted(TestMapId, RequestedTile, "reason");

        Assert.False(evidence.Succeeded);
    }

    [Fact]
    public void Succeeded_TrueOnlyForSucceededResult()
    {
        var succeeded = new MovementExecutionEvidence(
            TestMapId,
            RequestedTile,
            WorldFact<TileCoordinate>.Live(RequestedTile, confidence: 1d, DateTime.UnixEpoch),
            MovementExecutionResult.Succeeded,
            Detail: null,
            DateTime.UnixEpoch);

        var displaced = succeeded with { Result = MovementExecutionResult.Displaced };

        Assert.True(succeeded.Succeeded);
        Assert.False(displaced.Succeeded);
    }

    [Fact]
    public void RecordEquality_ComparesAllFields()
    {
        DateTime fixedInstant = DateTime.UnixEpoch;
        WorldFact<TileCoordinate> observed = WorldFact<TileCoordinate>.Live(RequestedTile, 1d, fixedInstant);

        var first = new MovementExecutionEvidence(TestMapId, RequestedTile, observed, MovementExecutionResult.Succeeded, null, fixedInstant);
        var second = new MovementExecutionEvidence(TestMapId, RequestedTile, observed, MovementExecutionResult.Succeeded, null, fixedInstant);

        Assert.Equal(first, second);
    }
}
