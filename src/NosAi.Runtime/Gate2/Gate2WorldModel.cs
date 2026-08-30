// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Riduzione deterministica delle osservazioni nel World Model canonico
// ============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;

namespace NosAi.Runtime.Gate2;

/// <summary>
/// One frame of validated observations to fold into the canonical world state.
/// </summary>
/// <remarks>
/// A <c>null</c> field means "not observed this frame", never "empty" or "zero":
/// the fold keeps the previous value instead of inventing one (UNKNOWN is not zero).
/// <see cref="ExplicitlyRemovedEntityIds"/> is for entities observed as gone
/// (death, despawn); silent disappearance is handled by staleness expiry instead.
/// </remarks>
public sealed record ObservationBatch(
    DateTime ObservedUtc,
    ControlledPlayerState? Player,
    ImmutableArray<WorldEntity> ObservedEntities,
    ImmutableArray<long> ExplicitlyRemovedEntityIds,
    int? ObservedMapId)
{
    public static ObservationBatch Empty(DateTime observedUtc) => new(
        observedUtc, null, ImmutableArray<WorldEntity>.Empty, ImmutableArray<long>.Empty, null);
}

/// <summary>Deterministic policy knobs for the world model reduction.</summary>
public static class Gate2WorldModelPolicy
{
    /// <summary>An entity not re-observed within this window is expired from the model.</summary>
    public static readonly TimeSpan StaleEntityTtl = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Pure, deterministic folding of observation batches into immutable snapshots.
/// No clock access: time always comes in through the batch, so every reduction
/// is replayable in tests exactly as it happened at runtime.
/// </summary>
public static class WorldModelReducer
{
    /// <summary>
    /// Folds one observation batch into the next snapshot (frame + 1).
    /// </summary>
    /// <remarks>
    /// Order of application: map change (clears entities of the previous map),
    /// entity upserts, explicit removals, staleness expiry, player update,
    /// confidence/degraded recomputation. Observation time must not move backwards.
    /// </remarks>
    public static WorldStateSnapshot Fold(WorldStateSnapshot previous, ObservationBatch batch, TimeSpan? staleEntityTtl = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.ObservedUtc < previous.TimestampUtc)
            throw new ArgumentException("Observation time must not precede the current snapshot.", nameof(batch));

        TimeSpan ttl = staleEntityTtl ?? Gate2WorldModelPolicy.StaleEntityTtl;
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleEntityTtl));

        var observed = batch.ObservedEntities.IsDefault ? ImmutableArray<WorldEntity>.Empty : batch.ObservedEntities;
        var removedIds = batch.ExplicitlyRemovedEntityIds.IsDefault ? ImmutableArray<long>.Empty : batch.ExplicitlyRemovedEntityIds;

        int previousMapId = previous.Player.MapId;
        int effectiveMapId = batch.ObservedMapId ?? batch.Player?.MapId ?? previousMapId;
        bool mapChanged = effectiveMapId != previousMapId;

        // A map change invalidates every entity of the previous map: the old
        // population must not survive as if it had been observed on the new map.
        var entities = mapChanged ? ImmutableDictionary<long, WorldEntity>.Empty : previous.Entities;

        if (!observed.IsEmpty)
        {
            var builder = entities.ToBuilder();
            foreach (var entity in observed) builder[entity.EntityId] = entity;
            entities = builder.ToImmutable();
        }
        if (!removedIds.IsEmpty) entities = entities.RemoveRange(removedIds);

        // Staleness expiry: entities that stopped being observed decay out of the
        // model instead of surviving forever as ghosts.
        var staleIds = entities.Values
            .Where(e => batch.ObservedUtc - e.LastObservedUtc > ttl)
            .Select(e => e.EntityId)
            .ToImmutableArray();
        if (!staleIds.IsEmpty) entities = entities.RemoveRange(staleIds);

        ControlledPlayerState player = batch.Player ?? previous.Player;
        if (batch.Player is null && mapChanged) player = player with { MapId = effectiveMapId };

        // The initial snapshot marks the player as unobserved (CharacterId == 0).
        // Any real observation, now or in a previous frame, clears that state.
        bool playerKnown = batch.Player is not null || previous.Player.CharacterId != 0;

        float confidence = entities.Count > 0
            ? entities.Values.Average(e => e.ConfidenceScore)
            : (playerKnown ? 1.0f : 0.0f);

        return previous with
        {
            FrameIndex = previous.FrameIndex + 1,
            TimestampUtc = batch.ObservedUtc,
            Player = player,
            Entities = entities,
            GlobalConfidence = confidence,
            IsDegradedState = !playerKnown,
        };
    }
}
