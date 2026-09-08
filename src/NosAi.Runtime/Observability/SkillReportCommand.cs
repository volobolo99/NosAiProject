using System.Globalization;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Prints the reference catalogue's skill fields next to what the wire actually
/// observed (CLI <c>--skill-report</c>), marking each field confirmed or
/// provisional. Read-only: it opens the catalogue and, in <c>--recording</c> mode,
/// reads a capture; it never writes, imports or feeds the planner.
/// </summary>
/// <remarks>
/// <para>
/// The artefact that makes the AP-05/A2+A4 measures readable: a human can see, per
/// field, whether its in-game meaning has been cross-checked against a real
/// observation or is still a provisional read of the client's table. Without it
/// the numbers live only in a report nobody rereads.
/// </para>
/// <para>
/// A missing catalogue is a named answer, not an error and not an empty catalogue:
/// <see cref="GameReferenceLocator"/> already separates "no volume" from "no file",
/// and this command prints whichever it found rather than printing zero skills.
/// </para>
/// </remarks>
public static class SkillReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--skill-report";

    /// <summary>Reports one skill by vnum; its value is the vnum.</summary>
    public const string VnumOption = "--vnum";

    /// <summary>Reports every player skill observed in a capture; its value is the path.</summary>
    public const string RecordingOption = "--recording";

    /// <summary>An argument starting with <c>--</c> that is not a known option.</summary>
    public const string UnknownOptionReason = "unknown_option";

    /// <summary><c>--vnum</c> carried no value or a value that is not an integer.</summary>
    public const string InvalidVnumReason = "invalid_vnum";

    /// <summary><c>--recording</c> carried no value.</summary>
    public const string RecordingWithoutValueReason = "recording_without_value";

    /// <summary>The dedicated volume (hence the catalogue) is absent.</summary>
    public const string CatalogueUnavailableReason = "skill_catalogue_unavailable";

    /// <summary>Neither <c>--vnum</c> nor <c>--recording</c> was given, so there is nothing to report.</summary>
    public const string NoTargetReason = "skill_report_no_target";

    /// <summary>Exit code for a named refusal. Matches the other diagnostics.</summary>
    public const int ExitRefused = 2;

    /// <summary>
    /// Validates the argument vector. Returns the named refusal reason, or null when
    /// well-formed (with the parsed vnum, recording path and language).
    /// </summary>
    public static string? TryParse(
        string[] args, out int? vnum, out string? recording, out string? language)
    {
        vnum = null;
        recording = null;
        language = "IT";

        int flagIndex = Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
            return NoTargetReason;

        for (int i = flagIndex + 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, VnumOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    return InvalidVnumReason;
                vnum = parsed;
                i++;
                continue;
            }

            if (string.Equals(arg, RecordingOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return RecordingWithoutValueReason;
                recording = args[++i];
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                return $"{UnknownOptionReason}:{arg}";
        }

        if (vnum is null && recording is null)
            return NoTargetReason;
        return null;
    }

    /// <summary>
    /// The pure core: writes one report per vnum. No console, no file I/O, nothing
    /// but the open database and the vnums handed over.
    /// </summary>
    public static void WriteReport(GameReferenceDatabase database, IEnumerable<int> vnums, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(vnums);
        ArgumentNullException.ThrowIfNull(output);

        var catalogue = new SkillCatalogue(database);
        foreach (int vnum in vnums.OrderBy(v => v))
        {
            SkillCatalogueLookup lookup = catalogue.Lookup(vnum);
            if (!lookup.Ok)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"skill {vnum}: {lookup.FailureReason}"));
                continue;
            }

            CataloguedSkill skill = lookup.Skill!;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"skill {vnum}: {skill.Name ?? "(senza nome)"}"));
            WriteField(output, "TYPE[1] cast_id", skill.CastId);
            WriteField(output, "TYPE[2] job_class", skill.JobClass);
            WriteField(output, "DATA[5] cooldown_tenths", skill.CooldownTenths);
            WriteField(output, "TARGET[3] area_targets", skill.AreaTargets);
            WriteField(output, "COST[0] cp_cost", skill.CpCost);
            WriteField(output, "DATA[8] mp_cost", skill.MpCost);
            WriteField(output, "TARGET[2] range", skill.Range);
        }
    }

    /// <summary>The player skill vnums a recording actually observes, in wire order.</summary>
    /// <remarks>
    /// Uses the chronological tool (<c>--wire-inspect --timeline</c>) so the order is
    /// the capture's own, not a sorted-by-opcode view. A vnum appears once even if it
    /// is cast many times: the report is about the catalogue, not about the count.
    /// </remarks>
    public static IReadOnlyList<int> ObservedSkills(string recordingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);

        using IPacketSource source = CaptureFile.Open(recordingPath);
        var seen = new HashSet<int>();
        var ordered = new List<int>();

        foreach (WireTimelineEntry entry in WireInspectCommand.Timeline(source, DataSourceKind.Cached, opcodes: null, maxLines: 0))
        {
            string[] f = entry.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int? skill = f.Length switch
            {
                > 7 when f[0] == "ct" && f[1] == "1" => ParsePositive(f[7]),
                > 5 when f[0] == "su" && f[1] == "1" => ParsePositive(f[5]),
                _ => null,
            };
            if (skill is int vnum && seen.Add(vnum))
                ordered.Add(vnum);
        }
        return ordered;
    }

    /// <summary>Console entry. Validates, opens the catalogue from the volume, and reports.</summary>
    public static int Run(string[] args)
    {
        string? refusal = TryParse(args, out int? vnum, out string? recording, out _);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} --vnum <n> | --recording <file.noscap>");
            return ExitRefused;
        }

        if (!GameReferenceLocator.TryOpen(out GameReferenceDatabase? database, out string? catalogueReason)
            || database is null)
        {
            Console.WriteLine($"[REFUSED] {CatalogueUnavailableReason}:{catalogueReason}");
            return ExitRefused;
        }

        using (database)
        {
            if (vnum is int one)
            {
                WriteReport(database, new[] { one }, Console.Out);
                return 0;
            }

            if (!File.Exists(recording))
            {
                Console.WriteLine($"[REFUSED] recording_not_found:{recording}");
                return ExitRefused;
            }

            IReadOnlyList<int> observed = ObservedSkills(recording!);
            if (observed.Count == 0)
            {
                Console.WriteLine("skill: no_player_skill_observed");
                return 0;
            }
            WriteReport(database, observed, Console.Out);
            return 0;
        }
    }

    private static int? ParsePositive(string field)
        => int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : null;

    private static void WriteField(TextWriter output, string label, ClassifiedValue<int> field)
    {
        string value = field.HasValue
            ? field.Value.ToString(CultureInfo.InvariantCulture) + " [CONFERMATO]"
            : $"UNKNOWN [PROVVISORIO] ({field.FailureReason})";
        output.WriteLine($"  {label}: {value}");
    }
}
