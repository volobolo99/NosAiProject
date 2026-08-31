using System.Text.Json;

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
                ("executionMode", "Esecuzione"))
        };
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
}
