using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Perception.Fusion;

/// <summary>
/// The four observation families AP-01 may fuse. Each sample keeps this label
/// so a fused fact can say which sensor won, and a disagreement can name both
/// sides. Local telemetry never becomes gameplay truth.
/// </summary>
public enum SensorKind : byte
{
    Network = 0,
    Memory = 1,
    Screen = 2,
    Local = 3
}

/// <summary>World-model field identity used when recording a disagreement.</summary>
public enum FusionField : byte
{
    Hp = 0,
    MaxHp = 1,
    Mp = 2,
    MaxMp = 3,
    HasTarget = 4,
    InCombat = 5,
    EntitiesInView = 6,
    Entities = 7,
    PlayerPosition = 8,
    MapId = 9,
    StandingCell = 10,
    HitBy = 11,
    SelectedTarget = 12,
    SkillsReady = 13,
    Inventory = 14,
    LastPickup = 15,
    GroundItems = 16,
    ClientAttached = 17,
    OccupancyViewFresh = 18,
    NavigationPathFound = 19
}

/// <summary>
/// Two usable samples named the same field and did not agree. The fused value
/// is either the higher-precedence sample or UNKNOWN when the two sat at the
/// same rank — this record does not choose; it only names the collision.
/// </summary>
public readonly record struct FusionDisagreement(
    FusionField Field,
    SensorKind LeftSensor,
    DataSourceKind LeftSource,
    SensorKind RightSensor,
    DataSourceKind RightSource,
    string Reason);

/// <summary>One sensor's classified reading of one field, after normalization.</summary>
public readonly record struct SensorSample<T>(
    ClassifiedValue<T> Value,
    SensorKind Sensor,
    float Confidence);

/// <summary>
/// A fused fact: the classified value plus who won, how sure that sensor was,
/// and whether it was still inside the freshness budget at fusion time.
/// </summary>
/// <remarks>
/// <see cref="Value"/> is what A4 may copy into the World Model. UNKNOWN stays
/// UNKNOWN — <see cref="ClassifiedValue{T}.HasValue"/> is false and
/// <see cref="Winner"/> is null. Confidence is 0 when nothing usable was seen.
/// </remarks>
public readonly record struct FusedFact<T>(
    ClassifiedValue<T> Value,
    SensorKind? Winner,
    float Confidence,
    bool IsFresh)
{
    public bool HasValue => Value.HasValue;

    public static FusedFact<T> Unknown(string reason, DateTime atUtc, string? warning = null) =>
        new(new ClassifiedValue<T>(default!, DataSourceKind.Unknown, atUtc, false, warning, reason),
            null, 0f, false);
}

/// <summary>
/// Screen-channel input. Screen facts are DERIVED or UNKNOWN and never LIVE
/// (ADR-0012). A missing nested reading means that sensor did not look, which
/// is not the same as a failed look.
/// </summary>
public readonly record struct ScreenWorldReading(
    ScreenVitalPair? Hp,
    ScreenVitalPair? Mp,
    ClassifiedValue<bool>? HasTarget,
    ClassifiedValue<MapPoint>? ProjectedPlayerPosition,
    DateTime? LastPlayerAttackAtUtc);

/// <summary>
/// Local-channel input: client liveness and navigation evidence. None of these
/// fields may fill HP, position, or targeting.
/// </summary>
public readonly record struct LocalWorldReading(
    ClassifiedValue<bool>? ClientAttached,
    OccupancyView? Occupancy,
    ClassifiedValue<bool>? NavigationPathFound,
    DateTime ObservedAtUtc);

/// <summary>
/// The four raw channels. A null channel is "this sensor was not bound", not
/// UNKNOWN. Callers must pass uncomposed sources: a <see cref="GameplayObservation"/>
/// that already mixed memory and screen must not be labelled Network.
/// </summary>
public readonly record struct SensorObservationSet(
    GameplayObservation? Network,
    GameplayObservation? Memory,
    ScreenWorldReading? Screen,
    LocalWorldReading? Local)
{
    public static SensorObservationSet Empty => default;
}

/// <summary>Freshness budgets applied at fusion time. Callers pass <c>nowUtc</c>.</summary>
public readonly record struct FusionPolicy(TimeSpan MaxAge, TimeSpan OccupancyMaxAge)
{
    /// <summary>The published vitals bound — not a second number.</summary>
    public static readonly TimeSpan DefaultMaxAge = NetworkGameplayProvider.DefaultMaxVitalsAge;

    /// <summary>The published occupancy-view bound — not a second number.</summary>
    public static readonly TimeSpan DefaultOccupancyMaxAge = OccupancyFreshness.DefaultMaxViewAge;

    public static FusionPolicy Default { get; } = new(DefaultMaxAge, DefaultOccupancyMaxAge);

    public const int MaxTrackedDisagreements = 16;
}

/// <summary>
/// AP-01 fused observation: Network + Memory + Screen + Local, field by field.
/// </summary>
/// <remarks>
/// <para>
/// This is the Sensor Fusion stage output. A1 owns the versioned World Model
/// contracts; A4 binds this type into them. Fields the current sensors cannot
/// observe (quest graph, NPC catalogue, equipment compare, …) are absent on
/// purpose — inventing UNKNOWN placeholders typed against contracts that do
/// not exist yet would be a cross-agent API.
/// </para>
/// <para>
/// Every gameplay fact is a <see cref="FusedFact{T}"/>. A consumer that needs
/// a number must inspect <see cref="FusedFact{T}.HasValue"/>. Missing sensors,
/// stale samples and same-rank contradictions all stay UNKNOWN.
/// </para>
/// </remarks>
public sealed record FusedWorldObservation(
    DateTime FusedAtUtc,
    FusedFact<int> Hp,
    FusedFact<int> MaxHp,
    FusedFact<int> Mp,
    FusedFact<int> MaxMp,
    FusedFact<bool> HasTarget,
    FusedFact<bool> InCombat,
    FusedFact<int> EntitiesInView,
    FusedFact<IReadOnlyList<SelectableEntity>> Entities,
    FusedFact<MapPoint> PlayerPosition,
    FusedFact<int> MapId,
    FusedFact<MapPoint> StandingCell,
    FusedFact<Aggressor> HitBy,
    FusedFact<TargetedEntity> SelectedTarget,
    FusedFact<IReadOnlyList<SkillReady>> SkillsReady,
    FusedFact<IReadOnlyList<InventorySlotReading>> Inventory,
    FusedFact<ItemPickup> LastPickup,
    FusedFact<IReadOnlyList<GroundItem>> GroundItems,
    FusedFact<bool> ClientAttached,
    FusedFact<bool> OccupancyViewFresh,
    FusedFact<bool> NavigationPathFound,
    IReadOnlyList<FusionDisagreement> Disagreements)
{
    public bool HasDisagreement => Disagreements.Count > 0;
}
