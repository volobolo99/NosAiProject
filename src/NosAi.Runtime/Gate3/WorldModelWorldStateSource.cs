using NosAi.LiveIntegration;
using NosAi.Runtime.Perception.Fusion;
using NosAi.Runtime.WorldModel;
using Gate1CanonicalSnapshot = NosAi.Runtime.Gate1.Gate1CanonicalSnapshot;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// AP-01 runtime wiring: existing Gate 1 snapshots (and optional uncomposed
/// channels) become the classified World Model the decision loop plans from.
/// </summary>
/// <remarks>
/// <para>
/// Cognition still only proposes. This source reads and publishes. It does not
/// arm input, issue tokens, or bypass Guard/Trust/Safety.
/// </para>
/// <para>
/// Fusion is A2's job. This type calls
/// <see cref="ObservationSnapshotFusionFeed"/> and
/// <see cref="SensorFusionWorldAdapter"/>, then
/// <see cref="Gate3WorldState.FromObservation"/> so UNKNOWN stays UNKNOWN.
/// The coarse <see cref="IWorldModel"/> is updated only when the adapter can
/// project without fabricating a number.
/// </para>
/// </remarks>
public sealed class WorldModelWorldStateSource : IWorldStateSource
{
    public const string SnapshotFailedPrefix = "snapshot_failed";
    public const string FusionFailedPrefix = "fusion_feed_failed";
    public const string ClientNotAttachedReason = "client_not_attached";
    public const string GameplayUnavailableReason = "gameplay_provider_not_available";

    private readonly Func<Gate1CanonicalSnapshot>? _snapshot;
    private readonly Func<FusedWorldObservation>? _fused;
    private readonly ObservationSnapshotFusionFeed? _feed;
    private readonly Func<MapWorldObservation>? _mapWorld;
    private readonly IClassifiedWorldModel _classified;
    private readonly IWorldModel? _coarse;
    private readonly FusionPolicy? _policy;

    /// <summary>Plans from a Gate 1 snapshot, fusing it with the optional map-world read.</summary>
    public WorldModelWorldStateSource(
        Func<Gate1CanonicalSnapshot> snapshot,
        IClassifiedWorldModel classified,
        IWorldModel? coarse = null,
        Func<MapWorldObservation>? mapWorld = null,
        ObservationSnapshotFusionFeed? feed = null,
        FusionPolicy? policy = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _classified = classified ?? throw new ArgumentNullException(nameof(classified));
        _coarse = coarse;
        _mapWorld = mapWorld;
        _feed = feed;
        _policy = policy;
    }

    /// <summary>Plans from an already-fused observation. Used by tests and replay.</summary>
    public static WorldModelWorldStateSource FromFused(
        Func<FusedWorldObservation> fused,
        IClassifiedWorldModel classified,
        IWorldModel? coarse = null)
    {
        ArgumentNullException.ThrowIfNull(fused);
        ArgumentNullException.ThrowIfNull(classified);
        return new WorldModelWorldStateSource(fused, classified, coarse);
    }

    private WorldModelWorldStateSource(
        Func<FusedWorldObservation> fused,
        IClassifiedWorldModel classified,
        IWorldModel? coarse)
    {
        _fused = fused;
        _classified = classified;
        _coarse = coarse;
    }

    public IClassifiedWorldModel Classified => _classified;

    public Task<Gate3WorldState> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_fused is not null)
        {
            FusedWorldObservation fusedOverride;
            try
            {
                fusedOverride = _fused();
            }
            catch (Exception ex)
            {
                return Task.FromResult(PublishUnobserved($"{FusionFailedPrefix}:{ex.GetType().Name}"));
            }

            return Task.FromResult(PublishFused(fusedOverride));
        }

        Gate1CanonicalSnapshot snapshot;
        try
        {
            snapshot = _snapshot!();
        }
        catch (Exception ex)
        {
            return Task.FromResult(PublishUnobserved($"{SnapshotFailedPrefix}:{ex.GetType().Name}"));
        }

        if (snapshot.Client.Attached.Value != true)
        {
            return Task.FromResult(PublishUnobserved(
                snapshot.Client.Attached.FailureReason ?? ClientNotAttachedReason));
        }

        if (snapshot.Client.Gameplay is null && _feed is null)
        {
            return Task.FromResult(PublishUnobserved(
                snapshot.Client.GameplayBaseline.FailureReason ?? GameplayUnavailableReason));
        }

        FusedWorldObservation fused;
        try
        {
            fused = FuseSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            return Task.FromResult(PublishUnobserved($"{FusionFailedPrefix}:{ex.GetType().Name}"));
        }

        return Task.FromResult(PublishFused(fused));
    }

    private FusedWorldObservation FuseSnapshot(Gate1CanonicalSnapshot snapshot)
    {
        if (_feed is not null)
            return _feed.Capture(_policy, snapshot.CapturedAtUtc);

        MapWorldObservation map = ReadMap();
        return ObservationSnapshotFusionFeed.FuseExistingSnapshot(
            snapshot.Client.Gameplay,
            map,
            snapshot.Client.Attached,
            snapshot.CapturedAtUtc,
            screen: null,
            policy: _policy);
    }

    private MapWorldObservation ReadMap()
    {
        if (_mapWorld is null)
            return MapWorldObservation.Unknown(GameplayObservation.MapIdNotReadReason);

        try
        {
            return _mapWorld();
        }
        catch (Exception ex)
        {
            return MapWorldObservation.Unknown(
                $"{ObservationSnapshotFusionFeed.MemoryReaderFailedPrefix}:{ex.GetType().Name}");
        }
    }

    private Gate3WorldState PublishFused(FusedWorldObservation fused)
    {
        GameplayObservation observation = SensorFusionWorldAdapter.ToGameplayObservation(fused);
        Gate3WorldState planning = Gate3WorldState.FromObservation(observation);
        WorldModelProjection coarse = SensorFusionWorldAdapter.TryToWorldState(fused);
        _classified.Publish(planning, fused, coarse);
        if (coarse is { Succeeded: true, State: { } state })
            _coarse?.Update(state);
        return planning;
    }

    private Gate3WorldState PublishUnobserved(string reason)
    {
        Gate3WorldState planning = Gate3WorldState.Unobserved(reason);
        _classified.Publish(planning, fusion: null, coarse: null);
        return planning;
    }
}
