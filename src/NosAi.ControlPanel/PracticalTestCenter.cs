using System.Text.Json;
using NosAi.Core.Testing;
using NosAi.Runtime.Observability;

namespace NosAi.ControlPanel;

/// <summary>
/// The verdict a practical test evaluates to against one canonical snapshot,
/// plus the evidence path and the detail the operator sees.
/// </summary>
internal sealed record PracticalTestVerdict(PracticalTestResult Result, string Evidence, string Detail);

/// <summary>
/// Pure decision logic for the Live Test Center: which verdict a test deserves
/// for a given snapshot, and how the live fields are rendered from the wire.
/// Kept here, outside the Window, so the behaviours the operator relies on are
/// testable without a dispatcher.
/// </summary>
internal static class PracticalTestCenter
{
    private const string EntitiesPath = "canonical.client.gameplayBaseline.value.entities";
    private const string InventoryPath = "canonical.client.gameplayBaseline.value.inventory";

    public static PracticalTestVerdict Evaluate(PracticalTestDefinition definition, JsonElement snapshot, DateTime nowUtc, DateTime snapshotReceivedAtUtc)
    {
        PracticalTestResult result;
        string evidence;
        string detail;
        switch (definition.Id)
        {
            case "T1":
                result = Both(snapshot, "client", "attached", "processDetected") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.attached+processDetected";
                detail = "Attach osservato dal runtime.";
                break;
            case "T2":
                result = Both(snapshot, "client", "windowDetected", "windowVisible") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.client.windowDetected+windowVisible";
                detail = "Non certifica ancora il contenuto di un frame WGC reale.";
                break;
            case "T3":
                result = ReadBool(snapshot, "gameObservation", "active") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.active";
                detail = "Observation channel attivo.";
                break;
            case "T4":
                result = ReadLong(snapshot, "gameObservation", "packetsDecoded") > 0 ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.gameObservation.packetsDecoded";
                detail = "Decodifica osservata; non equivale ancora a WorldState completo.";
                break;
            case "T5":
                {
                    bool entitiesPresent = TryReadArray(snapshot, "entities", out _, out string? reason);
                    result = entitiesPresent ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                    evidence = EntitiesPath;
                    detail = entitiesPresent
                        ? "Superficie spaziale presente ma serve una verifica before/after di movimento/replan."
                        : $"Superficie spaziale non osservata: {reason}.";
                    break;
                }
            case "T6":
                result = ReadBool(snapshot, "guard", "authenticated") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.guard.authenticated";
                detail = "Guard autenticato non prova una decisione combat eseguita e verificata.";
                break;
            case "T7":
                result = PracticalTestResult.Blocked;
                evidence = "quest_state_not_published";
                detail = "Manca un contratto quest/interaction live verificabile.";
                break;
            case "T8":
                {
                    bool inventoryPresent = TryReadArray(snapshot, "inventory", out _, out string? reason);
                    result = inventoryPresent ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                    evidence = InventoryPath;
                    detail = inventoryPresent
                        ? "Inventario pubblicato; resta da distinguere equipaggiato da zaino (T-12) e verificare il delta prima/dopo."
                        : $"Inventario non osservato: {reason}.";
                    break;
                }
            case "T9":
                result = ReadBool(snapshot, "guard", "authenticated") && ReadBool(snapshot, "safety", "requireGuardApproval") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.guard+safety";
                detail = "Precondizioni presenti, ma manca evidenza execution/verification completa.";
                break;
            case "T10":
                result = HasProperty(snapshot, "resilience", "state") ? PracticalTestResult.Unknown : PracticalTestResult.Blocked;
                evidence = "canonical.resilience";
                detail = "Recovery state esposto; la perturbazione fisica deve ancora essere eseguita.";
                break;
            case "T11":
                result = HasProperty(snapshot, "hardware", "logicalCores") && HasProperty(snapshot, "hardware", "systemRamMb") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.hardware";
                detail = "Hardware/runtime osservato dal processo.";
                break;
            case "T12":
                result = HasProperty(snapshot, "safety", "executionMode") && HasProperty(snapshot, "safety", "requireGuardApproval") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.safety";
                detail = "Safety policy esposta nel canonical snapshot.";
                break;
            case "T13":
                result = HasProperty(snapshot, "guard", "connected") && HasProperty(snapshot, "guard", "authenticated") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.guard";
                detail = "Guard state osservato; la certificazione finale richiede scenario reale.";
                break;
            case "T14":
                {
                    string? status = ReadRootString(snapshot, "runtimeStatus");
                    result = status is { Length: > 0 } && status != "Failed" ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                    evidence = "canonical.runtimeStatus";
                    detail = "Runtime status osservato.";
                    break;
                }
            case "T15":
                {
                    long ageMs = Math.Max(0, (long)(nowUtc - snapshotReceivedAtUtc).TotalMilliseconds);
                    result = ageMs <= 1000 && HasProperty(snapshot, "capturedAtUtc") && HasProperty(snapshot, "correlationId") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                    evidence = "canonical.capturedAtUtc+correlationId";
                    detail = $"Snapshot age lato Dashboard={ageMs} ms.";
                    break;
                }
            case "T16":
                result = HasProperty(snapshot, "client", "attached") && HasProperty(snapshot, "gameObservation", "active") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.classified-values";
                detail = "Classified values presenti; UNKNOWN resta esplicito.";
                break;
            case "T17":
                result = PracticalTestResult.Blocked;
                evidence = "event-log-endpoint-required";
                detail = "Serve il controllo persistente del journal/gap; non viene inferito dal solo snapshot.";
                break;
            case "T18":
                result = PracticalTestResult.Blocked;
                evidence = "controlled-reconnect-required";
                detail = "Richiede perturbazione controllata e verifica before/after.";
                break;
            case "T19":
                result = HasProperty(snapshot, "safety", "sessionAuthorityTerminal") && HasProperty(snapshot, "safety", "sessionAuthorityReason") ? PracticalTestResult.Pass : PracticalTestResult.Unknown;
                evidence = "canonical.safety.sessionAuthority";
                detail = "Il pannello osserva l'autorità e non può autorizzare autonomamente l'esecuzione.";
                break;
            case "T20":
                result = PracticalTestResult.Blocked;
                evidence = "certification_requires_physical_e2e";
                detail = "Richiede evidenze fisiche T1-T19 nel server privato e build .exe riproducibile.";
                break;
            default:
                result = PracticalTestResult.Blocked;
                evidence = "unsupported_test";
                detail = "Test non riconosciuto.";
                break;
        }

        return new PracticalTestVerdict(result, evidence, detail);
    }

