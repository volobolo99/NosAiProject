using System.Text.Json;
using NosAi.Runtime.Contracts;

namespace NosAi.ControlPanel;

internal static class AttachedSnapshot
{
    public static SnapshotView Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("contractVersion", out var version)
            && version.GetString() != "gate1.snapshot.v1")
        {
            return SnapshotView.Empty($"unsupported_contract_version:{version.GetString() ?? "missing"}");
        }

        var guard = root.TryGetProperty("guard", out var guardNode) && guardNode.ValueKind == JsonValueKind.Object
            ? guardNode
            : default(JsonElement?);
        var (slot, hint) = ChannelView.Slot(
            ReadClassifiedBool(guard, "connected"),
            ReadClassifiedBool(guard, "authenticated"),
            ReadClassifiedText(guard, "terminationReason"));
        JsonElement? client = root.TryGetProperty("client", out var clientNode) && clientNode.ValueKind == JsonValueKind.Object
            ? clientNode
            : default(JsonElement?);
        JsonElement? gameObservation = root.TryGetProperty("gameObservation", out var goNode) && goNode.ValueKind == JsonValueKind.Object
            ? goNode
            : default(JsonElement?);
        JsonElement? safetyNode = root.TryGetProperty("safety", out var safetyEl) && safetyEl.ValueKind == JsonValueKind.Object
            ? safetyEl
            : default(JsonElement?);
        GameplayPanelRead gameplay = GameplayWireReader.Read(client);

        return new SnapshotView
        {
            RuntimeStatus = root.TryGetProperty("runtimeStatus", out var status) ? status.GetString() ?? "UNKNOWN" : "UNKNOWN",
            Warning = root.TryGetProperty("warning", out var warning) ? warning.GetString() ?? "" : "",
            CapturedAt = root.TryGetProperty("capturedAtUtc", out var at) ? at.ToString() : "",
            ContractVersion = root.TryGetProperty("contractVersion", out var cv) ? cv.GetString() ?? "" : "gate1.snapshot.v1",
            SlotLabel = slot,
            SlotHint = hint,
            Client = ReadObject(root, "client",
                ("status", "Stato"),
                ("processName", "Processo"),
                ("processId", "PID"),
                ("windowTitle", "Finestra"),
                ("windowHandle", "Handle"),
                ("processResponding", "Risponde"),
                ("windowVisible", "Visibile"),
                ("gameplayBaseline", "Gameplay")),
            Guard = PrependChannel(
                ReadObject(root, "guard",
                    ("connected", "Collegato"),
                    ("authenticated", "Autenticato"),
                    ("sessionId", "Sessione"),
                    ("lastHeartbeatUtc", "Heartbeat"),
                    ("terminationReason", "Chiusura")),
                slot),
            Hardware = ReadObject(root, "hardware",
                ("platform", "Piattaforma"),
                ("cpu", "CPU"),
                ("logicalCores", "Core"),
                ("processWorkingSetMb", "RAM processo (MB)"),
                ("systemRamMb", "RAM sistema (MB)"),
                ("gpu", "GPU"),
                ("gpuMemoryMb", "VRAM (MB)"),
                ("displayRefreshHz", "Refresh (Hz)"),
                ("osVersion", "OS")),
            Safety = ReadObject(root, "safety",
                ("liveInputEnabled", "Input live"),
                ("packetInjectionEnabled", "Iniezione pacchetti"),
                ("requireClientHealthy", "Client sano richiesto"),
                ("requireGuardApproval", "Guard richiesto"),
                ("executionMode", "Esecuzione"),
                ("sessionActuating", "Sessione attuante"),
                ("sessionAuthorityReason", "Motivo autorità"),
                ("sessionAuthorityTerminal", "Verdetto terminale"),
                ("runtimeIntegrity", "Integrità runtime"),
                ("clientIntegrity", "Integrità client")),
            Resilience = ReadObject(root, "resilience",
                ("state", "Stato breaker"),
                ("failuresInWindow", "Fallimenti in finestra"),
                ("cooldownRemainingSeconds", "Attesa prossimo tentativo (s)"),
                ("windowSize", "Budget finestra"),
                ("probeSuccessesToClose", "Prove per chiudere"),
                ("baseCooldownSeconds", "Cooldown base (s)"),
                ("maxCooldownSeconds", "Cooldown massimo (s)"),
                ("currentCooldownSeconds", "Cooldown in vigore (s)"),
                ("halts", "Arresti")),
            GameObservation = ReadObservation(root),
            SessionAuthorityLine = SnapshotView.AuthorityStatus(
                ReadClassifiedBool(safetyNode, "sessionActuating"),
                ReadClassifiedText(safetyNode, "sessionAuthorityReason"),
                ReadClassifiedBool(safetyNode, "sessionAuthorityTerminal")),
            ClientProcessId = ReadClassifiedNullableInt(client, "processId", "process_not_attached"),
            ObservationLastHp = ReadClassifiedInt(gameObservation, "lastHp", "game_observation_absent"),
            ObservationLastMaxHp = ReadClassifiedInt(gameObservation, "lastMaxHp", "game_observation_absent"),
            Entities = gameplay.Entities,
            HitBy = gameplay.HitBy,
            HasTarget = gameplay.HasTarget
        };
    }

    private static IReadOnlyList<DisplayField> ReadObservation(JsonElement root)
    {
        if (!root.TryGetProperty("gameObservation", out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            const string reason = "game_observation_absent";
            return
            [
                new DisplayField("Canale osservazione gioco", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Endpoint osservato", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Pacchetti osservati", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Pacchetti decodificati", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Pacchetti non decodificabili", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Ultimo HP", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Ultimo HP massimo", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Ultimo MP", $"UNKNOWN · {reason}", "UNKNOWN"),
                new DisplayField("Timestamp vitals", $"UNKNOWN · {reason}", "UNKNOWN")
            ];
        }

        return ReadObject(root, "gameObservation",
            ("active", "Canale osservazione gioco"),
            ("endpoint", "Endpoint osservato"),
            ("packetsObserved", "Pacchetti osservati"),
            ("packetsDecoded", "Pacchetti decodificati"),
            ("packetsUndecodable", "Pacchetti non decodificabili"),
            ("lastHp", "Ultimo HP"),
            ("lastMaxHp", "Ultimo HP massimo"),
            ("lastMp", "Ultimo MP"),
            ("lastVitalsAtUtc", "Timestamp vitals"));
    }

    private static IReadOnlyList<DisplayField> ReadObject(JsonElement root, string name, params (string Key, string Label)[] fields)
    {
        if (!root.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return [new DisplayField(name, "UNKNOWN", "UNKNOWN")];

        var list = new List<DisplayField>(fields.Length);
        foreach (var (key, label) in fields)
        {
            if (!obj.TryGetProperty(key, out var node))
            {
                list.Add(new DisplayField(label, "UNKNOWN", "UNKNOWN"));
                continue;
            }

            if (node.ValueKind != JsonValueKind.Object)
            {
                list.Add(new DisplayField(label, node.ToString(), "DERIVED"));
                continue;
            }

            var source = node.TryGetProperty("source", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN";
            var hasValue = node.TryGetProperty("value", out var v) && v.ValueKind is not JsonValueKind.Null;
            if (!hasValue || source == "UNKNOWN")
            {
                var reason = node.TryGetProperty("failureReason", out var r) ? r.GetString() : null;
                list.Add(new DisplayField(label, string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : $"UNKNOWN · {reason}", "UNKNOWN"));
            }
            else
            {
                list.Add(new DisplayField(label, $"{v} [{source}]", source));
            }
        }

        return list;
    }

    private static IReadOnlyList<DisplayField> PrependChannel(IReadOnlyList<DisplayField> guard, string slot)
        =>
        [
            new DisplayField("Wire (questo build)", ChannelView.WireLabel, "DERIVED"),
            new DisplayField("Slot", slot, slot == "UNKNOWN" ? "UNKNOWN" : "DERIVED"),
            .. guard
        ];

    private static bool? ReadClassifiedBool(JsonElement? obj, string key)
    {
        if (obj is not { } root || !root.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object)
            return null;
        var source = node.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (source == "UNKNOWN")
            return null;
        if (!node.TryGetProperty("value", out var v) || v.ValueKind is JsonValueKind.Null)
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(v.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ReadClassifiedText(JsonElement? obj, string key)
    {
        if (obj is not { } root || !root.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object)
            return null;
        var source = node.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (source == "UNKNOWN")
            return null;
        if (!node.TryGetProperty("value", out var v) || v.ValueKind is JsonValueKind.Null)
            return null;
        var text = v.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static ClassifiedValue<int> ReadClassifiedInt(JsonElement? obj, string key, string missing)
    {
        if (!TryReadClassifiedNumber(obj, key, missing, out var value, out var source, out var reason))
            return ClassifiedValue<int>.Unknown(reason);
        return Classify(value, source);
    }

    private static ClassifiedValue<int?> ReadClassifiedNullableInt(JsonElement? obj, string key, string missing)
    {
        if (!TryReadClassifiedNumber(obj, key, missing, out var value, out var source, out var reason))
            return ClassifiedValue<int?>.Unknown(reason);
        return Classify((int?)value, source);
    }

    private static bool TryReadClassifiedNumber(
        JsonElement? obj, string key, string missing, out int value, out DataSourceKind source, out string reason)
    {
        value = 0;
        source = DataSourceKind.Unknown;
        reason = missing;
        if (obj is not { } root || !root.TryGetProperty(key, out var node) || node.ValueKind != JsonValueKind.Object)
            return false;

        var sourceText = node.TryGetProperty("source", out var s) ? s.GetString() : null;
        if (node.TryGetProperty("failureReason", out var r) && r.GetString() is { Length: > 0 } named)
            reason = named;

        if (string.IsNullOrWhiteSpace(sourceText) || sourceText == "UNKNOWN")
            return false;
        if (!node.TryGetProperty("value", out var v) || v.ValueKind is JsonValueKind.Null)
            return false;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out value))
        {
            source = ParseSource(sourceText);
            return source != DataSourceKind.Unknown;
        }

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out value))
        {
            source = ParseSource(sourceText);
            return source != DataSourceKind.Unknown;
        }

        return false;
    }

    private static DataSourceKind ParseSource(string source) => source switch
    {
        "LIVE" => DataSourceKind.Live,
        "DERIVED" => DataSourceKind.Derived,
        "CACHED" => DataSourceKind.Cached,
        "SIMULATED" => DataSourceKind.Simulated,
        _ => DataSourceKind.Unknown
    };

    private static ClassifiedValue<T> Classify<T>(T value, DataSourceKind source) => source switch
    {
        DataSourceKind.Live => ClassifiedValue<T>.Live(value),
        DataSourceKind.Derived => ClassifiedValue<T>.Derived(value),
        DataSourceKind.Cached => ClassifiedValue<T>.Cached(value, DateTime.UtcNow),
        DataSourceKind.Simulated => ClassifiedValue<T>.Simulated(value),
        _ => ClassifiedValue<T>.Unknown("unclassified_source")
    };
}
