using NosAi.Runtime.Gate3;

namespace NosAi.LiveIntegration;

/// <summary>
/// Reads the world back after an action through the gameplay provider already
/// attached. It does not keep a second cache: the provider already republishes
/// CACHED readings and ADR-0016 measures freshness on that age.
/// </summary>
public sealed class NetworkWorldStateObserver : IWorldStateObserver
{
    private readonly IGameplayProvider _provider;

    public NetworkWorldStateObserver(IGameplayProvider provider)
        => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <inheritdoc />
    public bool CanObserve => true;

    /// <inheritdoc />
    /// <remarks>
    /// Exceptions from the provider propagate. Swallowing them here would hide a
    /// perception fault; the orchestrator already treats a throwing observer as
    /// unverified.
    /// </remarks>
    public Task<ObservedState> ObserveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameplayObservation observation = _provider.Observe();
        return Task.FromResult(new ObservedState(observation.Hp, observation.Mp));
    }
}
