using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldActionProjectorTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;
    private static readonly ActionId AnyActionId = new("action-1");

    private static CombatActionCandidate UseSkillCandidate() =>
        new(CombatActionKind.UseSkill, target: new EntityId("mob-1"), skill: new SkillId("skill-201"));

    private static CombatExecutionEvidence CombatEvidence(CombatExecutionResult result, string? detail = null) =>
        new(
            UseSkillCandidate(),
            ResourceKind.Mana,
            WorldFact<double>.Live(50, 1d, Now),
            result == CombatExecutionResult.ResourceCostConfirmed ? WorldFact<double>.Live(40, 1d, Now) : WorldFact<double>.Live(50, 1d, Now),
            result,
            detail,
            Now);

    [Theory]
    [InlineData(CombatExecutionResult.ResourceCostConfirmed)]
    [InlineData(CombatExecutionResult.ResourceGainConfirmed)]
    public void FromCombat_ResourceChangeConfirmed_MapsToSucceeded(CombatExecutionResult result)
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(result));

        Assert.True(action.Outcome.HasValue);
        Assert.Equal(ActionOutcome.Succeeded, action.Outcome.Value);
    }

    [Fact]
    public void FromCombat_NoResourceChangeObserved_MapsToFailed()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.NoResourceChangeObserved));

        Assert.True(action.Outcome.HasValue);
        Assert.Equal(ActionOutcome.Failed, action.Outcome.Value);
    }

    [Theory]
    [InlineData(CombatExecutionResult.Unobserved)]
    [InlineData(CombatExecutionResult.Aborted)]
    public void FromCombat_UnobservedOrAborted_MapsToUnknown_NeverGuessedIntoASettledOutcome(CombatExecutionResult result)
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(result, "guard_refused"));

        Assert.False(action.Outcome.HasValue);
        Assert.Equal("guard_refused", action.Outcome.Reason);
    }

    [Fact]
    public void FromCombat_MissingDetail_FallsBackToANamedReason_NeverNull()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.Unobserved));

        Assert.False(action.Outcome.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(action.Outcome.Reason));
    }

    [Fact]
    public void FromCombat_KindIsExactlyTheCandidatesOwnKind_NeverFabricated()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));

        Assert.True(action.Kind.HasValue);
        Assert.Equal(nameof(CombatActionKind.UseSkill), action.Kind.Value);
    }

    [Fact]
    public void FromCombat_IdIsPassedThroughUnchanged()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));

        Assert.Equal(AnyActionId, action.Id);
    }

    private static MovementExecutionEvidence MovementEvidence(MovementExecutionResult result, string? detail = null) =>
        new(
            new MapId("map-1"),
            new TileCoordinate(1, 1),
            result == MovementExecutionResult.Succeeded
                ? WorldFact<TileCoordinate>.Live(new TileCoordinate(1, 1), 1d, Now)
                : WorldFact<TileCoordinate>.Unknown(detail ?? "not_confirmed", Now),
            result,
            detail,
            Now);

    [Fact]
    public void FromMovement_Succeeded_MapsToSucceeded()
    {
        WorldAction action = WorldActionProjector.FromMovement(AnyActionId, "scout-step", Now, MovementEvidence(MovementExecutionResult.Succeeded));

        Assert.True(action.Outcome.HasValue);
        Assert.Equal(ActionOutcome.Succeeded, action.Outcome.Value);
    }

    [Theory]
    [InlineData(MovementExecutionResult.Stalled)]
    [InlineData(MovementExecutionResult.Displaced)]
    public void FromMovement_StalledOrDisplaced_MapsToFailed_ThatStepDidNotReachTheRequestedTile(MovementExecutionResult result)
    {
        WorldAction action = WorldActionProjector.FromMovement(AnyActionId, "scout-step", Now, MovementEvidence(result));

        Assert.True(action.Outcome.HasValue);
        Assert.Equal(ActionOutcome.Failed, action.Outcome.Value);
    }

    [Theory]
    [InlineData(MovementExecutionResult.Unobserved)]
    [InlineData(MovementExecutionResult.Aborted)]
    public void FromMovement_UnobservedOrAborted_MapsToUnknown(MovementExecutionResult result)
    {
        WorldAction action = WorldActionProjector.FromMovement(AnyActionId, "scout-step", Now, MovementEvidence(result, "walk_guard_refused"));

        Assert.False(action.Outcome.HasValue);
        Assert.Equal("walk_guard_refused", action.Outcome.Reason);
    }

    [Fact]
    public void FromMovement_KindIsCallerSupplied_NeverFabricated()
    {
        WorldAction action = WorldActionProjector.FromMovement(AnyActionId, "collect-step", Now, MovementEvidence(MovementExecutionResult.Succeeded));

        Assert.Equal("collect-step", action.Kind.Value);
    }

    [Fact]
    public void FromMovement_BlankKind_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WorldActionProjector.FromMovement(AnyActionId, "  ", Now, MovementEvidence(MovementExecutionResult.Succeeded)));
    }

    [Fact]
    public void ToLedgerEntry_CopiesActionIdAndOutcomeVerbatim_NeverRederived()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));

        ActionOutcomeLedgerEntry entry = WorldActionProjector.ToLedgerEntry(action, MemoryType.Combat, "skill-201", Now);

        Assert.Equal(action.Id, entry.ActionId);
        Assert.Equal(action.Outcome, entry.Outcome);
        Assert.Equal(MemoryType.Combat, entry.Category);
        Assert.Equal("skill-201", entry.Context);
    }

    [Fact]
    public void ToLedgerEntry_GeneratesAnEntryId_WhenNoneGiven()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));

        ActionOutcomeLedgerEntry entry = WorldActionProjector.ToLedgerEntry(action, MemoryType.Combat, "skill-201", Now);

        Assert.NotEqual(Guid.Empty, entry.EntryId);
    }

    [Fact]
    public void ToLedgerEntry_UsesTheGivenEntryId_WhenProvided()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));
        Guid entryId = Guid.NewGuid();

        ActionOutcomeLedgerEntry entry = WorldActionProjector.ToLedgerEntry(action, MemoryType.Combat, "skill-201", Now, entryId);

        Assert.Equal(entryId, entry.EntryId);
    }

    [Fact]
    public void ToLedgerEntry_BlankContext_Throws()
    {
        WorldAction action = WorldActionProjector.FromCombat(AnyActionId, UseSkillCandidate(), Now, CombatEvidence(CombatExecutionResult.ResourceCostConfirmed));

        Assert.Throws<ArgumentException>(() => WorldActionProjector.ToLedgerEntry(action, MemoryType.Combat, "  ", Now));
    }
}