    public static IReadOnlyList<DisplayField> ReadLiveFields(JsonElement root)
    {
        var fields = new List<DisplayField>();
        AddRoot(fields, root, "runtimeStatus", "Runtime", "Local");
        AddRoot(fields, root, "contractVersion", "Contratto", "Local");
        AddRoot(fields, root, "capturedAtUtc", "Snapshot UTC", "Local");
        AddRoot(fields, root, "correlationId", "Correlation ID", "Local");
        AddSection(fields, root, "client", "attached", "Client attached", "Local");
        AddSection(fields, root, "client", "processDetected", "Processo rilevato", "Local");
        AddSection(fields, root, "client", "windowDetected", "Finestra rilevata", "Screen");
        AddSection(fields, root, "client", "windowVisible", "Finestra visibile", "Screen");
        AddSection(fields, root, "client", "processResponding", "Processo risponde", "Local");
        AddSection(fields, root, "client", "networkConnected", "Network connected", "Network");
        AddSection(fields, root, "client", "serverEndpoint", "Endpoint osservato", "Network");
        AddSection(fields, root, "gameObservation", "active", "Osservazione attiva", "Network");
        AddSection(fields, root, "gameObservation", "packetsObserved", "Pacchetti osservati", "Network");
        AddSection(fields, root, "gameObservation", "packetsDecoded", "Pacchetti decodificati", "Network");
        AddSection(fields, root, "gameObservation", "packetsUndecodable", "Pacchetti non decodificabili", "Network");
        AddSection(fields, root, "gameObservation", "lastHp", "HP", "Network");
        AddSection(fields, root, "gameObservation", "lastMaxHp", "Max HP", "Network");
        AddSection(fields, root, "gameObservation", "lastMp", "MP", "Network");
        AddSection(fields, root, "safety", "executionMode", "Execution mode", "Local");
        AddSection(fields, root, "safety", "sessionActuating", "Session actuating", "Local");
        AddSection(fields, root, "safety", "sessionAuthorityTerminal", "Authority terminal", "Local");
        AddSection(fields, root, "guard", "connected", "Guard connected", "Local");
        AddSection(fields, root, "guard", "authenticated", "Guard authenticated", "Local");
        AddSection(fields, root, "resilience", "state", "Recovery state", "Local");
        AddSection(fields, root, "resilience", "failuresInWindow", "Recovery failures", "Local");
        AddSection(fields, root, "resilience", "halts", "Recovery halts", "Local");
        return fields;
    }

