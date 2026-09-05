namespace NosAi.LiveIntegration;

/// <summary>
/// Canonical read-only observation boundary between live client probes and the
/// runtime decision/world-model layers.
/// </summary>
/// <remarks>
/// This gateway deliberately composes existing live sources instead of creating
/// a second process scanner or gameplay protocol. It gives downstream consumers
/// one coherent observation point while preserving provenance through the source
/// snapshots themselves. It has no execution authority and never writes to the
/// game client.
/// </remarks>
public sealed record LiveObservationSnapshot(
    ClientBaselineSnapshot Client,
    GameplayObservation Gameplay,
    DateTime ObservedAtUtc)
{
    public bool IsClientAttached => Client.ClientAttached;

    public bool HasLiveGameplayObservation =>
        Gameplay.Hp.IsKnown ||
        Gameplay.MaxHp.IsKnown ||
        Gameplay.Mp.IsKnown ||
        Gameplay.MaxMp.IsKnown ||
        Gameplay.Entities.IsKnown ||
        Gameplay.PlayerPosition.IsKnown ||
        Gameplay.MapId.IsKnown ||
        Gameplay.StandingCell.IsKnown ||
        Gameplay.SelectedTarget.IsKnown;
}

/// <summary>
/// Reads the current live client baseline and gameplay observation as one
/// immutable snapshot. The caller owns scheduling; this type performs no polling
/// loop and therefore cannot create a hidden background execution path.
/// </summary>
public sealed class LiveObservationGateway
{
    private readonly RealClientConnector _client;
    private readonly IGameplayProvider _gameplay;

    public LiveObservationGateway(
        RealClientConnector client,
        IGameplayProvider gameplay)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
    }

    public LiveObservationSnapshot Capture()
    {
        ClientBaselineSnapshot client = _client.Observe();
        GameplayObservation gameplay = ObserveGameplaySafely();

        var observedAt = client.ObservedAtUtc >= gameplay.ObservedAtUtc
            ? client.ObservedAtUtc
            : gameplay.ObservedAtUtc;

        return new LiveObservationSnapshot(client, gameplay, observedAt);
    }

    private GameplayObservation ObserveGameplaySafely()
    {
        try
        {
            return _gameplay.Observe();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        {
            return GameplayObservation.Unobserved(
                $"provider_observation_failed:{ex.GetType().Name}",
                DateTime.UtcNow);
        }
    }
}
