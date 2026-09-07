using NosAi.Core.Memory;
using NosAi.Core.Safety;
using Xunit;

namespace NosAi.Core.Tests.Planning;

public sealed class RecoveryAndEvaluationTests
{
    [Fact]
    public void RecoveryIsFailClosedAfterRetryBudget()
    {
        var r = new RetryBudgetController(new RetryBudgetPolicy(TimeSpan.FromSeconds(1), 2, TimeSpan.Zero));
        Assert.True(r.OnObservationTimeout());
        Assert.True(r.OnTransientFailure());
        Assert.False(r.OnTransientFailure());
        Assert.Equal(RetryBudgetState.SafeStop, r.State);
    }

    /// <summary>
    /// Il fermo non si compra indietro fallendo abbastanza volte.
    /// </summary>
    /// <remarks>
    /// <c>_retries</c> e' un <c>byte</c>. Finche' il controllo guardava solo il
    /// tetto, 256 fallimenti dopo il fermo riportavano il contatore a zero, il tetto
    /// tornava a passare e <c>SafeStop</c> si scioglieva da solo -- riferendo che un
    /// altro tentativo era permesso, nel momento in cui le cose andavano peggio. Il
    /// numero qui sotto e' 300 perche' e' oltre l'unico giro possibile di quel byte.
    /// </remarks>
    [Fact]
    public void SafeStopSurvivesEnoughFailuresToWrapTheRetryCounter()
    {
        var r = new RetryBudgetController(new RetryBudgetPolicy(TimeSpan.FromSeconds(1), 2, TimeSpan.Zero));
        Assert.True(r.OnTransientFailure());
        Assert.True(r.OnTransientFailure());
        Assert.False(r.OnTransientFailure());
        Assert.Equal(RetryBudgetState.SafeStop, r.State);

        for (int i = 0; i < 300; i++)
        {
            Assert.False(r.OnTransientFailure());
            Assert.False(r.OnObservationTimeout());
            Assert.Equal(RetryBudgetState.SafeStop, r.State);
        }
    }

    /// <summary>
    /// Solo <c>OnRecovered</c> toglie il fermo, ed e' un atto di chi lo chiama.
    /// </summary>
    [Fact]
    public void OnlyAnExplicitRecoveryLiftsTheStop()
    {
        var r = new RetryBudgetController(new RetryBudgetPolicy(TimeSpan.FromSeconds(1), 0, TimeSpan.Zero));
        Assert.False(r.OnTransientFailure());
        Assert.Equal(RetryBudgetState.SafeStop, r.State);

        r.OnRecovered();

        Assert.Equal(RetryBudgetState.Healthy, r.State);
        Assert.False(r.OnTransientFailure());
    }

    [Fact]
    public void ReasoningMemoryMayRecordUnknownButGameplayMemoryMayNot()
    {
        var store = new InMemoryStore();
        Assert.True(store.Append(new MemoryRecord(Guid.NewGuid(), MemoryType.Reasoning, MemoryProvenance.Unknown, .5f, 1, 1, 1, "hypothesis", "unknown", false)));
        Assert.False(store.Append(new MemoryRecord(Guid.NewGuid(), MemoryType.Episodic, MemoryProvenance.Unknown, .5f, 1, 1, 1, "state", "unknown", false)));
    }
}