    private static void AddRoot(List<DisplayField> fields, JsonElement root, string property, string label, string category)
        => fields.Add(new DisplayField(label, RenderRoot(root, property), category));

    private static void AddSection(List<DisplayField> fields, JsonElement root, string section, string property, string label, string category)
    {
        if (!root.TryGetProperty(section, out JsonElement node) || !node.TryGetProperty(property, out JsonElement value))
        {
            fields.Add(new DisplayField(label, "UNKNOWN", category));
            return;
        }
        fields.Add(new DisplayField(label, Unwrap(value), SourceOf(value, category)));
    }

    private static string RenderRoot(JsonElement root, string property)
        => root.TryGetProperty(property, out JsonElement value) ? Unwrap(value) : "UNKNOWN";

    /// <summary>
    /// The value text of one wire element. A classified value with a null
    /// <c>value</c> is UNKNOWN with its reason — never an empty cell, and never
    /// a zero — because an unobserved reading is not a blank.
    /// </summary>
    private static string Unwrap(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out JsonElement classified))
        {
            if (classified.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                string reason = FailureReason(value);
                return string.IsNullOrWhiteSpace(reason) ? "UNKNOWN" : $"UNKNOWN · {reason}";
            }
            return classified.ToString();
        }
        return value.ToString();
    }

    /// <summary>
    /// The provenance of one classified value: its own <c>source</c> field, so a
    /// CACHED or SIMULATED reading is never drawn as if it were LIVE. The caller's
    /// category ("Local"/"Network"/"Screen") is only a fallback for plain values
    /// that carry no <c>source</c>.
    /// </summary>
    private static string SourceOf(JsonElement value, string fallback)
    {
        if (value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("source", out JsonElement source)
            && source.GetString() is { Length: > 0 } named)
            return named;
        return fallback;
    }

    private static string FailureReason(JsonElement classified)
        => classified.TryGetProperty("failureReason", out JsonElement reason)
            && reason.GetString() is { Length: > 0 } named
                ? named
                : "";

    private static bool Both(JsonElement root, string section, string a, string b)
        => ReadBool(root, section, a) && ReadBool(root, section, b);

    private static bool ReadBool(JsonElement root, string section, string property)
        => bool.TryParse(ReadClassified(root, section, property), out var value) && value;

    private static long ReadLong(JsonElement root, string section, string property)
        => long.TryParse(ReadClassified(root, section, property), out var value) ? value : 0;

    private static string? ReadClassified(JsonElement root, string section, string property)
    {
        if (!root.TryGetProperty(section, out var node) || !node.TryGetProperty(property, out var value)) return null;
        return Unwrap(value);
    }

    private static bool HasProperty(JsonElement root, string section)
        => root.TryGetProperty(section, out _);

    private static bool HasProperty(JsonElement root, string section, string property)
        => root.TryGetProperty(section, out var node) && node.TryGetProperty(property, out _);

    private static string? ReadRootString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) ? value.ToString() : null;

    /// <summary>
    /// Reads one named list from the gameplay baseline and reports whether it is
    /// present and non-empty, plus the reason it is not. The path follows
    /// <see cref="OperatorApiSnapshot"/>: <c>client.gameplayBaseline.value.&lt;name&gt;</c>.
    /// </summary>
    private static bool TryReadArray(JsonElement snapshot, string name, out int count, out string? reason)
    {
        count = 0;
        reason = null;
        if (!OperatorApiSnapshot.TryGameplayBaseline(snapshot, out JsonElement baseline, out string? baselineReason))
        {
            reason = baselineReason;
            return false;
        }
        if (!OperatorApiSnapshot.TryField(baseline, name, out JsonElement list, out string? fieldReason))
        {
            reason = fieldReason;
            return false;
        }
        if (list.ValueKind != JsonValueKind.Array)
        {
            reason = $"{name}_not_an_array";
            return false;
        }
        count = list.GetArrayLength();
        return count > 0;
    }
}
