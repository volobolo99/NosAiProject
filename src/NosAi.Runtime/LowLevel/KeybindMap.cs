using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace NosAi.Runtime.LowLevel;

/// <summary>Un gesto che l'operatore ha associato a un'intenzione.</summary>
public readonly record struct Keybind(ushort VirtualKey, string Label);

/// <summary>
/// Quali tasti valgono quali intenzioni, secondo l'operatore. Una voce assente
/// non ha un default: restituisce false, e chi chiama rifiuta l'azione.
/// </summary>
public sealed class KeybindMap
{
    public const int MinVirtualKey = 1;
    public const int MaxVirtualKey = 254;

    private readonly IReadOnlyDictionary<string, Keybind> _binds;

    private KeybindMap(IReadOnlyDictionary<string, Keybind> binds)
    {
        _binds = binds;
        ConfiguredIntents = new ReadOnlyCollection<string>(
            binds.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Mappa vuota: nessuna intenzione è associata a un tasto.</summary>
    public static KeybindMap Empty { get; } = new(new Dictionary<string, Keybind>(StringComparer.Ordinal));

    /// <summary>Le intenzioni configurate, in ordine alfabetico.</summary>
    public IReadOnlyCollection<string> ConfiguredIntents { get; }

    /// <summary>Legge la mappa dal JSON dell'operatore, o dice perché non ci è riuscita.</summary>
    /// <returns>false con <paramref name="failureReason"/> valorizzato in caso di problema.</returns>
    public static bool TryLoad(string path, out KeybindMap map, out string? failureReason)
    {
        map = Empty;
        failureReason = null;

        if (!File.Exists(path))
        {
            failureReason = "file_not_found";
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            failureReason = $"file_unreadable:{ex.GetType().Name}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            failureReason = "file_empty";
            return false;
        }

        try
        {
            return TryParse(text, out map, out failureReason);
        }
        catch (JsonException)
        {
            map = Empty;
            failureReason = "json_malformed";
            return false;
        }
    }

    /// <summary>Il tasto per un'intenzione, se l'operatore ne ha configurato uno.</summary>
    public bool TryGet(string intent, out Keybind bind)
    {
        bind = default;
        if (intent is null)
            return false;
        return _binds.TryGetValue(intent, out bind);
    }

    private static bool TryParse(string json, out KeybindMap map, out string? failureReason)
    {
        map = Empty;
        failureReason = null;

        if (!TryRejectDuplicateIntents(json, out failureReason))
            return false;

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            failureReason = "json_not_object";
            return false;
        }

        if (!root.TryGetProperty("version", out JsonElement versionNode)
            || versionNode.ValueKind != JsonValueKind.Number
            || !versionNode.TryGetInt32(out int version))
        {
            failureReason = "version_missing";
            return false;
        }

        if (version != 1)
        {
            failureReason = $"unsupported_version:{version}";
            return false;
        }

        if (!root.TryGetProperty("binds", out JsonElement bindsNode)
            || bindsNode.ValueKind != JsonValueKind.Object)
        {
            failureReason = "binds_missing";
            return false;
        }

        var binds = new Dictionary<string, Keybind>(StringComparer.Ordinal);
        foreach (JsonProperty property in bindsNode.EnumerateObject())
        {
            if (!TryReadBind(property.Value, out Keybind bind, out failureReason))
            {
                failureReason = $"{failureReason}:{property.Name}";
                return false;
            }

            binds[property.Name] = bind;
        }

        map = new KeybindMap(binds);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// <see cref="JsonDocument"/> keeps the last duplicate. Ambiguity has to be
    /// caught on the token stream, before a winner is chosen.
    /// </summary>
    private static bool TryRejectDuplicateIntents(string json, out string? failureReason)
    {
        failureReason = null;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return true;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            string? name = reader.GetString();
            if (name != "binds")
            {
                reader.Skip();
                continue;
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return true;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                string intent = reader.GetString() ?? "";
                if (!seen.Add(intent))
                {
                    failureReason = $"duplicate_intent:{intent}";
                    return false;
                }

                reader.Skip();
            }
        }

        return true;
    }

    private static bool TryReadBind(JsonElement node, out Keybind bind, out string? failureReason)
    {
        bind = default;
        failureReason = null;
        if (node.ValueKind != JsonValueKind.Object)
        {
            failureReason = "bind_not_object";
            return false;
        }

        if (!node.TryGetProperty("virtualKey", out JsonElement keyNode)
            || keyNode.ValueKind != JsonValueKind.Number
            || !keyNode.TryGetInt32(out int virtualKey))
        {
            failureReason = "virtual_key_missing";
            return false;
        }

        if (virtualKey is < MinVirtualKey or > MaxVirtualKey)
        {
            failureReason = $"virtual_key_out_of_range:{virtualKey}";
            return false;
        }

        if (!node.TryGetProperty("label", out JsonElement labelNode)
            || labelNode.ValueKind != JsonValueKind.String
            || labelNode.GetString() is not { } label)
        {
            failureReason = "label_missing";
            return false;
        }

        bind = new Keybind((ushort)virtualKey, label);
        return true;
    }
}
