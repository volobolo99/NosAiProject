using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.Memory;

public sealed class MemoryTypeExtensionTests
{
    [Theory]
    [InlineData(MemoryType.Spatial)]
    [InlineData(MemoryType.Combat)]
    [InlineData(MemoryType.Quest)]
    [InlineData(MemoryType.Character)]
    [InlineData(MemoryType.Failure)]
    public void NewCategories_AreDefined(MemoryType kind)
    {
        Assert.True(Enum.IsDefined(kind));
    }

    [Fact]
    public void OriginalCategories_KeepTheirNumericValues()
    {
        Assert.Equal(0, (byte)MemoryType.Working);
        Assert.Equal(1, (byte)MemoryType.Episodic);
        Assert.Equal(2, (byte)MemoryType.Semantic);
        Assert.Equal(3, (byte)MemoryType.Procedural);
        Assert.Equal(4, (byte)MemoryType.Reasoning);
    }
}

public sealed class LocalOutcomeSimulatorTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    private static ActionOutcomeLedgerEntry BuildEntry(string context, ActionOutcome outcome) =>
        new(Guid.NewGuid(), new ActionId(Guid.NewGuid().ToString("N")), MemoryType.Combat, WorldFact<ActionOutcome>.Live(outcome, 1d, Now), context, Now);

    private static ActionOutcomeLedgerEntry BuildUnknownEntry(string context, string reason) =>
        new(Guid.NewGuid(), new ActionId(Guid.NewGuid().ToString("N")), MemoryType.Combat, WorldFact<ActionOutcome>.Unknown(reason, Now), context, Now);

    [Fact]
    public void Predict_EmptyLedger_ReturnsZeroCountsAndNullRate()
    {
        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", EquatableArray<ActionOutcomeLedgerEntry>.Empty);

        Assert.Equal(0, prediction.SucceededCount);
        Assert.Equal(0, prediction.FailedCount);
        Assert.Equal(0, prediction.InProgressCount);
        Assert.Equal(0, prediction.UnknownCount);
        Assert.Null(prediction.SuccessRate);
    }

    [Fact]
    public void Predict_UnknownOutcomeEntries_CountedSeparately_NeverAsSettledOrInProgress()
    {
        var ledger = EquatableArray<ActionOutcomeLedgerEntry>.From(new[]
        {
            BuildEntry("skill-201", ActionOutcome.Succeeded),
            BuildUnknownEntry("skill-201", "guard_refused"),
            BuildUnknownEntry("skill-201", "verification_window_closed"),
        });

        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", ledger);

        Assert.Equal(1, prediction.SucceededCount);
        Assert.Equal(0, prediction.FailedCount);
        Assert.Equal(0, prediction.InProgressCount);
        Assert.Equal(2, prediction.UnknownCount);
        Assert.Equal(1.0, prediction.SuccessRate);
    }

    [Fact]
    public void Predict_OnlyInProgressEntries_RateStaysNull()
    {
        var ledger = EquatableArray<ActionOutcomeLedgerEntry>.From(new[]
        {
            BuildEntry("skill-201", ActionOutcome.InProgress),
            BuildEntry("skill-201", ActionOutcome.InProgress),
        });

        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", ledger);

        Assert.Equal(2, prediction.InProgressCount);
        Assert.Null(prediction.SuccessRate);
    }

    [Fact]
    public void Predict_MixedOutcomes_ComputesRateFromSettledOnly()
    {
        var ledger = EquatableArray<ActionOutcomeLedgerEntry>.From(new[]
        {
            BuildEntry("skill-201", ActionOutcome.Succeeded),
            BuildEntry("skill-201", ActionOutcome.Succeeded),
            BuildEntry("skill-201", ActionOutcome.Succeeded),
            BuildEntry("skill-201", ActionOutcome.Failed),
            BuildEntry("skill-201", ActionOutcome.InProgress),
        });

        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", ledger);

        Assert.Equal(3, prediction.SucceededCount);
        Assert.Equal(1, prediction.FailedCount);
        Assert.Equal(1, prediction.InProgressCount);
        Assert.Equal(0.75, prediction.SuccessRate);
    }

    [Fact]
    public void Predict_IgnoresEntriesForOtherContexts()
    {
        var ledger = EquatableArray<ActionOutcomeLedgerEntry>.From(new[]
        {
            BuildEntry("skill-201", ActionOutcome.Succeeded),
            BuildEntry("skill-999", ActionOutcome.Failed),
        });

        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", ledger);

        Assert.Equal(1, prediction.SucceededCount);
        Assert.Equal(0, prediction.FailedCount);
    }

    [Fact]
    public void Predict_ContextMatchIsOrdinal_CaseSensitive()
    {
        var ledger = EquatableArray<ActionOutcomeLedgerEntry>.From(new[]
        {
            BuildEntry("Skill-201", ActionOutcome.Succeeded),
        });

        LocalOutcomePrediction prediction = LocalOutcomeSimulator.Predict("skill-201", ledger);

        Assert.Equal(0, prediction.SucceededCount);
    }
}
