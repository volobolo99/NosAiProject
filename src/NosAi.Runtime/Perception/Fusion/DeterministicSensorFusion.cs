using System.Buffers;
using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Perception.Fusion;

/// <summary>
/// Fuses Network, Memory, Screen and Local observations into one classified
/// snapshot. Precedence, disagreement and freshness are total functions of the
/// inputs and <c>nowUtc</c> — two calls with the same arguments produce the
/// same bits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Precedence.</b> When usable samples agree, provenance rank first: LIVE,
/// then CACHED, then DERIVED, then SIMULATED. UNKNOWN is never a candidate.
/// At the same provenance rank a field-specific sensor order decides. A
/// value mismatch among trusted samples is a contradiction: the field
/// becomes UNKNOWN and both sides are recorded. Simulated samples are
/// ignored when a trusted (LIVE/CACHED/DERIVED) sample exists — they must
/// not veto a real reading and they must not be labelled as one.
/// </para>
/// <para>
/// <b>HasTarget.</b> Memory establishes (ADR-0021), the screen establishes
/// whether when memory is unbound (ADR-0018), the wire never establishes. A
/// <c>ct</c> newer than a Memory/Screen <c>false</c> is a contradiction, not
/// a target. A HasTarget sitting on the Network observation is never an
/// establishing sample.
/// </para>
/// <para>
/// <b>Freshness.</b> A usable sample older than the policy budget is not a
/// candidate. If every sample is stale, the field is UNKNOWN with a named
/// reason. The original observation instant is never rewritten to <c>nowUtc</c>.
/// </para>
/// <para>
/// <b>Allocations.</b> Candidate buffers are four-slot value types. Disagreement
/// records use a caller span or a rented 16-slot pool. Winning
/// <see cref="ClassifiedValue{T}"/> instances are reused when they need no
/// rewrite. The returned snapshot is one record plus a compact disagreement
/// array that is empty when nothing collided.
/// </para>
/// </remarks>
public static class DeterministicSensorFusion
{
    public const string SensorNotObservedReason = "sensor_not_observed";
    public const string FusedStalePrefix = "fused_stale:";
    public const string FutureObservationReason = "fused_future_observation";
    public const string SameRankDisagreementReason = "sensor_sources_disagree";
    public const string NetworkDoesNotEstablishHasTargetReason = "has_target_not_established_by_network";
    public const string OccupancyNeverObservedReason = OccupancyFreshness.NeverObservedReason;
    public const string OccupancyNotStampedReason = OccupancyFreshness.ViewNotStampedReason;
    public const string OccupancyFromFutureReason = OccupancyFreshness.ViewFromTheFutureReason;

    /// <summary>
    /// Fuses the four channels. Always returns a complete snapshot: missing
    /// evidence is UNKNOWN, never a default number or an empty world.
    /// </summary>
    public static FusedWorldObservation Fuse(
        in SensorObservationSet observations,
        DateTime nowUtc,
        FusionPolicy? policy = null)
    {
        FusionDisagreement[] rented = ArrayPool<FusionDisagreement>.Shared.Rent(FusionPolicy.MaxTrackedDisagreements);
        try
        {
            return Fuse(observations, nowUtc, policy, rented.AsSpan(0, FusionPolicy.MaxTrackedDisagreements), out _);
        }
        finally
        {
            ArrayPool<FusionDisagreement>.Shared.Return(rented, clearArray: true);
        }
    }

