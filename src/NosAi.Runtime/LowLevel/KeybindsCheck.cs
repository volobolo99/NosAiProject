using System.Globalization;
using System.Text;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// One reading of the operator's keybinds against the intents the runtime asks for.
/// </summary>
/// <param name="Path">The file <see cref="KeybindMap.TryLoad"/> would read.</param>
/// <param name="Exists">Whether that file is present. Missing is not an empty map.</param>
/// <param name="LoadFailure">
/// Why the file could not be parsed, or null when it loaded. Distinct from a missing
/// file: an empty or malformed file is present and still covers nothing.
/// </param>
/// <param name="Configured">Every intent the file bound, in ordinal order.</param>
/// <param name="UncoveredPrefixes">
/// Runtime intent prefixes with no <b>confirmed</b> bind. A prefix covered only by
/// declared binds is uncovered here, because a declared bind refuses at the press
/// boundary with <c>keybind_not_confirmed</c>: coverage means the runtime can act,
/// not that a line exists in the file.
/// </param>
/// <param name="DeclaredIntents">
/// Configured intents whose effect has never been observed on this client. They
/// load, they are listed, and they do not fire.
/// </param>
public readonly record struct KeybindsCheckReport(
    string Path,
    bool Exists,
    string? LoadFailure,
    IReadOnlyList<KeybindCheckEntry> Configured,
    IReadOnlyList<string> UncoveredPrefixes,
    IReadOnlyList<string> DeclaredIntents)
{
    /// <summary>
    /// True only when the file is present, parses, and every runtime prefix has a
    /// confirmed bind — the condition under which a press actually leaves.
    /// </summary>
    public bool Ok => Exists && LoadFailure is null && UncoveredPrefixes.Count == 0;
}

/// <summary>One configured intent, as the check prints it.</summary>
public readonly record struct KeybindCheckEntry(
    string Intent,
    ushort VirtualKey,
    string Label,
    bool Confirmed);

/// <summary>
/// Prints which intents the operator has bound, and which the runtime can ask
/// for that are not bound (C3-1).
/// </summary>
/// <remarks>
/// <para>
/// The runtime asks for a key by composing a name in
/// <c>InputActionEffector</c>: <c>consumable.{slot}</c> for a potion,
/// <c>skill.{id}</c> for a skill. There is no closed catalogue of slot numbers
/// or skill ids — those are the operator's quickbar — so coverage is by prefix:
/// a prefix the runtime asks for is uncovered until at least one configured
/// intent starts with it. An extra bind that does not start with one of those
/// prefixes is listed as configured and does not cover them.
/// </para>
/// <para>
/// The real file stays with the operator. This command does not write it.
/// </para>
/// </remarks>
public static class KeybindsCheck
{
    /// <summary>
    /// Intent prefixes <c>InputActionEffector</c> actually constructs, in the
    /// order the planner needs them: heal first, then attack
    /// (<c>docs/TASTI_E_BERSAGLIO.md</c> § 4.2). A configured intent covers a
    /// prefix only by starting with it.
    /// </summary>
    public static readonly string[] RuntimeIntentPrefixes =
    [
        ConsumablePrefix,
        SkillPrefix
    ];

    /// <summary>Prefix of every <c>UseConsumable</c> intent the effector asks for.</summary>
    public const string ConsumablePrefix = "consumable.";

    /// <summary>Prefix of every <c>UseSkill</c> intent the effector asks for.</summary>
    public const string SkillPrefix = "skill.";

    /// <summary>Console entry for <c>--keybinds-check</c>.</summary>
    public static int Run(string? path = null)
    {
        KeybindsCheckReport report = Inspect(path ?? ResolvePath());
        Console.Write(Format(report));
        return report.Ok ? 0 : 1;
    }

    /// <summary>
    /// The expected file, resolved from the repository root when one is found and
    /// from the current directory otherwise — the same rule the other data-file
    /// probes use, rather than the output directory of this assembly.
    /// </summary>
    public static string ResolvePath()
    {
        string root = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                      ?? TestSuiteRunner.FindRepositoryRoot()
                      ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, KeybindMap.RelativePath));
    }

    /// <summary>Reads the file at <paramref name="path"/> without writing anything.</summary>
    public static KeybindsCheckReport Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        bool exists = File.Exists(path);
        var configured = new List<KeybindCheckEntry>();
        string? loadFailure = null;

        if (exists)
        {
            if (KeybindMap.TryLoad(path, out KeybindMap map, out loadFailure))
            {
                loadFailure = null;
                foreach (string intent in map.ConfiguredIntents)
                {
                    if (!map.TryGet(intent, out Keybind bind))
                        continue;

                    configured.Add(new KeybindCheckEntry(
                        intent, bind.VirtualKey, bind.Label, bind.Confirmed));
                }
            }
        }
        else
        {
            loadFailure = "file_not_found";
        }

        var uncovered = new List<string>(RuntimeIntentPrefixes.Length);
        foreach (string prefix in RuntimeIntentPrefixes)
        {
            var covered = false;
            for (var i = 0; i < configured.Count; i++)
            {
                // Only a confirmed bind covers a prefix. A declared one is listed
                // and still refuses when the press is attempted, so counting it as
                // coverage would report a runtime that cannot act as ready.
                if (configured[i].Confirmed
                    && configured[i].Intent.StartsWith(prefix, StringComparison.Ordinal))
                {
                    covered = true;
                    break;
                }
            }

            if (!covered)
                uncovered.Add(prefix + "*");
        }

        var declared = new List<string>();
        for (var i = 0; i < configured.Count; i++)
        {
            if (!configured[i].Confirmed)
                declared.Add(configured[i].Intent);
        }

        return new KeybindsCheckReport(path, exists, loadFailure, configured, uncovered, declared);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(in KeybindsCheckReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"path:    {report.Path}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"exists:  {(report.Exists ? "true" : "false")}"));

        if (report.LoadFailure is { } failure && report.Exists)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"load:    {failure}"));

        if (report.Configured.Count == 0)
        {
            text.AppendLine("configured: (none)");
        }
        else
        {
            text.AppendLine("configured:");
            foreach (KeybindCheckEntry entry in report.Configured)
            {
                string state = entry.Confirmed ? "confirmed" : "declared";
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {entry.Intent}  vk={entry.VirtualKey}  label={entry.Label}  {state}"));
            }
        }

        if (report.DeclaredIntents.Count > 0)
        {
            text.AppendLine("declared, will refuse until confirmed:");
            foreach (string intent in report.DeclaredIntents)
                text.AppendLine("  " + intent);
        }

        if (report.UncoveredPrefixes.Count == 0)
        {
            text.AppendLine("uncovered: (none)");
        }
        else
        {
            text.AppendLine("uncovered (heal then attack):");
            foreach (string prefix in report.UncoveredPrefixes)
                text.AppendLine("  " + prefix);
        }

        return text.ToString();
    }
}
