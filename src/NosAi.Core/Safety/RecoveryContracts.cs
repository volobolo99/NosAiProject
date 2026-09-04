namespace NosAi.Core.Safety;

public enum RecoveryState : byte { Healthy, Degraded, Recovering, SafeStop }

public readonly record struct RecoveryPolicy(TimeSpan ObservationTimeout, byte MaxRetries, TimeSpan RetryDelay);

public interface IRecoveryController
{
    RecoveryState State { get; }
    bool OnObservationTimeout();
    bool OnTransientFailure();
    void OnRecovered();
    void ForceSafeStop();
}

public sealed class RecoveryController : IRecoveryController
{
    private readonly RecoveryPolicy _policy;
    private byte _retries;
    public RecoveryState State { get; private set; } = RecoveryState.Healthy;

    public RecoveryController(RecoveryPolicy policy) => _policy = policy;

    public bool OnObservationTimeout()
    {
        if (++_retries > _policy.MaxRetries) { ForceSafeStop(); return false; }
        State = RecoveryState.Recovering;
        return true;
    }

    public bool OnTransientFailure()
    {
        if (++_retries > _policy.MaxRetries) { ForceSafeStop(); return false; }
        State = RecoveryState.Degraded;
        return true;
    }

    public void OnRecovered() { _retries = 0; State = RecoveryState.Healthy; }
    public void ForceSafeStop() { State = RecoveryState.SafeStop; }
}
