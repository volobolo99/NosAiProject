using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Fusion;

namespace NosAi.Runtime.WorldModel;

/// <summary>
/// One pass over the four uncomposed channels. A null channel is "not bound".
/// </summary>
public readonly record struct ObservationChannelReads(
    GameplayObservation? Network,
    GameplayObservation? Memory,
    ScreenWorldReading? Screen,
    LocalWorldReading? Local);

/// <summary>
/// Builds a <see cref="FusedWorldObservation"/> from existing snapshot/provider
/// seams without relabelling a mixed observation as a single sensor.
/// </summary>
/// <remarks>
/// <para>
/// The live host today composes Network + optional Memory/Screen target
/// decorators into one <see cref="GameplayObservation"/>. Fusion forbids that
/// mix from being labelled Network: the wire does not establish
/// <c>HasTarget</c>. This feed therefore splits one composed reading into a
/// network-shaped vitals observation (HasTarget stripped) and a memory-shaped
/// observation that carries only map/cell and establishing fields that already
/// have a value.
/// </para>
/// <para>
/// It does not invent numbers. A missing provider is a null channel. A throwing
/// reader becomes an Unobserved observation with the exception type in the
/// reason.
/// </para>
/// </remarks>
public sealed class ObservationSnapshotFusionFeed
{
    public const string NetworkReaderFailedPrefix = "network_reader_failed";
    public const string MemoryReaderFailedPrefix = "memory_reader_failed";
    public const string LocalReaderFailedPrefix = "local_reader_failed";
    public const string MemoryFieldNotOnChannelReason = "memory_field_not_on_this_channel";

    private readonly Func<ObservationChannelReads> _read;
    private readonly TimeProvider _clock;

    public ObservationSnapshotFusionFeed(Func<ObservationChannelReads> read, TimeProvider? clock = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Fuses the current channel reads. Missing evidence stays UNKNOWN.</summary>
    public FusedWorldObservation Capture(FusionPolicy? policy = null, DateTime? nowUtc = null)
    {
        DateTime now = nowUtc ?? _clock.GetUtcNow().UtcDateTime;
        ObservationChannelReads reads = _read();
        return DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(reads.Network, reads.Memory, reads.Screen, reads.Local),
            now,
            policy);
    }

    /// <summary>
    /// Host composition: one Observe() of the bound gameplay provider, one map
    /// read, attach from the snapshot, no separate screen channel.
    /// </summary>
    public static ObservationSnapshotFusionFeed ForExistingHost(
        Func<IGameplayProvider?> networkProvider,
        Func<MapWorldObservation> mapWorld,
        Func<ClassifiedValue<bool>> clientAttached,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(networkProvider);
        ArgumentNullException.ThrowIfNull(mapWorld);
        ArgumentNullException.ThrowIfNull(clientAttached);

        return new ObservationSnapshotFusionFeed(
            () => ReadHost(networkProvider, mapWorld, clientAttached),
            clock);
    }

    /// <summary>
    /// Splits a Gate 1 snapshot's already-composed gameplay into uncomposed
    /// fusion channels. Used when the source has the snapshot and no live
    /// provider handle.
    /// </summary>
    public static FusedWorldObservation FuseExistingSnapshot(
        GameplayObservation? composed,
        MapWorldObservation mapWorld,
        ClassifiedValue<bool> clientAttached,
        DateTime nowUtc,
        ScreenWorldReading? screen = null,
        FusionPolicy? policy = null)
    {
        ObservationChannelReads reads = Split(composed, mapWorld, clientAttached, screen, nowUtc);
        return DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(reads.Network, reads.Memory, reads.Screen, reads.Local),
            nowUtc,
            policy);
    }

    internal static ObservationChannelReads Split(
        GameplayObservation? composed,
        MapWorldObservation mapWorld,
        ClassifiedValue<bool> clientAttached,
        ScreenWorldReading? screen,
        DateTime nowUtc)
        => new(
            Network: StripNetworkEstablishing(composed),
            Memory: BuildMemory(mapWorld, composed),
            Screen: screen,
            Local: SensorNormalizer.FromLocal(clientAttached, null, null, nowUtc));

    private static ObservationChannelReads ReadHost(
        Func<IGameplayProvider?> networkProvider,
        Func<MapWorldObservation> mapWorld,
        Func<ClassifiedValue<bool>> clientAttached)
    {
        DateTime now = DateTime.UtcNow;
        GameplayObservation? composed = ObserveProvider(networkProvider, now);
        MapWorldObservation map = ReadMap(mapWorld);
        ClassifiedValue<bool> attached = ReadAttached(clientAttached, now);
        return Split(composed, map, attached, screen: null, now);
    }

    private static GameplayObservation? ObserveProvider(Func<IGameplayProvider?> networkProvider, DateTime now)
    {
        IGameplayProvider? provider;
        try
        {
            provider = networkProvider();
        }
        catch (Exception ex)
        {
            return GameplayObservation.Unobserved($"{NetworkReaderFailedPrefix}:{ex.GetType().Name}", now);
        }

        if (provider is null)
            return null;

        try
        {
            return provider.Observe();
        }
        catch (Exception ex)
        {
            return GameplayObservation.Unobserved($"{NetworkReaderFailedPrefix}:{ex.GetType().Name}", now);
        }
    }

    private static MapWorldObservation ReadMap(Func<MapWorldObservation> mapWorld)
    {
        try
        {
            return mapWorld();
        }
        catch (Exception ex)
        {
            return MapWorldObservation.Unknown($"{MemoryReaderFailedPrefix}:{ex.GetType().Name}");
        }
    }

    private static ClassifiedValue<bool> ReadAttached(Func<ClassifiedValue<bool>> clientAttached, DateTime now)
    {
        try
        {
            return clientAttached();
        }
        catch (Exception ex)
        {
            return new ClassifiedValue<bool>(
                default,
                DataSourceKind.Unknown,
                now,
                false,
                null,
                $"{LocalReaderFailedPrefix}:{ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Network may carry vitals, entities and combat. It must not establish
    /// HasTarget (ADR-0018 / ADR-0021). The original classified HasTarget is
    /// moved to the Memory channel when it already has a value.
    /// </summary>
    private static GameplayObservation? StripNetworkEstablishing(GameplayObservation? composed)
    {
        if (composed is null)
            return null;

        return composed with
        {
            HasTarget = ClassifiedValue<bool>.Unknown(
                DeterministicSensorFusion.NetworkDoesNotEstablishHasTargetReason)
        };
    }

    private static GameplayObservation? BuildMemory(MapWorldObservation map, GameplayObservation? composed)
    {
        bool mapKnown = map.MapId.HasValue || map.StandingCell.HasValue;
        bool targetKnown = composed is { HasTarget.HasValue: true };
        bool positionKnown = composed is { PlayerPosition.HasValue: true };
        if (!mapKnown && !targetKnown && !positionKnown)
            return null;

        DateTime at = composed?.ObservedAtUtc ?? DateTime.UtcNow;
        return GameplayObservation.Unobserved(MemoryFieldNotOnChannelReason, at) with
        {
            MapId = map.MapId,
            StandingCell = map.StandingCell,
            HasTarget = composed?.HasTarget
                ?? ClassifiedValue<bool>.Unknown(GameplayObservation.NotPublishedReason),
            PlayerPosition = composed?.PlayerPosition
                ?? ClassifiedValue<MapPoint>.Unknown(GameplayObservation.PlayerPositionNotReadReason)
        };
    }
}
