using System.Globalization;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.ControlPanel;

/// <summary>
/// Every field the baseline publishes, parsed from the wire form of
/// <c>gameplayBaseline</c> without inventing members the payload omitted.
/// The wire carries fourteen fields; this read keeps all of them, not the
/// five the panel used to draw.
/// </summary>
internal readonly record struct GameplayPanelRead(
    ClassifiedValue<IReadOnlyList<SelectableEntity>> Entities,
    ClassifiedValue<Aggressor> HitBy,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<int> MapId,
    ClassifiedValue<MapPoint> StandingCell,
    ClassifiedValue<IReadOnlyList<InventorySlotReading>> Inventory,
    ClassifiedValue<TargetedEntity> SelectedTarget,
    ClassifiedValue<IReadOnlyList<GroundItem>> GroundItems,
    ClassifiedValue<ItemPickup> LastPickup,
    ClassifiedValue<IReadOnlyList<SkillReady>> SkillsReady,
    ClassifiedValue<bool> InCombat)
{
    /// <summary>Nothing on the wire. Not an empty surroundings list.</summary>
    public static GameplayPanelRead Unknown(string reason) => new(
        ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason),
        ClassifiedValue<Aggressor>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<MapPoint>.Unknown(reason),
        ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason),
        ClassifiedValue<TargetedEntity>.Unknown(reason),
        ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(reason),
        ClassifiedValue<ItemPickup>.Unknown(reason),
        ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason));
}

/// <summary>
/// Reads the C1 fields inside an attached <c>gameplayBaseline</c>. Missing keys
/// stay UNKNOWN with the reason the producer already uses; an empty array is
/// forwarded as an empty list, which is not UNKNOWN.
/// </summary>
internal static class GameplayWireReader
{
    /// <summary>
    /// Parses <paramref name="client"/>.<c>gameplayBaseline</c>. A missing or
    /// unread baseline is UNKNOWN with the factory's no-provider reason, not
    /// an empty sighting list.
    /// </summary>
    public static GameplayPanelRead Read(JsonElement? client)
    {
        if (client is not { } root
            || !root.TryGetProperty("gameplayBaseline", out JsonElement baseline)
            || baseline.ValueKind != JsonValueKind.Object)
        {
            return GameplayPanelRead.Unknown("gameplay_provider_not_available");
        }

        if (!TryValueObject(baseline, out JsonElement value, out string unreadReason))
            return GameplayPanelRead.Unknown(unreadReason);

        return new GameplayPanelRead(
            ReadEntities(value),
            ReadHitBy(value),
            ReadBool(value, "hasTarget"),
            ReadMapId(value),
            ReadStandingCell(value),
            ReadInventory(value),
            ReadSelectedTarget(value),
            ReadGroundItems(value),
            ReadLastPickup(value),
            ReadSkillsReady(value),
            ReadBool(value, "inCombat"));
    }

