using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NosAi.Runtime.LowLevel;

namespace NosAi.ControlPanel;

/// <summary>
/// Logica pura della card «Conferma keybind»: riceve e restituisce stringhe,
/// non tocca il disco e non ha riferimenti WPF. Una conferma dichiara cosa e'
/// stato osservato (MP scesi, HP saliti, slot nominato da <c>sr</c>); senza
/// quell'osservazione il bind resta dichiarato e il runtime lo rifiuta.
/// </summary>
internal static class KeybindConfirmCard
{
    public const string RelativePath = "data/keybinds.json";

    // Scrittura indentata che non trasforma i caratteri non ASCII in sequenze
    // \uXXXX: l'encoder di default lo farebbe e il documento dell'operatore
    // diventerebbe illeggibile a mano.
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Una riga per intento: nome, tasto virtuale, etichetta e stato confermato o dichiarato.</summary>
    public static string Describe(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return $"{RelativePath} non leggibile: documento vuoto.";
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return $"{RelativePath} non leggibile: JSON malformato ({ex.Message}).";
        }

        if (rootNode is not JsonObject document)
        {
            return $"{RelativePath} non leggibile: la radice non è un oggetto JSON.";
        }

        if (document["version"] is not JsonValue versionValue
            || !versionValue.TryGetValue<int>(out int version)
            || version != 1)
        {
            return $"{RelativePath} non leggibile: atteso version 1.";
        }

        if (document["binds"] is not JsonObject binds)
        {
            return $"{RelativePath} non leggibile: manca l'oggetto binds.";
        }

        var lines = new List<string>();
        foreach (KeyValuePair<string, JsonNode?> property in binds)
        {
            string intent = property.Key;
            string virtualKey = "?";
            string label = "?";
            bool confirmed = false;

            if (property.Value is JsonObject bind)
            {
                if (bind["virtualKey"] is JsonValue virtualKeyValue
                    && virtualKeyValue.TryGetValue<int>(out int virtualKeyNumber))
                {
                    virtualKey = virtualKeyNumber.ToString(CultureInfo.InvariantCulture);
                }

                if (bind["label"] is JsonValue labelValue
                    && labelValue.TryGetValue<string>(out string? labelText)
                    && labelText is not null)
                {
                    label = labelText;
                }

                confirmed = bind["confirmed"] is JsonValue confirmedValue
                    && confirmedValue.TryGetValue<bool>(out bool confirmedFlag)
                    && confirmedFlag;
            }

            string state = confirmed
                ? "confermato"
                : "dichiarato, verrà rifiutato alla pressione";

            lines.Add($"{intent}  vk={virtualKey}  label={label}  {state}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Conferma un intento già presente in <c>binds</c> solo se l'operatore
    /// dichiara cosa ha osservato. Non scrive nulla su disco: il documento
    /// aggiornato esce da <paramref name="updatedJson"/>.
    /// </summary>
    public static bool TryConfirm(
        string json,
        string? intent,
        string? virtualKeyText,
        string? observation,
        out string updatedJson,
        out string? refusal)
    {
        updatedJson = json ?? string.Empty;
        refusal = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            refusal = $"{RelativePath} non leggibile: documento vuoto.";
            return false;
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            refusal = $"{RelativePath} non leggibile: JSON malformato ({ex.Message}).";
            return false;
        }

        if (rootNode is not JsonObject document)
        {
            refusal = $"{RelativePath} non leggibile: la radice non è un oggetto JSON.";
            return false;
        }

        if (document["version"] is not JsonValue versionValue
            || !versionValue.TryGetValue<int>(out int version)
            || version != 1)
        {
            refusal = $"{RelativePath} non leggibile: atteso version 1.";
            return false;
        }

        if (document["binds"] is not JsonObject binds)
        {
            refusal = $"{RelativePath} non leggibile: manca l'oggetto binds.";
            return false;
        }

        string cleanIntent = (intent ?? string.Empty).Trim();
        if (cleanIntent.Length == 0)
        {
            refusal = "Intento vuoto: inserisci nel campo intento l'intento da confermare.";
            return false;
        }

        if (binds[cleanIntent] is not JsonObject bind)
        {
            refusal = $"Intento «{cleanIntent}» non presente in binds: si conferma solo un intento già configurato in {RelativePath}.";
            return false;
        }

        if (!int.TryParse(
                (virtualKeyText ?? string.Empty).Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int virtualKey)
            || virtualKey < KeybindMap.MinVirtualKey
            || virtualKey > KeybindMap.MaxVirtualKey)
        {
            refusal = $"Tasto non valido: il campo tasto deve contenere un intero da {KeybindMap.MinVirtualKey} a {KeybindMap.MaxVirtualKey}.";
            return false;
        }

        string cleanObservation = (observation ?? string.Empty).Trim();
        if (cleanObservation.Length == 0)
        {
            refusal = "Osservazione vuota: per confermare devi dichiarare nel campo osservazione cosa hai osservato (MP scesi, HP saliti, slot nominato da sr).";
            return false;
        }

        // La modifica è in luogo sul solo bind richiesto: virtualKey allineato,
        // conferma vera e nota di ciò che è stato osservato. version, _readme e
        // gli altri bind non vengono toccati.
        bind["virtualKey"] = JsonValue.Create(virtualKey);
        bind["confirmed"] = JsonValue.Create(true);
        bind["confirmedNote"] = JsonValue.Create(cleanObservation);
        bind["confirmedAtUtc"] = JsonValue.Create(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        updatedJson = document.ToJsonString(SaveOptions);
        refusal = null;
        return true;
    }

    /// <summary>Riga di riepilogo da mostrare dopo il salvataggio della conferma.</summary>
    public static string SummariseConfirmation(string intent, int virtualKey, string observation)
    {
        return $"Confermato {intent} sul tasto {virtualKey}; osservazione registrata: {observation}.";
    }
}
