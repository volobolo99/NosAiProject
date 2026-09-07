using System.Globalization;
using System.Linq;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.Runtime.Observability;

/// <summary>
/// One field of one opcode, measured across every packet of that opcode in a
/// recording: either constant (a single distinct value, carried out verbatim)
/// or varying (the count of distinct values it assumed).
/// </summary>
public readonly record struct WireFieldShape(int Position, string? ConstantValue, int DistinctCount);

/// <summary>
/// What one opcode looked like across a whole recording: how often it appeared,
/// how many fields each packet carried (every arity, not just the last), and the
/// shape of each field position up to the widest packet.
/// </summary>
public sealed record WireOpcodeCensus(
    string Opcode,
    long Count,
    IReadOnlyList<int> Arities,
    IReadOnlyList<WireFieldShape> Fields);

/// <summary>
/// Measures what the wire actually carries instead of guessing it
/// (CLI <c>--wire-inspect</c>). Offline and read-only: it opens a
/// <c>.noscap</c> and either censuses the field shape of every opcode, or
/// prints the raw lines of one opcode up to <c>--max</c>.
/// </summary>
/// <remarks>
/// <para>
/// No interpretation. The census counts packets, field arities and distinct
/// values per field position; it never assigns a meaning to a field. A field
/// that never changed is reported as constant, which is exactly the fact that
/// lets the reader tell "the server always sends this" apart from "nobody has
/// established this field's meaning" — the same rule
/// <c>CLAUDE.md</c> § <i>External reference data</i> states in words, here made
/// a column.
/// </para>
/// <para>
/// A file is CACHED by construction: the bytes were real when they were
/// captured and are not current now. The census and the raw lines both read the
/// file through <see cref="NosTaleWorldFramer.Factory(DataSourceKind)"/> with
/// <see cref="DataSourceKind.Cached"/>, so nothing recorded is ever labelled
/// live.
/// </para>
/// </remarks>
public static class WireInspectCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--wire-inspect";

    /// <summary>Selects the raw-lines mode; its value is the opcode to filter on.</summary>
    public const string OpcodeOption = "--opcode";

    /// <summary>Cap on raw lines in <c>--opcode</c> mode. <c>0</c> means all.</summary>
    public const string MaxOption = "--max";

    /// <summary>Per-field census of the opcode named by <c>--opcode</c>.</summary>
    public const string FieldsOption = "--fields";

    /// <summary>How many raw lines <c>--opcode</c> prints when <c>--max</c> is absent.</summary>
    public const int DefaultMaxLines = 20;

    /// <summary>How many example values <c>--fields</c> prints per field.</summary>
    /// <remarks>
    /// Five is enough to recognise a constant, an enum-like field (a handful of
    /// distinct values) and a free-running counter at a glance, while the min and
    /// max already bound a numeric field — printing every distinct value would
    /// flood the operator on a field that assumes hundreds.
    /// </remarks>
    public const int MaxFieldExamples = 5;

    /// <summary>Exit code for a refused argument vector. Matches the replay commands.</summary>
    public const int ExitRefused = 2;

    /// <summary>No recording exists at the given path.</summary>
    public const string MissingFileReason = "recording_not_found";

    /// <summary>The path is not a <c>.noscap</c> recording.</summary>
    public const string BadExtensionReason = "not_a_noscap";

    /// <summary><c>--max</c> carried a negative value.</summary>
    public const string NegativeMaxReason = "negative_max";

    /// <summary><c>--opcode</c> carried no value.</summary>
    public const string OpcodeWithoutValueReason = "opcode_without_value";

    /// <summary><c>--max</c> carried no value or a value that is not an integer.</summary>
    public const string InvalidMaxReason = "invalid_max";

    /// <summary>The recording could not be opened or parsed.</summary>
    public const string UnreadableFileReason = "recording_unreadable";

    /// <summary>No recording path followed the flag.</summary>
    public const string MissingPathReason = "missing_path";

    /// <summary>An argument starting with <c>--</c> that is not a known option.</summary>
    public const string UnknownOptionReason = "unknown_option";

    /// <summary><c>--fields</c> was given without <c>--opcode</c>, so no opcode was requested to census.</summary>
    public const string FieldsWithoutOpcodeReason = "fields_without_opcode";

    /// <summary>
    /// Decodes every line of a finite packet source and measures each opcode's
    /// shape. Pure: no console, no file I/O, no clock, nothing but the bytes
    /// the source hands over.
    /// </summary>
    public static IReadOnlyList<WireOpcodeCensus> Census(IPacketSource source, DataSourceKind sourceKind)
    {
        ArgumentNullException.ThrowIfNull(source);

        var byOpcode = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (string line in DecodeLines(source, sourceKind))
        {
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;
            if (!byOpcode.TryGetValue(tokens[0], out List<string[]>? packets))
            {
                packets = new List<string[]>();
                byOpcode[tokens[0]] = packets;
            }
            packets.Add(tokens);
        }

        var census = new List<WireOpcodeCensus>(byOpcode.Count);
        foreach (KeyValuePair<string, List<string[]>> entry in byOpcode)
        {
            var arities = new SortedSet<int>();
            int widest = 0;
            foreach (string[] tokens in entry.Value)
            {
                int arity = tokens.Length - 1;
                arities.Add(arity);
                if (arity > widest)
                    widest = arity;
            }

            var fields = new List<WireFieldShape>(widest);
            for (int position = 1; position <= widest; position++)
            {
                var distinct = new HashSet<string>(StringComparer.Ordinal);
                foreach (string[] tokens in entry.Value)
                    if (position < tokens.Length)
                        distinct.Add(tokens[position]);
                fields.Add(new WireFieldShape(
                    position,
                    distinct.Count == 1 ? distinct.First() : null,
                    distinct.Count));
            }

            census.Add(new WireOpcodeCensus(entry.Key, entry.Value.Count, arities.ToList(), fields));
        }

        return census
            .OrderByDescending(o => o.Count)
            .ThenBy(o => o.Opcode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The decoded lines whose first token is <paramref name="opcode"/>, up to
    /// <paramref name="maxLines"/> (<c>0</c> = every one). The filter is on the
    /// line text alone: no field is extracted and nothing is interpreted.
    /// </summary>
    public static IReadOnlyList<string> RawLines(IPacketSource source, DataSourceKind sourceKind, string opcode, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(opcode);

        var lines = new List<string>();
        foreach (string line in DecodeLines(source, sourceKind))
        {
            string first = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (!string.Equals(first, opcode, StringComparison.Ordinal))
                continue;
            if (maxLines > 0 && lines.Count >= maxLines)
                break;
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// The whole command as one pure function: decode the source and write the
    /// census (no <paramref name="opcode"/>), the raw lines (with it), or the
    /// per-field census (with it and <paramref name="showFields"/>) to
    /// <paramref name="output"/>.
    /// </summary>
    public static void Inspect(IPacketSource source, TextWriter output, DataSourceKind sourceKind, string? opcode, int maxLines, bool showFields = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);

        if (opcode is null)
        {
            WriteCensus(output, Census(source, sourceKind));
            return;
        }

        if (showFields)
        {
            WriteFieldCensus(output, source, sourceKind, opcode);
            return;
        }

        foreach (string line in RawLines(source, sourceKind, opcode, maxLines))
            output.WriteLine(line);
    }

    /// <summary>
    /// Validates the argument vector against the flag grammar. Returns the named
    /// refusal reason, or null when the vector is well-formed (with the parsed
    /// path, opcode and max line count in the out parameters).
    /// </summary>
    public static string? Validate(string[] args, out string? path, out string? opcode, out int maxLines)
        => Validate(args, out path, out opcode, out maxLines, out _);

    /// <summary>
    /// Validates the argument vector against the flag grammar. Returns the named
    /// refusal reason, or null when the vector is well-formed (with the parsed
    /// path, opcode, max line count and <c>--fields</c> presence in the out
    /// parameters).
    /// </summary>
    public static string? Validate(string[] args, out string? path, out string? opcode, out int maxLines, out bool showFields)
    {
        path = null;
        opcode = null;
        maxLines = DefaultMaxLines;
        showFields = false;

        int flagIndex = Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
            return MissingPathReason;

        for (int i = flagIndex + 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, OpcodeOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return OpcodeWithoutValueReason;
                opcode = args[++i];
                continue;
            }

            if (string.Equals(arg, MaxOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedMax))
                    return InvalidMaxReason;
                if (parsedMax < 0)
                    return NegativeMaxReason;
                maxLines = parsedMax;
                i++;
                continue;
            }

            if (string.Equals(arg, FieldsOption, StringComparison.OrdinalIgnoreCase))
            {
                showFields = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                return $"{UnknownOptionReason}:{arg}";

            path ??= arg;
        }

        if (string.IsNullOrWhiteSpace(path))
            return MissingPathReason;
        if (showFields && opcode is null)
            return FieldsWithoutOpcodeReason;
        if (!string.Equals(Path.GetExtension(path), ".noscap", StringComparison.OrdinalIgnoreCase))
            return BadExtensionReason;
        if (!File.Exists(path))
            return MissingFileReason;

        return null;
    }

    /// <summary>Console entry. Validates, opens the recording, and inspects it.</summary>
    public static int Run(string[] args)
    {
        string? refusal = Validate(args, out string? path, out string? opcode, out int maxLines, out bool showFields);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} <file.noscap> [--opcode <opcode>] [--max <n>] [--fields]");
            return ExitRefused;
        }

        try
        {
            using IPacketSource source = CaptureFile.Open(path!);
            Inspect(source, Console.Out, DataSourceKind.Cached, opcode, maxLines, showFields);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[REFUSED] {UnreadableFileReason}:{ex.GetType().Name}");
            return ExitRefused;
        }
    }

    /// <summary>
    /// The census in the shape the spec pins: one header line per opcode
    /// (<c>opcode n=… campi=…</c>, with every arity listed when it varies),
    /// then one detail line naming each field position's constant value or
    /// distinct count.
    /// </summary>
    public static void WriteCensus(TextWriter output, IReadOnlyList<WireOpcodeCensus> census)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(census);

        foreach (WireOpcodeCensus opcode in census)
        {
            string arity = opcode.Arities.Count == 1
                ? opcode.Arities[0].ToString(CultureInfo.InvariantCulture)
                : string.Join(",", opcode.Arities);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{opcode.Opcode,-9}n={opcode.Count,5}  campi={arity}"));

            if (opcode.Fields.Count == 0)
                continue;

            var parts = new List<string>(opcode.Fields.Count);
            foreach (WireFieldShape field in opcode.Fields)
            {
                parts.Add(field.ConstantValue is { } value
                    ? string.Create(CultureInfo.InvariantCulture, $"{field.Position}={value}")
                    : string.Create(CultureInfo.InvariantCulture, $"{field.Position}:{field.DistinctCount}var"));
            }

            output.WriteLine("         " + string.Join("  ", parts));
        }
    }

    /// <summary>
    /// The per-field census of one opcode (<c>--fields</c>): for each field
    /// index — numbered as the decoder does, <c>fields[0]</c> being the opcode —
    /// the distinct count, the min and max when every value is numeric, and up to
    /// <see cref="MaxFieldExamples"/> example values. A field with one distinct
    /// value is written <c>costante</c>, and packets of different lengths are
    /// censused separately because mixing them would invent variance that is not
    /// on the wire.
    /// </summary>
    public static void WriteFieldCensus(TextWriter output, IPacketSource source, DataSourceKind sourceKind, string opcode)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(opcode);

        var packets = new List<string[]>();
        foreach (string line in DecodeLines(source, sourceKind))
        {
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && string.Equals(tokens[0], opcode, StringComparison.Ordinal))
                packets.Add(tokens);
        }

        if (packets.Count == 0)
        {
            output.WriteLine($"{opcode}  n=0");
            return;
        }

        var groups = packets
            .GroupBy(p => p.Length)
            .Select(g => (Arity: g.Key - 1, Packets: g.ToArray()))
            .OrderBy(g => g.Arity)
            .ToArray();

        if (groups.Length == 1)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{opcode}  n={packets.Count}  campi={groups[0].Arity}  (fields[0]=opcode)"));
            WriteFieldRows(output, groups[0].Packets, groups[0].Arity);
            return;
        }

        string arities = string.Join(",", groups.Select(g => g.Arity.ToString(CultureInfo.InvariantCulture)));
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{opcode}  n={packets.Count}  campi={arities}  (lunghezze diverse: censiti per lunghezza, fields[0]=opcode)"));
        foreach ((int arity, string[][] groupPackets) in groups)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  -- campi={arity} (n={groupPackets.Length}) --"));
            WriteFieldRows(output, groupPackets, arity);
        }
    }

    /// <summary>
    /// One line per field index for a group of equal-length packets: the index,
    /// the distinct count (<c>costante</c> when it is one), the min and max when
    /// numeric, and up to <see cref="MaxFieldExamples"/> example values.
    /// </summary>
    private static void WriteFieldRows(TextWriter output, IReadOnlyList<string[]> packets, int arity)
    {
        for (int index = 1; index <= arity; index++)
        {
            var distinctValues = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string[] tokens in packets)
                if (seen.Add(tokens[index]))
                    distinctValues.Add(tokens[index]);

            if (distinctValues.Count == 1)
            {
                output.WriteLine($"  {index.ToString(CultureInfo.InvariantCulture)}  costante={distinctValues[0]}");
                continue;
            }

            var numeric = new List<long>();
            bool allNumeric = true;
            foreach (string value in distinctValues)
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                {
                    allNumeric = false;
                    break;
                }
                numeric.Add(parsed);
            }

            var parts = new List<string>(5)
            {
                index.ToString(CultureInfo.InvariantCulture),
                $"distinti={distinctValues.Count.ToString(CultureInfo.InvariantCulture)}",
            };
            if (allNumeric)
            {
                parts.Add($"min={numeric.Min().ToString(CultureInfo.InvariantCulture)}");
                parts.Add($"max={numeric.Max().ToString(CultureInfo.InvariantCulture)}");
            }
            int exampleCount = Math.Min(MaxFieldExamples, distinctValues.Count);
            parts.Add($"es={string.Join(",", distinctValues.Take(exampleCount))}");
            if (!allNumeric)
                parts.Add("(non numerico)");

            output.WriteLine("  " + string.Join("  ", parts));
        }
    }

    /// <summary>
    /// Runs the same chain <see cref="LiveWireMonitor"/> uses — engine, framer,
    /// decoder — and hands back the decoded lines, without the monitor's
    /// timestamp/direction prefix.
    /// </summary>
    private static IReadOnlyList<string> DecodeLines(IPacketSource source, DataSourceKind sourceKind)
    {
        var lines = new List<string>();
        var engine = new GameTrafficCaptureEngine(source, NosTaleWorldFramer.Factory(sourceKind));
        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;
            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
                lines.Add(line);
        };
        engine.Run();
        return lines;
    }
}
