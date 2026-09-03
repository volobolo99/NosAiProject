using System.Globalization;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.ControlPanel;

/// <summary>
/// Entities, last hit, and target as classified values, parsed from the wire
/// form of <c>gameplayBaseline</c> without inventing members the payload omitted.
/// </summary>
internal readonly record struct GameplayPanelRead(
    ClassifiedValue<IReadOnlyList<SelectableEntity>> Entities,
    ClassifiedValue<Aggressor> HitBy,
    ClassifiedValue<bool> HasTarget,
    ClassifiedValue<int> MapId,
    ClassifiedValue<MapPoint> StandingCell)
{
    /// <summary>Nothing on the wire. Not an empty surroundings list.</summary>
    public static GameplayPanelRead Unknown(string reason) => new(
        ClassifiedValue<IReadOnlyList<SelectableEntity>>.Unknown(reason),
        ClassifiedValue<Aggressor>.Unknown(reason),
        ClassifiedValue<bool>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<MapPoint>.Unknown(reason));
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
            ReadHasTarget(value),
            ReadMapId(value),
            ReadStandingCell(value));
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

    private static ClassifiedValue<bool> ReadHasTarget(JsonElement payload)
    {
        if (!payload.TryGetProperty("hasTarget", out JsonElement node) || node.ValueKind != JsonValueKind.Object)
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

        DateTime at = item.TryGetProperty("observedAtUtc", out JsonElement timeNode) && TryTime(timeNode, out DateTime stated)
            ? stated
            : fallbackUtc;

        double? hp = null;
        if (item.TryGetProperty("hpRatio", out JsonElement hpNode) && hpNode.ValueKind == JsonValueKind.Number
            && hpNode.TryGetDouble(out double ratio))
        {
            hp = ratio;
        }

        entity = new SelectableEntity(id, new MapPoint(x, y), hp, at);
        return true;
    }

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

    private static ClassifiedValue<T> Classify<T>(T value, string? source, DateTime at) => source switch
    {
        "LIVE" => ClassifiedValue<T>.Live(value, at),
        "DERIVED" => ClassifiedValue<T>.Derived(value, at),
        "CACHED" => ClassifiedValue<T>.Cached(value, at),
        "SIMULATED" => ClassifiedValue<T>.Simulated(value, at),
        _ => ClassifiedValue<T>.Unknown("unclassified_source")
    };
}
