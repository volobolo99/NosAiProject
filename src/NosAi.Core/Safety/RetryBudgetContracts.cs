namespace NosAi.Core.Safety;

/// <summary>How a retry budget is currently treating whatever it guards.</summary>
/// <remarks>
/// <b>Not the runtime's recovery state.</b> The authoritative one is
/// <c>NosAi.Runtime.Safety.RecoveryState</c> (<c>Closed</c>, <c>Throttled</c>,
/// <c>Halted</c>, <c>Probing</c>), a circuit breaker with a failure window, a
/// doubling cooldown and a probe phase; the architecture requires it to live there
/// ("Runtime is authoritative for authorization and safety"). This one counts
/// retries against a ceiling and stops. Both were called <c>RecoveryState</c> until
/// 2026-09-07, in two namespaces, which meant a <c>using NosAi.Core.Safety;</c>
/// compiled and silently took the weaker one. See <c>PIANO_DI_RIORDINO.md § R1</c>.
/// </remarks>
public enum RetryBudgetState : byte { Healthy, Degraded, Recovering, SafeStop }

/// <summary>How many retries the budget allows, and how long it waits between them.</summary>
public readonly record struct RetryBudgetPolicy(TimeSpan ObservationTimeout, byte MaxRetries, TimeSpan RetryDelay);

/// <summary>A retry budget: it says whether another attempt is still allowed.</summary>
/// <remarks>
/// Distinct from <c>NosAi.Runtime.Safety.RecoveryController</c>, which is the
/// authoritative circuit breaker. This is the pure contract sketch in
/// <c>NosAi.Core</c>, reached today by tests only.
/// </remarks>
public interface IRetryBudgetController
{
    /// <summary>The budget's current state. <c>SafeStop</c> is terminal.</summary>
    RetryBudgetState State { get; }

    /// <summary>Spends one retry after an observation timed out. False once the budget is gone.</summary>
    bool OnObservationTimeout();

    /// <summary>Spends one retry after a transient failure. False once the budget is gone.</summary>
    bool OnTransientFailure();

    /// <summary>Restores the full budget after the guarded thing worked again.</summary>
    void OnRecovered();

    /// <summary>Stops, without spending a retry and without asking.</summary>
    void ForceSafeStop();
}

/// <summary>
/// The reference implementation of a retry budget: N failures, then a stop that
/// only <see cref="OnRecovered"/> can lift.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>RetryBudgetController</c> since 2026-09-07, not <c>RecoveryController</c>:
/// the recovery controller of this project is the circuit breaker in
/// <c>NosAi.Runtime.Safety</c>, and while the two shared a name, reaching for one
/// and getting the other compiled without a word.
/// </para>
/// <para>
/// <b>SafeStop is terminal by construction.</b> The earlier version tested only the
/// retry ceiling, and <c>_retries</c> is a <see cref="byte"/>: 256 further failures
/// after the stop wrapped the counter back to zero, the ceiling test passed again,
/// and the controller left <c>SafeStop</c> on its own and reported that another
/// attempt was allowed. A stop that undoes itself after enough failures is a stop
/// that fails open exactly when things are going worst.
/// </para>
/// </remarks>
public sealed class RetryBudgetController : IRetryBudgetController
{
    private readonly RetryBudgetPolicy _policy;
    private byte _retries;

    /// <inheritdoc />
    public RetryBudgetState State { get; private set; } = RetryBudgetState.Healthy;

    /// <summary>Creates a budget with the retries that policy allows.</summary>
    public RetryBudgetController(RetryBudgetPolicy policy) => _policy = policy;

    /// <inheritdoc />
    public bool OnObservationTimeout() => Spend(RetryBudgetState.Recovering);

    /// <inheritdoc />
    public bool OnTransientFailure() => Spend(RetryBudgetState.Degraded);

    /// <inheritdoc />
    public void OnRecovered() { _retries = 0; State = RetryBudgetState.Healthy; }

    /// <inheritdoc />
    public void ForceSafeStop() { State = RetryBudgetState.SafeStop; }

    private bool Spend(RetryBudgetState onSpent)
    {
        // Read the stop before the counter, so no number of further failures can
        // wrap a byte past the ceiling and buy back a retry the budget refused.
        if (State == RetryBudgetState.SafeStop) return false;

        if (++_retries > _policy.MaxRetries) { ForceSafeStop(); return false; }

        State = onSpent;
        return true;
    }
}
