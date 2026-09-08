using System.Globalization;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Prints the reference catalogue's monster fields next to what the wire actually
/// observed (CLI <c>--monster-report</c>), marking each field confirmed or
/// provisional. Read-only: it opens the catalogue and, in <c>--recording</c> mode,
/// reads a capture; it never writes, imports or feeds the planner.
/// </summary>
/// <remarks>
/// <para>
/// The monster counterpart of <see cref="SkillReportCommand"/>: a human can see,
/// per field, whether its in-game meaning has been cross-checked against a real
/// observation or is still a provisional read of the client's table. For monsters
/// exactly one field holds — the level, cross-checked against <c>st</c> field 3 —
/// and the report says so rather than implying the rest was verified.
/// </para>
/// <para>
/// A missing catalogue is a named answer, not an error and not an empty catalogue:
/// <see cref="GameReferenceLocator"/> already separates "no volume" from "no file",
/// and this command prints whichever it found rather than printing zero monsters.
/// </para>
/// </remarks>
public static class MonsterReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--monster-report";

    /// <summary>Reports one monster by vnum; its value is the vnum.</summary>
    public const string VnumOption = "--vnum";

    /// <summary>Reports every monster observed in a capture; its value is the path.</summary>
    public const string RecordingOption = "--recording";

    /// <summary>An argument starting with <c>--</c> that is not a known option.</summary>
    public const string UnknownOptionReason = "unknown_option";

    /// <summary><c>--vnum</c> carried no value or a value that is not an integer.</summary>
    public const string InvalidVnumReason = "invalid_vnum";

    /// <summary><c>--recording</c> carried no value.</summary>
    public const string RecordingWithoutValueReason = "recording_without_value";

    /// <summary>The dedicated volume (hence the catalogue) is absent.</summary>
    public const string CatalogueUnavailableReason = "monster_catalogue_unavailable";

    /// <summary>Neither <c>--vnum</c> nor <c>--recording</c> was given, so there is nothing to report.</summary>
    public const string NoTargetReason = "monster_report_no_target";

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

        var catalogue = new MonsterCatalogue(database);
        foreach (int vnum in vnums.OrderBy(v => v))
        {
            MonsterCatalogueLookup lookup = catalogue.Lookup(vnum);
            if (!lookup.Ok)
            {
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"monster {vnum}: {lookup.FailureReason}"));
                continue;
            }

            CataloguedMonster monster = lookup.Monster!;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"monster {vnum}: {monster.Name ?? "(senza nome)"}"));
            WriteField(output, "LEVEL level", monster.Level);
            WriteField(output, "HP/MP[0] max_hp_bonus", monster.MaxHpBonus);
            WriteField(output, "HP/MP[1] max_mp_bonus", monster.MaxMpBonus);
        }
    }

    /// <summary>The monster vnums a recording actually observes, in wire order.</summary>
    /// <remarks>
    /// <para>
    /// The vnum arrives only with <c>in</c> (<c>in type vnum id …</c>), so this
    /// reads field 2 of that opcode, restricted to the entity types the decoder
    /// reads (2 and 3) — a type-1 <c>in</c> carries a player name where a monster
    /// carries a vnum, and parsing it here would produce a number out of a name.
    /// </para>
    /// <para>
    /// Uses the chronological tool (<c>--wire-inspect --timeline</c>) so the order is
    /// the capture's own, not a sorted-by-opcode view. A vnum appears once even if it
    /// is sighted many times: the report is about the catalogue, not about the count.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> ObservedMonsters(string recordingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingPath);

        using IPacketSource source = CaptureFile.Open(recordingPath);
        var seen = new HashSet<int>();
        var ordered = new List<int>();

        foreach (WireTimelineEntry entry in WireInspectCommand.Timeline(source, DataSourceKind.Cached, opcodes: null, maxLines: 0))
        {
            string[] f = entry.Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length > 2 && f[0] == "in" && (f[1] == "2" || f[1] == "3"))
            {
                int? vnum = ParsePositive(f[2]);
                if (vnum is int number && seen.Add(number))
                    ordered.Add(number);
            }
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

            IReadOnlyList<int> observed = ObservedMonsters(recording!);
            if (observed.Count == 0)
            {
                Console.WriteLine("monster: no_monster_observed");
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
