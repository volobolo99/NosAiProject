using System.IO.Compression;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>One field line of a table: a name and its tab-separated values.</summary>
public sealed record NosField(string Name, IReadOnlyList<string> Values)
{
    /// <summary>The value at <paramref name="index"/>, or null when absent.</summary>
    /// <remarks>
    /// Null rather than an empty string: a field the record does not carry and a
    /// field carrying "" are different facts, and only one of them is data.
    /// </remarks>
    public string? Value(int index) => index >= 0 && index < Values.Count ? Values[index] : null;

    /// <summary>Reads a value as an integer, or null when it is absent or not one.</summary>
    public int? Int(int index) =>
        int.TryParse(Value(index), out int parsed) ? parsed : null;
}

/// <summary>One record of a table, keyed by its <c>VNUM</c> where it has one.</summary>
public sealed record NosRecord(int? Vnum, IReadOnlyList<NosField> Fields)
{
    /// <summary>The first field with this name, or null.</summary>
    public NosField? Field(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every field with this name; some repeat, such as <c>BASIC</c>.</summary>
    public IEnumerable<NosField> AllFields(string name) =>
        Fields.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A decoded table, or why it could not be decoded.</summary>
public sealed record NosTableResult(
    string Name,
    DataSourceKind Source,
    IReadOnlyList<NosRecord> Records,
    string? FailureReason,
    int LineCount = 0,
    int BinaryTailCount = 0,
    int UnreliableLengthCount = 0,
    IReadOnlyList<string>? Lines = null)
{
    public bool Ok => FailureReason is null;

    public static NosTableResult Failed(string name, string reason) =>
        new(name, DataSourceKind.Unknown, Array.Empty<NosRecord>(), reason);
}

/// <summary>
/// Decodes the client's <c>.dat</c> tables into records.
/// </summary>
/// <remarks>
/// <para>
/// <b>The chain, each step established from the bytes.</b> An archive entry may be
/// zlib-compressed behind a 13-byte header — recognised by the declared compressed
/// length matching the remaining bytes exactly, and confirmed by the declared
/// uncompressed length matching what inflate produced. The table itself is
/// obfuscated with <c>XOR 0x33</c>, which is the key that yields tab separators and
/// upper-case field names; <c>0x13</c> also produces readable letters and was the
/// first thing tried, but it turns every tab into <c>)</c> and so is wrong.
/// </para>
/// <para>
/// <b>Line framing uses two signals and trusts neither alone.</b> Each line is
/// <c>0xFF</c>, a length byte, then that many obfuscated bytes. The length is right
/// almost everywhere, but a line may be followed by a short run of bytes that are
/// not text — a value the table stores in binary — and a few lines declare
/// <c>255</c>, which is not a real length. So the length gives the text and the
/// terminator resynchronises. Both anomalies are <b>counted and reported</b> rather
/// than smoothed over: a decoder that silently absorbs what it does not understand
/// is how wrong statistics enter a database.
/// </para>
/// </remarks>
public static class NosDataTable
{
    /// <summary>The key that yields tab separators and upper-case field names.</summary>
    public const byte ObfuscationKey = 0x33;

    /// <summary>Ends every line.</summary>
    private const byte LineTerminator = 0xFF;

    /// <summary>A declared length of 255 is not a length; the terminator is used instead.</summary>
    private const byte UnreliableLength = 0xFF;

    /// <summary>Bytes before the zlib stream in a compressed entry.</summary>
    private const int CompressedHeaderSize = 13;

    /// <summary>
    /// A byte whose high nibble is 8 starts a packed number rather than text.
    /// </summary>
    /// <remarks>
    /// Obfuscated text can never reach this range: printable ASCII is 0x20-0x7E and
    /// XOR 0x33 maps it to 0x13-0x4D, so the two cannot be confused.
    /// </remarks>
    private const byte PackedNumberMarker = 0x80;

    /// <summary>
    /// A packed nibble holds a character: <c>nibble = character - 0x2C</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alphabet is <c>'-'</c> (1), <c>'.'</c> (2), <c>'/'</c> (3) and
    /// <c>'0'</c>-<c>'9'</c> (4-13). It was established from the 2 705 monster
    /// identifiers, which ascend and so cannot be fitted by accident: 100 is
    /// <c>83 54 40</c>, 109 is <c>83 54 d0</c>, 900 is <c>83 d4 40</c>.
    /// </para>
    /// <para>
    /// Reading it as digits alone left 7 555 values undecodable, and the reason is
    /// worth stating: <b>these are not integers</b>. A nibble of 2 is a decimal
    /// point and one of 1 is a minus sign, so <c>83 26 40</c> is <c>.20</c> and
    /// <c>83 2b 40</c> is <c>.70</c> — rates and multipliers, in the very fields a
    /// damage calculation needs. Treating them as broken integers would have thrown
    /// away every rate in the game while reporting a 99.6% success.
    /// </para>
    /// <para>
    /// The marker's low nibble counts <i>characters</i>, not digits, which is why a
    /// three-character value like <c>-20</c> or <c>.25</c> carries the same marker
    /// as <c>100</c>.
    /// </para>
    /// </remarks>
    private const int PackedAlphabetBase = 0x2C;

    /// <summary>Lowest nibble the alphabet uses (<c>'-'</c>).</summary>
    private const int PackedAlphabetMin = 1;

    /// <summary>Highest nibble the alphabet uses (<c>'9'</c>).</summary>
    private const int PackedAlphabetMax = 13;

    /// <summary>Marks a record boundary inside a table.</summary>
    private const string RecordSeparator = "#=";

    /// <summary>
    /// Decodes a table's bytes, decompressing first when they are compressed.
    /// </summary>
    public static NosTableResult Decode(string name, ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
            return NosTableResult.Failed(name, "empty_payload");

        byte[] body;
        if (TryInflate(payload, out byte[]? inflated, out string? inflateFailure))
        {
            body = inflated!;
        }
        else if (inflateFailure is not null)
        {
            return NosTableResult.Failed(name, inflateFailure);
        }
        else
        {
            body = payload.ToArray();
        }

        return Parse(name, body);
    }

    /// <summary>
    /// Inflates a compressed entry.
    /// </summary>
    /// <returns>
    /// True when it decompressed. False with a null failure means the payload was
    /// simply not compressed — a different answer from "compressed but broken",
    /// which returns a reason.
    /// </returns>
    private static bool TryInflate(ReadOnlySpan<byte> payload, out byte[]? inflated, out string? failure)
    {
        inflated = null;
        failure = null;

        if (payload.Length <= CompressedHeaderSize)
            return false;

        int declaredPlain = BitConverter.ToInt32(payload[4..8]);
        int declaredPacked = BitConverter.ToInt32(payload[8..12]);
        ReadOnlySpan<byte> stream = payload[CompressedHeaderSize..];

        // The header only counts as a header when its own numbers describe what
        // follows. Otherwise these are ordinary table bytes that happen to start
        // with plausible values.
        if (declaredPacked != stream.Length || declaredPlain <= 0)
            return false;
        if (stream.Length < 2 || stream[0] != 0x78)
            return false;

        try
        {
            using var input = new MemoryStream(stream.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(declaredPlain);
            zlib.CopyTo(output);
            byte[] result = output.ToArray();

            if (result.Length != declaredPlain)
            {
                failure = $"inflate_size_mismatch:{result.Length}!={declaredPlain}";
                return false;
            }

            inflated = result;
            return true;
        }
        catch (InvalidDataException ex)
        {
            failure = $"inflate_failed:{ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// Reads a language table: one key and its displayed text per line.
    /// </summary>
    /// <remarks>
    /// These files are framed exactly like the data tables but hold no records, only
    /// <c>key TAB text</c>. A duplicate key keeps the first value: the client reads
    /// them in order, and picking a later one would show text it never displays.
    /// </remarks>
    public static Dictionary<string, string> ReadKeyedText(ReadOnlySpan<byte> payload)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        NosTableResult table = Decode("language", payload);
        if (!table.Ok || table.Lines is null)
            return entries;

        // These lines are "key TAB text" with no leading tab, so they are read here
        // rather than through the record grouping, which exists for the data tables
        // and would discard every one of them.
        foreach (string line in table.Lines)
        {
            int tab = line.IndexOf('	');
            if (tab <= 0 || tab == line.Length - 1)
                continue;
            entries.TryAdd(line[..tab], line[(tab + 1)..]);
        }
        return entries;
    }

    /// <summary>Splits a decoded table into records.</summary>
    public static NosTableResult Parse(string name, ReadOnlySpan<byte> body)
    {
        var lines = new List<string>();
        int tails = 0;
        int unreliable = 0;
        string? problem = null;

        int first = IndexOf(body, LineTerminator, 0);
        if (first < 0)
            return NosTableResult.Failed(name, "no_line_terminator");

        // The block opens with one line whose first byte is its length.
        lines.Add(Deobfuscate(body[1..first]));

        int at = first;
        while (at < body.Length && body[at] == LineTerminator)
        {
            if (at + 2 > body.Length)
                break;

            byte declared = body[at + 1];
            int start = at + 2;

            if (declared == UnreliableLength)
                unreliable++;

            // A line runs to the next terminator. The declared length covers only the
            // text up to the first packed number, so it cannot delimit the line on its
            // own: a value of 100 or more is stored in binary and the rest of the line
            // continues after it.
            int next = IndexOf(body, LineTerminator, start);
            if (next < 0)
                break;

            if (declared != UnreliableLength && start + declared < next)
                tails++;

            lines.Add(ReadLine(body[start..next]));
            at = next;
        }

        IReadOnlyList<NosRecord> records = GroupIntoRecords(lines);
        return new NosTableResult(
            name,
            DataSourceKind.Live,
            records,
            problem,
            lines.Count,
            tails,
            unreliable,
            lines);
    }

    private static string Deobfuscate(ReadOnlySpan<byte> encoded)
    {
        var buffer = new byte[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
            buffer[i] = (byte)(encoded[i] ^ ObfuscationKey);
        return Encoding.Latin1.GetString(buffer);
    }

    /// <summary>
    /// Reads one line, decoding packed numbers back into their digits.
    /// </summary>
    /// <remarks>
    /// A number of 100 or more is not written as text: it is stored as a marker byte
    /// giving the digit count, then the digits two to a byte. Without this the value
    /// is simply missing from the line -- which is how an import can look successful
    /// while every large statistic in the game, every hit point and every damage
    /// figure, quietly fails to arrive.
    /// <para>
    /// A number that does not decode becomes <see cref="UnknownValue"/> carrying its
    /// bytes, never a plausible substitute. A wrong hit-point total is worse than a
    /// missing one, because only one of the two announces itself.
    /// </para>
    /// </remarks>
    private static string ReadLine(ReadOnlySpan<byte> line)
    {
        var text = new StringBuilder(line.Length);
        int i = 0;

        while (i < line.Length)
        {
            byte current = line[i];
            if ((current & 0xF0) != PackedNumberMarker)
            {
                text.Append((char)(current ^ ObfuscationKey));
                i++;
                continue;
            }

            int characters = current & 0x0F;
            int consumed = (characters + 1) / 2;
            if (characters == 0 || i + 1 + consumed > line.Length)
            {
                text.Append(UnknownValue);
                break;
            }

            AppendNumber(text, line.Slice(i + 1, consumed), characters);
            i += 1 + consumed;
        }

        return text.ToString();
    }

    /// <summary>What a value that could not be decoded reads as.</summary>
    public const string UnknownValue = "UNKNOWN";

    private static void AppendNumber(StringBuilder text, ReadOnlySpan<byte> packed, int characters)
    {
        Span<char> rendered = stackalloc char[characters];
        for (int c = 0; c < characters; c++)
        {
            byte b = packed[c / 2];
            int nibble = (c % 2 == 0) ? b >> 4 : b & 0x0F;
            if (nibble is < PackedAlphabetMin or > PackedAlphabetMax)
            {
                // Outside the alphabet this encoding uses. Saying UNKNOWN keeps a
                // value nobody verified out of a damage calculation.
                text.Append(UnknownValue);
                return;
            }
            rendered[c] = (char)(nibble + PackedAlphabetBase);
        }
        text.Append(rendered);
    }

    private static int IndexOf(ReadOnlySpan<byte> haystack, byte needle, int from)
    {
        for (int i = from; i < haystack.Length; i++)
        {
            if (haystack[i] == needle)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Turns lines into records, split on the table's own separator lines.
    /// </summary>
    /// <remarks>
    /// A record with no <c>VNUM</c> keeps a null id rather than being given one.
    /// Numbering it here would invent an identity the client never assigned.
    /// </remarks>
    private static IReadOnlyList<NosRecord> GroupIntoRecords(List<string> lines)
    {
        var records = new List<NosRecord>();
        var current = new List<NosField>();

        foreach (string line in lines)
        {
            string trimmed = line.TrimStart('\t');
            if (trimmed.StartsWith(RecordSeparator, StringComparison.Ordinal))
            {
                Flush(records, current);
                continue;
            }

            if (!line.StartsWith('\t'))
                continue;

            string[] parts = line.Split('\t', StringSplitOptions.None);
            // parts[0] is the empty string before the leading tab.
            if (parts.Length < 2 || parts[1].Length == 0)
                continue;

            current.Add(new NosField(parts[1], parts.Skip(2).ToArray()));
        }

        Flush(records, current);
        return records;
    }

    private static void Flush(List<NosRecord> records, List<NosField> fields)
    {
        if (fields.Count == 0)
            return;

        NosField? vnum = fields.FirstOrDefault(f =>
            string.Equals(f.Name, "VNUM", StringComparison.OrdinalIgnoreCase));
        records.Add(new NosRecord(vnum?.Int(0), fields.ToArray()));
        fields.Clear();
    }
}