    /// <summary>
    /// Fuses into a caller-supplied disagreement buffer. Excess collisions past
    /// the buffer length are dropped; <paramref name="disagreementCount"/> is
    /// the number written, never more than the span.
    /// </summary>
    public static FusedWorldObservation Fuse(
        in SensorObservationSet observations,
        DateTime nowUtc,
        FusionPolicy? policy,
        Span<FusionDisagreement> disagreements,
        out int disagreementCount)
    {
        FusionPolicy rules = policy ?? FusionPolicy.Default;
        disagreementCount = 0;

        ScreenVitalPair? screenHp = observations.Screen?.Hp;
        ScreenVitalPair? screenMp = observations.Screen?.Mp;
        float hpConfidence = screenHp is { } hpPair ? (float)hpPair.Confidence : 1f;
        float mpConfidence = screenMp is { } mpPair ? (float)mpPair.Confidence : 1f;

        FusedFact<int> hp = FuseInt(FusionField.Hp, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.Hp, screenHp?.Current, hpConfidence);
        FusedFact<int> maxHp = FuseInt(FusionField.MaxHp, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.MaxHp, screenHp?.Maximum, hpConfidence);
        FusedFact<int> mp = FuseInt(FusionField.Mp, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.Mp, screenMp?.Current, mpConfidence);
        FusedFact<int> maxMp = FuseInt(FusionField.MaxMp, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.MaxMp, screenMp?.Maximum, mpConfidence);
        FusedFact<bool> inCombat = FuseBool(FusionField.InCombat, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.InCombat);
        FusedFact<int> entitiesInView = FuseInt(FusionField.EntitiesInView, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.EntitiesInView, null, 1f);
        FusedFact<IReadOnlyList<SelectableEntity>> entities = FuseEntities(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<MapPoint> playerPosition = FuseMapPoint(FusionField.PlayerPosition, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.PlayerPosition, observations.Screen?.ProjectedPlayerPosition);
        FusedFact<int> mapId = FuseInt(FusionField.MapId, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.MapId, null, 1f);
        FusedFact<MapPoint> standingCell = FuseMapPoint(FusionField.StandingCell, observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount, static o => o.StandingCell, null);
        FusedFact<Aggressor> hitBy = FuseAggressor(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<TargetedEntity> selectedTarget = FuseSelectedTarget(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<IReadOnlyList<SkillReady>> skills = FuseSkills(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<IReadOnlyList<InventorySlotReading>> inventory = FuseInventory(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<ItemPickup> lastPickup = FusePickup(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<IReadOnlyList<GroundItem>> ground = FuseGround(observations, nowUtc, rules.MaxAge, disagreements, ref disagreementCount);
        FusedFact<bool> hasTarget = FuseHasTarget(observations, nowUtc, rules.MaxAge, selectedTarget, disagreements, ref disagreementCount);
        FusedFact<bool> clientAttached = FuseClientAttached(observations, nowUtc, rules.MaxAge);
        FusedFact<bool> occupancyFresh = FuseOccupancy(observations, nowUtc, rules.OccupancyMaxAge);
        FusedFact<bool> pathFound = FuseNavigation(observations, nowUtc, rules.MaxAge);

        IReadOnlyList<FusionDisagreement> recorded = CopyDisagreements(disagreements, disagreementCount);
        return new FusedWorldObservation(
            nowUtc,
            hp,
            maxHp,
            mp,
            maxMp,
            hasTarget,
            inCombat,
            entitiesInView,
            entities,
            playerPosition,
            mapId,
            standingCell,
            hitBy,
            selectedTarget,
            skills,
            inventory,
            lastPickup,
            ground,
            clientAttached,
            occupancyFresh,
            pathFound,
            recorded);
    }

    private static FusedFact<int> FuseInt(
        FusionField field,
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount,
        Func<GameplayObservation, ClassifiedValue<int>> pick,
        ClassifiedValue<int>? screen,
        float screenConfidence)
    {
        SampleBox<int> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, pick);
        if (screen is { } extra)
            TryOffer(ref box, extra, SensorKind.Screen, screenConfidence, nowUtc, maxAge);
        FusedFact<int> fused = Finish(field, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(field, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<bool> FuseBool(
        FusionField field,
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount,
        Func<GameplayObservation, ClassifiedValue<bool>> pick)
    {
        SampleBox<bool> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, pick);
        FusedFact<bool> fused = Finish(field, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(field, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<MapPoint> FuseMapPoint(
        FusionField field,
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount,
        Func<GameplayObservation, ClassifiedValue<MapPoint>> pick,
        ClassifiedValue<MapPoint>? screen)
    {
        SampleBox<MapPoint> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, pick);
        if (screen is { } extra)
            TryOffer(ref box, extra, SensorKind.Screen, 1f, nowUtc, maxAge);
        FusedFact<MapPoint> fused = Finish(field, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(field, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<IReadOnlyList<SelectableEntity>> FuseEntities(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<IReadOnlyList<SelectableEntity>> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.Entities);
        FusedFact<IReadOnlyList<SelectableEntity>> fused = Finish(FusionField.Entities, box, nowUtc, SameEntitySet);
        RecordDisagreements(FusionField.Entities, box, disagreements, ref disagreementCount, SameEntitySet);
        return fused;
    }

    private static FusedFact<Aggressor> FuseAggressor(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<Aggressor> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.HitBy);
        FusedFact<Aggressor> fused = Finish(FusionField.HitBy, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(FusionField.HitBy, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<TargetedEntity> FuseSelectedTarget(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<TargetedEntity> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.SelectedTarget);
        FusedFact<TargetedEntity> fused = Finish(FusionField.SelectedTarget, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(FusionField.SelectedTarget, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<IReadOnlyList<SkillReady>> FuseSkills(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<IReadOnlyList<SkillReady>> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.SkillsReady);
        FusedFact<IReadOnlyList<SkillReady>> fused = Finish(FusionField.SkillsReady, box, nowUtc, SameSequence);
        RecordDisagreements(FusionField.SkillsReady, box, disagreements, ref disagreementCount, SameSequence);
        return fused;
    }

    private static FusedFact<IReadOnlyList<InventorySlotReading>> FuseInventory(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<IReadOnlyList<InventorySlotReading>> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.Inventory);
        FusedFact<IReadOnlyList<InventorySlotReading>> fused = Finish(FusionField.Inventory, box, nowUtc, SameSequence);
        RecordDisagreements(FusionField.Inventory, box, disagreements, ref disagreementCount, SameSequence);
        return fused;
    }

    private static FusedFact<ItemPickup> FusePickup(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<ItemPickup> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.LastPickup);
        FusedFact<ItemPickup> fused = Finish(FusionField.LastPickup, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(FusionField.LastPickup, box, disagreements, ref disagreementCount, static (a, b) => a == b);
        return fused;
    }

    private static FusedFact<IReadOnlyList<GroundItem>> FuseGround(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<IReadOnlyList<GroundItem>> box = default;
        CollectGameplay(observations, nowUtc, maxAge, ref box, static o => o.GroundItems);
        FusedFact<IReadOnlyList<GroundItem>> fused = Finish(FusionField.GroundItems, box, nowUtc, SameSequence);
        RecordDisagreements(FusionField.GroundItems, box, disagreements, ref disagreementCount, SameSequence);
        return fused;
    }

    private static FusedFact<bool> FuseHasTarget(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        in FusedFact<TargetedEntity> selectedTarget,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount)
    {
        SampleBox<bool> box = default;

        if (observations.Memory is { } memory)
            TryOffer(ref box, memory.HasTarget, SensorKind.Memory, 1f, nowUtc, maxAge);

        if (observations.Screen is { HasTarget: { } screenTarget })
            TryOffer(ref box, screenTarget, SensorKind.Screen, 1f, nowUtc, maxAge);

        FusedFact<bool> fused = Finish(FusionField.HasTarget, box, nowUtc, static (a, b) => a == b);
        RecordDisagreements(FusionField.HasTarget, box, disagreements, ref disagreementCount, static (a, b) => a == b);

        if (!fused.HasValue
            && observations.Network is not null
            && observations.Memory is null
            && observations.Screen is null)
        {
            return FusedFact<bool>.Unknown(NetworkDoesNotEstablishHasTargetReason, nowUtc);
        }

        if (fused.HasValue
            && !fused.Value.Value
            && selectedTarget.HasValue
            && selectedTarget.Value.ObservedAtUtc > fused.Value.ObservedAtUtc)
        {
            RecordDisagreement(
                disagreements,
                ref disagreementCount,
                new FusionDisagreement(
                    FusionField.HasTarget,
                    fused.Winner ?? SensorKind.Screen,
                    fused.Value.Source,
                    SensorKind.Network,
                    selectedTarget.Value.Source,
                    TargetStateComposer.SourcesDisagreeReason));
            return FusedFact<bool>.Unknown(TargetStateComposer.SourcesDisagreeReason, nowUtc);
        }

        return fused;
    }

    private static FusedFact<bool> FuseClientAttached(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        if (observations.Local is not { ClientAttached: { } attached })
            return FusedFact<bool>.Unknown(SensorNotObservedReason, nowUtc);

        SampleBox<bool> box = default;
        TryOffer(ref box, attached, SensorKind.Local, 1f, nowUtc, maxAge);
        return Finish(FusionField.ClientAttached, box, nowUtc, static (a, b) => a == b);
    }

    private static FusedFact<bool> FuseOccupancy(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        if (observations.Local is not { Occupancy: { } view })
            return FusedFact<bool>.Unknown(OccupancyNeverObservedReason, nowUtc);

        if (view.ObservedAtUtc is not { } stamped)
            return FusedFact<bool>.Unknown(OccupancyNotStampedReason, nowUtc);

        if (stamped > nowUtc)
            return FusedFact<bool>.Unknown(OccupancyFromFutureReason, nowUtc);

        TimeSpan age = nowUtc - stamped;
        if (age > maxAge)
        {
            string reason = string.Create(
                CultureInfo.InvariantCulture,
                $"{OccupancyFreshness.ViewStalePrefix}:{age.TotalMilliseconds:F0}ms_of_{maxAge.TotalMilliseconds:F0}ms");
            return FusedFact<bool>.Unknown(reason, nowUtc);
        }

        ClassifiedValue<bool> fresh = new(true, DataSourceKind.Derived, stamped, true, null, null);
        return new FusedFact<bool>(fresh, SensorKind.Local, 1f, true);
    }

    private static FusedFact<bool> FuseNavigation(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        if (observations.Local is not { NavigationPathFound: { } path })
            return FusedFact<bool>.Unknown(SensorNotObservedReason, nowUtc);

        SampleBox<bool> box = default;
        TryOffer(ref box, path, SensorKind.Local, 1f, nowUtc, maxAge);
        return Finish(FusionField.NavigationPathFound, box, nowUtc, static (a, b) => a == b);
    }

    private static void CollectGameplay<T>(
        in SensorObservationSet observations,
        DateTime nowUtc,
        TimeSpan maxAge,
        ref SampleBox<T> box,
        Func<GameplayObservation, ClassifiedValue<T>> pick)
    {
        if (observations.Network is { } network)
            TryOffer(ref box, pick(network), SensorKind.Network, 1f, nowUtc, maxAge);
        if (observations.Memory is { } memory)
            TryOffer(ref box, pick(memory), SensorKind.Memory, 1f, nowUtc, maxAge);
    }

    private static void TryOffer<T>(
        ref SampleBox<T> box,
        ClassifiedValue<T> value,
        SensorKind sensor,
        float confidence,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        if (!value.HasValue)
        {
            box.RememberUnknown(value.FailureReason);
            return;
        }

        if (nowUtc < value.ObservedAtUtc)
        {
            box.RememberStale(FutureObservationReason);
            return;
        }

        if (nowUtc - value.ObservedAtUtc > maxAge)
        {
            box.RememberStale($"{FusedStalePrefix}{sensor}");
            return;
        }

        box.Add(new SensorSample<T>(value, sensor, confidence));
    }

    private static FusedFact<T> Finish<T>(
        FusionField field,
        in SampleBox<T> box,
        DateTime nowUtc,
        Func<T, T, bool> equals)
    {
        if (box.Count == 0)
        {
            if (box.StaleReason is { } stale)
                return FusedFact<T>.Unknown(stale, nowUtc);
            if (box.UnknownReason is { } unknown)
                return FusedFact<T>.Unknown(unknown, nowUtc);
            return FusedFact<T>.Unknown(SensorNotObservedReason, nowUtc);
        }

        bool hasTrusted = false;
        for (int i = 0; i < box.Count; i++)
        {
            if (IsTrusted(box[i].Value.Source))
                hasTrusted = true;
        }

        for (int i = 0; i < box.Count; i++)
        {
            if (hasTrusted && box[i].Value.Source == DataSourceKind.Simulated)
                continue;
            for (int j = i + 1; j < box.Count; j++)
            {
                if (hasTrusted && box[j].Value.Source == DataSourceKind.Simulated)
                    continue;
                if (!equals(box[i].Value.Value, box[j].Value.Value))
                    return FusedFact<T>.Unknown(SameRankDisagreementReason, nowUtc);
            }
        }

        int winner = -1;
        for (int i = 0; i < box.Count; i++)
        {
            if (hasTrusted && box[i].Value.Source == DataSourceKind.Simulated)
                continue;
            if (winner < 0 || Compare(field, box[winner], box[i], equals) > 0)
                winner = i;
        }

        if (winner < 0)
            return FusedFact<T>.Unknown(SensorNotObservedReason, nowUtc);

        SensorSample<T> chosen = box[winner];
        return new FusedFact<T>(chosen.Value, chosen.Sensor, chosen.Confidence, true);
    }

    private static void RecordDisagreements<T>(
        FusionField field,
        in SampleBox<T> box,
        Span<FusionDisagreement> disagreements,
        ref int disagreementCount,
        Func<T, T, bool> equals)
    {
        if (box.Count < 2)
            return;

        for (int i = 0; i < box.Count; i++)
        {
            for (int j = i + 1; j < box.Count; j++)
            {
                if (equals(box[i].Value.Value, box[j].Value.Value))
                    continue;

                RecordDisagreement(
                    disagreements,
                    ref disagreementCount,
                    new FusionDisagreement(
                        field,
                        box[i].Sensor,
                        box[i].Value.Source,
                        box[j].Sensor,
                        box[j].Value.Source,
                        SameRankDisagreementReason));
            }
        }
    }

    private static bool IsTrusted(DataSourceKind source)
        => source is DataSourceKind.Live or DataSourceKind.Derived or DataSourceKind.Cached;

    private static int Compare<T>(FusionField field, in SensorSample<T> left, in SensorSample<T> right, Func<T, T, bool> equals)
    {
        int source = SourceRank(left.Value.Source).CompareTo(SourceRank(right.Value.Source));
        if (source != 0)
            return source;

        int sensor = SensorRank(field, left.Sensor).CompareTo(SensorRank(field, right.Sensor));
        if (sensor != 0)
            return sensor;

        if (equals(left.Value.Value, right.Value.Value))
            return left.Sensor.CompareTo(right.Sensor);

        return 0;
    }

    private static int SourceRank(DataSourceKind source) => source switch
    {
        DataSourceKind.Live => 0,
        DataSourceKind.Cached => 1,
        DataSourceKind.Derived => 2,
        DataSourceKind.Simulated => 3,
        _ => 99
    };

    private static int SensorRank(FusionField field, SensorKind sensor)
    {
        return field switch
        {
            FusionField.Hp or FusionField.MaxHp or FusionField.Mp or FusionField.MaxMp
                or FusionField.EntitiesInView or FusionField.Entities
                or FusionField.HitBy or FusionField.SelectedTarget
                or FusionField.SkillsReady or FusionField.Inventory
                or FusionField.LastPickup or FusionField.GroundItems
                => sensor switch
                {
                    SensorKind.Network => 0,
                    SensorKind.Memory => 1,
                    SensorKind.Screen => 2,
                    _ => 9
                },
            FusionField.PlayerPosition or FusionField.MapId or FusionField.StandingCell
                => sensor switch
                {
                    SensorKind.Memory => 0,
                    SensorKind.Screen => 1,
                    SensorKind.Network => 2,
                    _ => 9
                },
            FusionField.HasTarget or FusionField.InCombat
                => sensor switch
                {
                    SensorKind.Memory => 0,
                    SensorKind.Screen => 1,
                    SensorKind.Network => 2,
                    _ => 9
                },
            _ => sensor switch
            {
                SensorKind.Local => 0,
                _ => 9
            }
        };
    }

    private static bool SameEntitySet(IReadOnlyList<SelectableEntity> left, IReadOnlyList<SelectableEntity> right)
    {
        if (left.Count != right.Count)
            return false;

        for (int i = 0; i < left.Count; i++)
        {
            bool found = false;
            for (int j = 0; j < right.Count; j++)
            {
                if (left[i].EntityId != right[j].EntityId)
                    continue;
                if (left[i].At != right[j].At || left[i].Vnum != right[j].Vnum || left[i].HpRatio != right[j].HpRatio)
                    return false;
                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private static bool SameSequence<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count)
            return false;
        for (int i = 0; i < left.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static void RecordDisagreement(
        Span<FusionDisagreement> disagreements,
        ref int count,
        in FusionDisagreement item)
    {
        if ((uint)count >= (uint)disagreements.Length)
            return;

        for (int i = 0; i < count; i++)
        {
            if (disagreements[i].Field == item.Field
                && disagreements[i].LeftSensor == item.LeftSensor
                && disagreements[i].RightSensor == item.RightSensor
                && disagreements[i].Reason == item.Reason)
            {
                return;
            }
        }

        disagreements[count] = item;
        count++;
    }

    private static IReadOnlyList<FusionDisagreement> CopyDisagreements(ReadOnlySpan<FusionDisagreement> source, int count)
    {
        if (count <= 0)
            return Array.Empty<FusionDisagreement>();

        var copy = new FusionDisagreement[count];
        source.Slice(0, count).CopyTo(copy);
        return copy;
    }

    private struct SampleBox<T>
    {
        private SensorSample<T> _a;
        private SensorSample<T> _b;
        private SensorSample<T> _c;
        private SensorSample<T> _d;

        public int Count { get; private set; }
        public string? StaleReason { get; private set; }
        public string? UnknownReason { get; private set; }

        public SensorSample<T> this[int index] => index switch
        {
            0 => _a,
            1 => _b,
            2 => _c,
            3 => _d,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        public void Add(in SensorSample<T> sample)
        {
            switch (Count)
            {
                case 0: _a = sample; break;
                case 1: _b = sample; break;
                case 2: _c = sample; break;
                case 3: _d = sample; break;
                default: return;
            }

            Count++;
        }

        public void RememberStale(string reason) => StaleReason ??= reason;

        public void RememberUnknown(string? reason)
        {
            if (UnknownReason is null && reason is { Length: > 0 })
                UnknownReason = reason;
        }
    }
}
