using NosAi.Runtime.LowLevel;

namespace NosAi.ControlPanel;

/// <summary>Operator-facing keybind row: configured intents and named missing ones.</summary>
internal sealed class KeybindsView
{
    public bool FileExists { get; init; }
    public string? LoadFailure { get; init; }
    public IReadOnlyList<DisplayField> Fields { get; init; } = Array.Empty<DisplayField>();
}

/// <summary>
/// Reads what <see cref="KeybindsCheck"/> already exposes. A missing intent is
/// drawn with its name and <see cref="MissingLabel"/>, never as a blank row.
/// This inspect does not create or alter the operator's file.
/// </summary>
internal static class KeybindsInspect
{
    /// <summary>Operator-facing mark for an uncovered runtime intent prefix.</summary>
    public const string MissingLabel = "mancante";

    /// <summary>
    /// Operator-facing mark for a bind that is present but never observed on this
    /// client. The row exists precisely so a declared bind does not read like a
    /// working one: the runtime refuses it with <c>keybind_not_confirmed</c>.
    /// </summary>
    public const string DeclaredLabel = "dichiarato, non premera'";

    /// <summary>Formats the keybind row from the file <see cref="KeybindsCheck"/> would read.</summary>
    public static KeybindsView Inspect(string path)
    {
        KeybindsCheckReport report = KeybindsCheck.Inspect(path);
        var fields = new List<DisplayField>(2 + report.Configured.Count + report.UncoveredPrefixes.Count);

        if (report.LoadFailure is { } failure)
        {
            fields.Add(new DisplayField("File tasti", $"UNKNOWN · {failure}", "UNKNOWN"));
        }
        else
        {
            fields.Add(new DisplayField("File tasti", report.Path, "LIVE"));
        }

        foreach (KeybindCheckEntry entry in report.Configured)
        {
            // A declared bind reads as configured everywhere else; here it has to
            // read as what it is, or the panel would show a runtime ready to press
            // a key that the effector will refuse.
            string value = entry.Confirmed
                ? $"vk={entry.VirtualKey} label={entry.Label}"
                : $"vk={entry.VirtualKey} label={entry.Label} · {DeclaredLabel}";

            fields.Add(new DisplayField(entry.Intent, value, entry.Confirmed ? "LIVE" : "DERIVED"));
        }

        string missingSource = report.LoadFailure is null ? "DERIVED" : "UNKNOWN";
        foreach (string prefix in report.UncoveredPrefixes)
        {
            fields.Add(new DisplayField(prefix, MissingLabel, missingSource));
        }

        return new KeybindsView
        {
            FileExists = report.Exists,
            LoadFailure = report.LoadFailure,
            Fields = fields
        };
    }
}
