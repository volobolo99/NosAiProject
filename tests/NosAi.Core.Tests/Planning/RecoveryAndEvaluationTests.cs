using NosAi.Core.Memory;
using NosAi.Core.Safety;
using Xunit;

namespace NosAi.Core.Tests.Planning;

public sealed class RecoveryAndEvaluationTests
{
    [Fact]
    public void RecoveryIsFailClosedAfterRetryBudget()
    {
        var r = new RecoveryController(new RecoveryPolicy(TimeSpan.FromSeconds(1), 2, TimeSpan.Zero));
        Assert.True(r.OnObservationTimeout());
        Assert.True(r.OnTransientFailure());
        Assert.False(r.OnTransientFailure());
        Assert.Equal(RecoveryState.SafeStop, r.State);
    }

    [Fact]
    public void ReasoningMemoryMayRecordUnknownButGameplayMemoryMayNot()
    {
        var store = new InMemoryStore();
        Assert.True(store.Append(new MemoryRecord(Guid.NewGuid(), MemoryType.Reasoning, MemoryProvenance.Unknown, .5f, 1, 1, 1, "hypothesis", "unknown", false)));
        Assert.False(store.Append(new MemoryRecord(Guid.NewGuid(), MemoryType.Episodic, MemoryProvenance.Unknown, .5f, 1, 1, 1, "state", "unknown", false)));
    }
}
