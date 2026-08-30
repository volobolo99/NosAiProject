using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// The world state as actually read back after an action, for the verify step.
/// </summary>
/// <param name="Hp">Observed HP, or UNKNOWN when nothing could be read.</param>
/// <param name="Mp">Observed MP, or UNKNOWN when nothing could be read.</param>
/// <remarks>
/// Both fields are classified, because "not observed" and "observed as zero" have
/// to stay distinguishable all the way into the verifier. A verifier that reads
/// UNKNOWN as 0 would confirm a prediction of death every time the observer was
/// simply unavailable.
/// </remarks>
public sealed record ObservedState(ClassifiedValue<int> Hp, ClassifiedValue<int> Mp)
{
    /// <summary>Whether both readings came from a real observation.</summary>
    public bool IsFullyObserved =>
        Hp.Source == DataSourceKind.Live && Mp.Source == DataSourceKind.Live && Hp.HasValue && Mp.HasValue;

    public static ObservedState Unobserved(string reason) =>
        new(ClassifiedValue<int>.Unknown(reason), ClassifiedValue<int>.Unknown(reason));

    public static ObservedState Live(int hp, int mp, DateTime? observedAtUtc = null) =>
        new(ClassifiedValue<int>.Live(hp, observedAtUtc), ClassifiedValue<int>.Live(mp, observedAtUtc));
}

/// <summary>
/// Reads the world back after an action so the prediction can be checked against
/// something other than itself.
/// </summary>
/// <remarks>
/// Gate 3 previously computed the post-state by applying the prediction's own
/// deltas and then compared that to the prediction. Verification therefore passed
/// by construction and could never detect a discrepancy — the safety net of the
/// closed loop was a tautology. This seam is what makes the check real: the
/// verifier compares a prediction against an observation, and where there is no
/// observer there is no confirmation.
/// </remarks>
public interface IWorldStateObserver
{
    /// <summary>Whether this observer can read the world at all.</summary>
    bool CanObserve { get; }

    Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The observer used when no real perception source is bound.
/// </summary>
/// <remarks>
/// It returns UNKNOWN rather than a plausible number. Every cycle it takes part in
/// ends unverified, which is the honest outcome: without an observation the
/// runtime does not know whether the action had the predicted effect.
/// </remarks>
public sealed class UnavailableWorldStateObserver : IWorldStateObserver
{
    private readonly string _reason;

    public UnavailableWorldStateObserver(string reason = "world_state_observer_not_bound") => _reason = reason;

    public bool CanObserve => false;

    public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ObservedState.Unobserved(_reason));
}

/// <summary>
/// An observer backed by a delegate, for wiring a real perception source in.
/// </summary>
/// <remarks>
/// A throwing source yields UNKNOWN rather than propagating: a perception fault
/// must leave the cycle unverified, not tear down the pipeline.
/// </remarks>
public sealed class DelegateWorldStateObserver : IWorldStateObserver
{
    private readonly Func<CancellationToken, Task<ObservedState>> _read;

    public DelegateWorldStateObserver(Func<CancellationToken, Task<ObservedState>> read)
        => _read = read ?? throw new ArgumentNullException(nameof(read));

    public bool CanObserve => true;

    public async Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _read(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ObservedState.Unobserved($"observer_failed:{ex.GetType().Name}");
        }
    }
}
