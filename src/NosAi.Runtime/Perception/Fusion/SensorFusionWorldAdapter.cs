using System.Globalization;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Perception.Fusion;

/// <summary>
/// Projects a fused observation into the types A4 already consumes.
/// </summary>
/// <remarks>
/// <para>
/// A1 owns the versioned AP-01 World Model contracts. This adapter does not
/// invent them. It maps onto the existing classified
/// <see cref="GameplayObservation"/> (the seam Gate 3 already reads) and, when
/// every required fact is known without fabrication, onto the current
/// <see cref="WorldState"/> snapshot.
/// </para>
/// <para>
/// <see cref="TryToWorldState"/> refuses when a required field is UNKNOWN or
/// SIMULATED. The current <see cref="WorldState"/> cannot carry provenance, so
/// emitting one from an unknown HP or an unknown entity list would turn
/// UNKNOWN into <c>false</c>, <c>0</c> or an empty world.
/// </para>
/// </remarks>
public readonly record struct WorldModelProjection(
    bool Succeeded,
    WorldState? State,
    string? FailureReason)
{
    public static WorldModelProjection Failed(string reason) => new(false, null, reason);
}

public static class SensorFusionWorldAdapter
{
    public const string PlayerVitalsUnknownReason = "fused_player_vitals_unknown";
    public const string EntitiesUnknownReason = "fused_entities_unknown";
    public const string SimulatedNotProjectedReason = "fused_simulated_not_projected";
    public const string MaxHpNotPositiveReason = "fused_max_hp_not_positive";

    /// <summary>
    /// The classified observation A4 can copy into
    /// <c>Gate3WorldState.FromObservation</c>. Every field keeps the fused
    /// provenance; UNKNOWN stays UNKNOWN.
    /// </summary>
    public static GameplayObservation ToGameplayObservation(FusedWorldObservation fused)
    {
        ArgumentNullException.ThrowIfNull(fused);

        return new GameplayObservation(
            fused.Hp.Value,
            fused.MaxHp.Value,
            fused.Mp.Value,
            fused.MaxMp.Value,
            fused.HasTarget.Value,
            fused.InCombat.Value,
            fused.EntitiesInView.Value,
            fused.FusedAtUtc)
        {
            Entities = fused.Entities.Value,
            PlayerPosition = fused.PlayerPosition.Value,
            MapId = fused.MapId.Value,
            StandingCell = fused.StandingCell.Value,
            HitBy = fused.HitBy.Value,
            SelectedTarget = fused.SelectedTarget.Value,
            SkillsReady = fused.SkillsReady.Value,
            Inventory = fused.Inventory.Value,
            LastPickup = fused.LastPickup.Value,
            GroundItems = fused.GroundItems.Value,
        };
    }

    /// <summary>
    /// Projects into <see cref="WorldState"/> only when player vitals and the
    /// entity list are known and none of them is simulated.
    /// </summary>
    public static WorldModelProjection TryToWorldState(FusedWorldObservation fused)
    {
        ArgumentNullException.ThrowIfNull(fused);

        if (IsSimulated(fused.Hp) || IsSimulated(fused.MaxHp) || IsSimulated(fused.Entities))
            return WorldModelProjection.Failed(SimulatedNotProjectedReason);

        if (!fused.Hp.HasValue || !fused.MaxHp.HasValue)
            return WorldModelProjection.Failed(fused.Hp.Value.FailureReason
                ?? fused.MaxHp.Value.FailureReason
                ?? PlayerVitalsUnknownReason);

        if (fused.MaxHp.Value.Value <= 0)
            return WorldModelProjection.Failed(MaxHpNotPositiveReason);

        if (!fused.Entities.HasValue)
            return WorldModelProjection.Failed(fused.Entities.Value.FailureReason ?? EntitiesUnknownReason);

        int hp = fused.Hp.Value.Value;
        int maxHp = fused.MaxHp.Value.Value;
        double ratio = hp / (double)maxHp;
        bool alive = hp > 0;
        long tick = new DateTimeOffset(fused.FusedAtUtc).ToUnixTimeMilliseconds();

        IReadOnlyList<SelectableEntity> seen = fused.Entities.Value.Value;
        var entities = new EntityState[seen.Count];
        for (int i = 0; i < seen.Count; i++)
        {
            SelectableEntity entity = seen[i];
            string kind = entity.Vnum is { } vnum
                ? vnum.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            entities[i] = new EntityState(
                entity.EntityId.ToString(CultureInfo.InvariantCulture),
                kind,
                entity.At.X,
                entity.At.Y,
                entity.HpRatio);
        }

        return new WorldModelProjection(
            true,
            new WorldState(tick, alive, ratio, entities),
            null);
    }

    private static bool IsSimulated<T>(in FusedFact<T> fact)
        => fact.HasValue && fact.Value.Source == DataSourceKind.Simulated;
}