    private static bool TryValueObject(JsonElement classified, out JsonElement value, out string reason)
    {
        value = default;
        reason = FailureReason(classified, "gameplay_provider_not_available");
        string? source = SourceText(classified);
        if (string.IsNullOrWhiteSpace(source) || source == "UNKNOWN")
            return false;
        if (!classified.TryGetProperty("value", out value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        return true;
    }

    private static ClassifiedValue<IReadOnlyList<SelectableEntity>> ReadEntities(JsonElement payload)
    {
        if (!payload.TryGetProperty("entities", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            return ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason);

        var rows = new List<SelectableEntity>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (TryEntity(item, at, out SelectableEntity entity))
                rows.Add(entity);
        }

        IReadOnlyList<SelectableEntity> frozen = rows.Count == 0
            ? Array.Empty<SelectableEntity>()
            : rows;
        return Classify(frozen, source, at);
    }

    private static ClassifiedValue<Aggressor> ReadHitBy(JsonElement payload)
    {
        if (!payload.TryGetProperty("hitBy", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<Aggressor>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<Aggressor>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<Aggressor>.Unknown(reason);
        if (!TryInt64(value, "entityId", out long id) || !TryInt32(value, "entityType", out int type))
            return ClassifiedValue<Aggressor>.Unknown(reason);

        return Classify(new Aggressor(id, type), source, at);
    }

    private static ClassifiedValue<TargetedEntity> ReadSelectedTarget(JsonElement payload)
    {
        if (!payload.TryGetProperty("selectedTarget", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<TargetedEntity>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<TargetedEntity>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<TargetedEntity>.Unknown(reason);
        if (!TryInt64(value, "entityId", out long id) || !TryInt32(value, "entityType", out int type))
            return ClassifiedValue<TargetedEntity>.Unknown(reason);

        return Classify(new TargetedEntity(id, type), source, at);
    }

    private static ClassifiedValue<IReadOnlyList<InventorySlotReading>> ReadInventory(JsonElement payload)
    {
        if (!payload.TryGetProperty("inventory", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            return ClassifiedValue<IReadOnlyList<InventorySlotReading>>.Unknown(reason);

        DataSourceKind kind = ParseKind(source);
        var rows = new List<InventorySlotReading>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (TryInventory(item, at, kind, out InventorySlotReading slot))
                rows.Add(slot);
        }

        IReadOnlyList<InventorySlotReading> frozen = rows.Count == 0
            ? Array.Empty<InventorySlotReading>()
            : rows;
        return Classify(frozen, source, at);
    }

    private static ClassifiedValue<IReadOnlyList<GroundItem>> ReadGroundItems(JsonElement payload)
    {
        if (!payload.TryGetProperty("groundItems", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            return ClassifiedValue<IReadOnlyList<GroundItem>>.Unknown(reason);

        DataSourceKind kind = ParseKind(source);
        var rows = new List<GroundItem>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (TryGroundItem(item, at, kind, out GroundItem ground))
                rows.Add(ground);
        }

        IReadOnlyList<GroundItem> frozen = rows.Count == 0
            ? Array.Empty<GroundItem>()
            : rows;
        return Classify(frozen, source, at);
    }

    private static ClassifiedValue<IReadOnlyList<SkillReady>> ReadSkillsReady(JsonElement payload)
    {
        if (!payload.TryGetProperty("skillsReady", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            return ClassifiedValue<IReadOnlyList<SkillReady>>.Unknown(reason);

        DataSourceKind kind = ParseKind(source);
        var rows = new List<SkillReady>(list.GetArrayLength());
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (TrySkillReady(item, at, kind, out SkillReady skill))
                rows.Add(skill);
        }

        IReadOnlyList<SkillReady> frozen = rows.Count == 0
            ? Array.Empty<SkillReady>()
            : rows;
        return Classify(frozen, source, at);
    }

    private static ClassifiedValue<ItemPickup> ReadLastPickup(JsonElement payload)
    {
        if (!payload.TryGetProperty("lastPickup", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<ItemPickup>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<ItemPickup>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<ItemPickup>.Unknown(reason);
        if (!TryInt32(value, "takerType", out int takerType)
            || !TryInt64(value, "takerId", out long takerId)
            || !TryInt64(value, "dropId", out long dropId))
            return ClassifiedValue<ItemPickup>.Unknown(reason);

        // Null is not false: before the own id is known the wire leaves byPlayer
        // unset, and that absence stays a null rather than "not by the player".
        bool? byPlayer = value.TryGetProperty("byPlayer", out JsonElement byNode)
            && byNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? byNode.GetBoolean()
                : null;

        return Classify(new ItemPickup(takerType, takerId, dropId, byPlayer, at, ParseKind(source)), source, at);
    }

    private static ClassifiedValue<bool> ReadBool(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<bool>.Unknown(GameplayObservation.NotPublishedReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<bool>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ClassifiedValue<bool>.Unknown(reason);

        bool? flag = value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
            _ => null
        };
        if (flag is not { } named)
            return ClassifiedValue<bool>.Unknown(reason);

        return Classify(named, source, at);
    }

    private static ClassifiedValue<int> ReadMapId(JsonElement payload)
    {
        if (!payload.TryGetProperty("mapId", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<int>.Unknown(GameplayObservation.MapIdNotReadReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<int>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ClassifiedValue<int>.Unknown(reason);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int id))
            return Classify(id, source, at);
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return Classify(id, source, at);

        return ClassifiedValue<int>.Unknown(reason);
    }

    private static ClassifiedValue<MapPoint> ReadStandingCell(JsonElement payload)
    {
        if (!payload.TryGetProperty("standingCell", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<MapPoint>.Unknown(GameplayObservation.StandingCellNotReadReason);

        if (!TryOpen(node, out string? source, out DateTime at, out string reason))
            return ClassifiedValue<MapPoint>.Unknown(reason);

        if (!node.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            return ClassifiedValue<MapPoint>.Unknown(reason);
        if (!TryInt32(value, "x", out int x) || !TryInt32(value, "y", out int y))
            return ClassifiedValue<MapPoint>.Unknown(reason);

        return Classify(new MapPoint(x, y), source, at);
    }

    private static bool TryOpen(JsonElement node, out string? source, out DateTime at, out string reason)
    {
        source = SourceText(node);
        at = ReadTime(node);
        reason = FailureReason(node, GameplayObservation.NotPublishedReason);
        if (string.IsNullOrWhiteSpace(source) || source == "UNKNOWN")
            return false;
        if (node.TryGetProperty("hasObservedValue", out JsonElement observed)
            && observed.ValueKind == JsonValueKind.False)
            return false;
        return true;
    }

    private static bool TryEntity(JsonElement item, DateTime fallbackUtc, out SelectableEntity entity)
    {
        entity = default;
        if (item.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryInt64(item, "entityId", out long id))
            return false;
        if (!TryInt32(item, "x", out int x) || !TryInt32(item, "y", out int y))
            return false;

        DateTime at = ReadMemberTime(item, fallbackUtc);

        double? hp = null;
        if (item.TryGetProperty("hpRatio", out JsonElement hpNode) && hpNode.ValueKind == JsonValueKind.Number
            && hpNode.TryGetDouble(out double ratio))
        {
            hp = ratio;
        }

        // Il vnum lo snapshot lo pubblica da sempre, e questo lettore lo buttava
        // via: il pannello scriveva "vnum_not_on_observation" per entita' il cui
        // numero era li' nel JSON. La specie e' arrivata dopo, il 2026-09-08.
        int? vnum = item.TryGetProperty("vnum", out JsonElement vnumNode)
            && vnumNode.ValueKind == JsonValueKind.Number
            && vnumNode.TryGetInt32(out int parsedVnum)
                ? parsedVnum
                : null;

        string? kind = item.TryGetProperty("kind", out JsonElement kindNode)
            && kindNode.ValueKind == JsonValueKind.String
                ? kindNode.GetString()
                : null;

        entity = new SelectableEntity(id, new MapPoint(x, y), hp, at, vnum, Vitals: null, Kind: kind);
        return true;
    }

    private static bool TryInventory(JsonElement item, DateTime fallbackUtc, DataSourceKind source, out InventorySlotReading slot)
    {
        slot = null!;
        if (item.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryInt32(item, "inventoryKind", out int kind)
            || !TryInt32(item, "slot", out int slotIndex)
            || !TryInt32(item, "vnum", out int vnum)
            || !TryInt32(item, "amount", out int amount)
            || !TryInt32(item, "rarity", out int rarity))
            return false;

        slot = new InventorySlotReading(kind, slotIndex, vnum, amount, rarity, ReadMemberTime(item, fallbackUtc), source);
        return true;
    }

    private static bool TryGroundItem(JsonElement item, DateTime fallbackUtc, DataSourceKind source, out GroundItem ground)
    {
        ground = null!;
        if (item.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryInt64(item, "dropId", out long dropId)
            || !TryInt32(item, "vnum", out int vnum)
            || !TryInt32(item, "x", out int x)
            || !TryInt32(item, "y", out int y)
            || !TryInt32(item, "amount", out int amount)
            || !TryInt64(item, "ownerId", out long ownerId))
            return false;

        ground = new GroundItem(vnum, dropId, x, y, amount, ownerId, ReadMemberTime(item, fallbackUtc), source);
        return true;
    }

    private static bool TrySkillReady(JsonElement item, DateTime fallbackUtc, DataSourceKind source, out SkillReady skill)
    {
        skill = null!;
        if (item.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryInt32(item, "slot", out int slot))
            return false;

        skill = new SkillReady(slot, ReadMemberTime(item, fallbackUtc), source);
        return true;
    }

    private static DateTime ReadMemberTime(JsonElement item, DateTime fallbackUtc)
        => item.TryGetProperty("observedAtUtc", out JsonElement timeNode) && TryTime(timeNode, out DateTime stated)
            ? stated
            : fallbackUtc;

    private static bool TryInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement node) || node.ValueKind != JsonValueKind.Number)
            return false;
        return node.TryGetInt64(out value);
    }

    private static bool TryInt32(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out JsonElement node) || node.ValueKind != JsonValueKind.Number)
            return false;
        return node.TryGetInt32(out value);
    }

    private static string FailureReason(JsonElement node, string fallback)
    {
        if (node.TryGetProperty("failureReason", out JsonElement reason)
            && reason.GetString() is { Length: > 0 } named)
            return named;
        return fallback;
    }

    private static string? SourceText(JsonElement node)
        => node.TryGetProperty("source", out JsonElement source) ? source.GetString() : null;

    private static DateTime ReadTime(JsonElement node)
        => node.TryGetProperty("observedAtUtc", out JsonElement time) && TryTime(time, out DateTime at)
            ? at
            : DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    private static bool TryTime(JsonElement node, out DateTime at)
    {
        at = default;
        if (node.ValueKind != JsonValueKind.String)
            return false;
        if (!DateTime.TryParse(
                node.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed))
            return false;
        at = parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
        return true;
    }

    private static DataSourceKind ParseKind(string? source) => source switch
    {
        "LIVE" => DataSourceKind.Live,
        "DERIVED" => DataSourceKind.Derived,
        "CACHED" => DataSourceKind.Cached,
        "SIMULATED" => DataSourceKind.Simulated,
        _ => DataSourceKind.Unknown
    };

    private static ClassifiedValue<T> Classify<T>(T value, string? source, DateTime at) => ParseKind(source) switch
    {
        DataSourceKind.Live => ClassifiedValue<T>.Live(value, at),
        DataSourceKind.Derived => ClassifiedValue<T>.Derived(value, at),
        DataSourceKind.Cached => ClassifiedValue<T>.Cached(value, at),
        DataSourceKind.Simulated => ClassifiedValue<T>.Simulated(value, at),
        _ => ClassifiedValue<T>.Unknown("unclassified_source")
    };
}
