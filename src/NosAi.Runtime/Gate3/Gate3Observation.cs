using System.Linq;
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
    /// <remarks>
    /// The strictest tier, kept for a caller that wants it. It is no longer what
    /// gates verification: see <see cref="IsUsableForVerification"/> and VER-04.
    /// </remarks>
    public bool IsFullyObserved =>
        Hp.Source == DataSourceKind.Live && Mp.Source == DataSourceKind.Live && Hp.HasValue && Mp.HasValue;

    /// <summary>
    /// Whether this reading is good enough to verify an action with (VER-04).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The verification tier is not stricter than the actuation tier.</b> That
    /// was the fourth defect of docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md § 1: the
    /// verifier refused to conclude anything unless both readings were LIVE, while
    /// ADR-0016 § 2 had already settled that a runtime may <i>act</i> on LIVE,
    /// DERIVED or CACHED within the freshness bound. The two rules were never
    /// reconciled, so the runtime could act on a screen-derived reading and could
    /// never verify one — every cycle ADR-0018 made possible ended
    /// <c>Unverified</c>.
    /// </para>
    /// <para>
    /// The severity was on the wrong side. A reading too weak to verify with is
    /// too weak to act on; the converse does not follow. So this admits exactly
    /// what <see cref="Gate3WorldState.IsActionable"/> admits, measured the same
    /// way: real (not UNKNOWN, not SIMULATED) and no older than the bound.
    /// </para>
    /// </remarks>
    public bool IsUsableForVerification(DateTime nowUtc, TimeSpan maxAge)
    {
        if (maxAge < TimeSpan.Zero) return false;
        if (!Hp.HasValue && !Mp.HasValue) return false;
        if (Known().Any(v => v.Source == DataSourceKind.Simulated)) return false;

        DateTime oldest = Known().Min(v => v.ObservedAtUtc);
        TimeSpan age = nowUtc - oldest;
        // A reading stamped in the future is a clock disagreement, not a fresh
        // one, and is unusable rather than maximally recent.
        return age >= TimeSpan.Zero && age <= maxAge;
    }

    /// <summary>
    /// This reading as one element of a post-condition's series.
    /// </summary>
    /// <remarks>
    /// The vitals and nothing else, because this type holds nothing else. A card
    /// that needs the position, the entities or the inventory finds them UNKNOWN
    /// here and says so by name — which is the honest answer for a runtime whose
    /// only bound observer reads two numbers.
    /// </remarks>
    public Gate3WorldState ToWorldState() => new(
        Hp: Hp,
        MaxHp: ClassifiedValue<int>.Unknown("max_hp_not_read_back"),
        Mp: Mp,
        HasTarget: ClassifiedValue<bool>.Unknown("target_not_read_back"),
        InCombat: ClassifiedValue<bool>.Unknown("combat_not_read_back"));

    private IEnumerable<ClassifiedValue<int>> Known()
    {
        if (Hp.HasValue) yield return Hp;
        if (Mp.HasValue) yield return Mp;
    }

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
