using System.Buffers.Binary;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>The container layout an archive uses.</summary>
public enum NosArchiveFormat
{
    /// <summary>Not recognised. The archive is not read rather than guessed at.</summary>
    Unknown = 0,

    /// <summary>Named entries: <c>count</c>, then <c>(flag, nameLen, name, flag2, size, bytes)</c>.</summary>
    NamedEntries = 1,

    /// <summary>
    /// Numbered entries: <c>"NT Data NN"</c>, a header, then an index of
    /// <c>(id, offset)</c> pairs followed by the payloads.
    /// </summary>
    NumberedIndex = 2
}

/// <summary>One member of an archive, located but not yet decoded.</summary>
/// <remarks>
/// <para>
/// Either <see cref="Name"/> or <see cref="Id"/> identifies the entry, depending on
/// the container: the named format carries file names such as <c>conststring.dat</c>,
/// the numbered format carries the game's own identifiers. In
/// <c>NSmpData01.NOS</c> those ids start at 1024, which are map ids — the entry
/// number is data, not a position, so it is never rewritten to an index.
/// </para>
/// <para>
/// <see cref="Payload"/> is the raw bytes as stored. Whether they are readable is a
/// separate question from whether they were located, and conflating the two is how
/// an unreadable blob turns into a confident wrong answer.
/// </para>
/// </remarks>
public sealed record NosArchiveEntry(
    string? Name,
    int? Id,
    long Offset,
    int Length)
{
    /// <summary>How this entry is identified, for logs and diagnostics.</summary>
    public string Describe() => Name ?? (Id.HasValue ? $"#{Id.Value}" : "(non identificato)");
}

/// <summary>
/// The result of opening an archive: either its entries, or why it could not be read.
/// </summary>
public sealed record NosArchiveResult(
    string Path,
    NosArchiveFormat Format,
    DataSourceKind Source,
    IReadOnlyList<NosArchiveEntry> Entries,
    string? FailureReason,
    string? Magic = null,
    long FileLength = 0,
    long TrailerLength = 0)
{
    public bool Ok => FailureReason is null && Format != NosArchiveFormat.Unknown;

    public static NosArchiveResult Unreadable(string path, string reason, NosArchiveFormat format = NosArchiveFormat.Unknown) =>
        new(path, format, DataSourceKind.Unknown, Array.Empty<NosArchiveEntry>(), reason);
}

/// <summary>
/// Reads the NosTale client's own <c>.NOS</c> data archives.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these files.</b> The reference data this project needs — item, monster
/// and map identifiers and their statistics — cannot be invented without breaking
/// the rule that every exposed value must be certain. The client's own archives are
/// the authoritative source: they are what the client itself reads to draw the game.
/// </para>
/// <para>
/// <b>How the layouts were established.</b> Not from documentation, but from the
/// bytes, and then checked. The named format is confirmed by every entry's declared
/// size accounting exactly for the file. The numbered format is confirmed by the
/// index ending precisely where the first payload begins — on 146 archives of this
/// installation — with offsets ascending and inside the file. Anything failing those
/// checks is reported <see cref="DataSourceKind.Unknown"/> with a reason, never
/// returned as a partial success.
/// </para>
/// <para>
/// <b>Read-only, and safe while the game runs.</b> Files are opened for reading with
/// full sharing, so a running client is neither blocked nor disturbed. Nothing in
/// this class writes to the client's installation.
/// </para>
/// </remarks>
public static class NosArchive
{
    /// <summary>
    /// How many leading bytes must be printable for a file to be treated as magic-led.
    /// </summary>
    /// <remarks>
    /// The numbered format opens with a printable banner: this installation ships
    /// <c>"NT Data 02".."NT Data 26"</c> and also <c>"32GBS V1.0"</c>, which carry the
    /// same index. Recognising the layout by its structure rather than by one literal
    /// string is what lets the reader accept both without a list of banners that
    /// would be wrong the first time the game shipped another one.
    /// </remarks>
    private const int MagicProbeLength = 4;

    /// <summary>Header size of the numbered format, before the index begins.</summary>
    private const int NumberedHeaderSize = 21;

    /// <summary>One index record of the numbered format: id and offset.</summary>
    private const int NumberedIndexEntrySize = 8;

    /// <summary>A cap that keeps a corrupt length from allocating wildly.</summary>
    private const int MaxEntryCount = 1_000_000;

    /// <summary>Longest entry name accepted before the file is called corrupt.</summary>
    private const int MaxNameLength = 512;

    /// <summary>
    /// Reads an archive's directory without decoding any payload.
    /// </summary>
    /// <remarks>
    /// The whole file is read into memory: the largest archive in a stock
    /// installation is under 100 MB, and a single read avoids a torn view of a file
    /// the client may be touching at the same time.
    /// </remarks>
    public static NosArchiveResult Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] data;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            data = new byte[stream.Length];
            stream.ReadExactly(data);
        }
        catch (FileNotFoundException)
        {
            return NosArchiveResult.Unreadable(path, "file_not_found");
        }
        catch (DirectoryNotFoundException)
        {
            return NosArchiveResult.Unreadable(path, "directory_not_found");
        }
        catch (IOException ex)
        {
            return NosArchiveResult.Unreadable(path, $"io_error:{ex.GetType().Name}");
        }
        catch (UnauthorizedAccessException)
        {
            return NosArchiveResult.Unreadable(path, "access_denied");
        }

        return Parse(path, data);
    }

    /// <summary>Reads an archive already held in memory. Exposed for tests.</summary>
    public static NosArchiveResult Parse(string path, ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return NosArchiveResult.Unreadable(path, $"file_too_short:{data.Length}");

        return data.Length >= NumberedHeaderSize && LooksMagicLed(data)
            ? ParseNumbered(path, data)
            : ParseNamed(path, data);
    }

    /// <summary>
    /// True when the file opens with a printable banner rather than a count.
    /// </summary>
    /// <remarks>
    /// The named format begins with a little-endian entry count, whose upper bytes
    /// are zero for any plausible count; a banner is printable throughout. The two
    /// cannot be confused.
    /// </remarks>
    private static bool LooksMagicLed(ReadOnlySpan<byte> data)
    {
        for (int i = 0; i < MagicProbeLength; i++)
        {
            if (data[i] < 0x20 || data[i] > 0x7E)
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------ named format

    /// <summary>
    /// <c>count</c>, then for each entry <c>flag, nameLen, name, flag2, size, bytes</c>.
    /// </summary>
    private static NosArchiveResult ParseNamed(string path, ReadOnlySpan<byte> data)
    {
        int offset = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += 4;

        if (count == 0 || count > MaxEntryCount)
            return NosArchiveResult.Unreadable(path, $"entry_count_implausible:{count}");

        var entries = new List<NosArchiveEntry>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (offset + 8 > data.Length)
                return Truncated(path, i, count, NosArchiveFormat.NamedEntries);

            offset += 4; // flag, meaning not established; not invented either
            uint nameLength = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            if (nameLength == 0 || nameLength > MaxNameLength || offset + nameLength > data.Length)
                return NosArchiveResult.Unreadable(path,
                    $"name_length_implausible:{nameLength}@entry_{i}", NosArchiveFormat.NamedEntries);

            string name = Encoding.Latin1.GetString(data.Slice(offset, (int)nameLength)).TrimEnd('\0');
            offset += (int)nameLength;

            if (offset + 8 > data.Length)
                return Truncated(path, i, count, NosArchiveFormat.NamedEntries);

            offset += 4; // second flag
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            offset += 4;

            if (offset + size > data.Length)
                return NosArchiveResult.Unreadable(path,
                    $"entry_exceeds_file:{name}:{offset}+{size}>{data.Length}", NosArchiveFormat.NamedEntries);

            entries.Add(new NosArchiveEntry(name, null, offset, (int)size));
            offset += (int)size;
        }

        // Every named archive in a stock installation ends with 12 bytes that are
        // not part of any entry: four that differ per file and eight shared across
        // them, so a checksum and a stamp rather than content. They are reported
        // rather than absorbed into the last entry, which would hand a decoder
        // twelve bytes of something else and let it fail far from here.
        return new NosArchiveResult(
            path,
            NosArchiveFormat.NamedEntries,
            DataSourceKind.Live,
            entries,
            null,
            Magic: null,
            FileLength: data.Length,
            TrailerLength: data.Length - offset);
    }

    // --------------------------------------------------------- numbered format

    /// <summary>
    /// <c>"NT Data NN"</c>, header, an index of <c>(id, offset)</c>, then the payloads.
    /// </summary>
    /// <remarks>
    /// The last entry's length is the distance to the end of the file, because the
    /// index records where each payload starts and nothing records where it ends.
    /// </remarks>
    private static NosArchiveResult ParseNumbered(string path, ReadOnlySpan<byte> data)
    {
        // The banner is kept on every outcome, including refusal: knowing that a
        // file said "CCINF V1.20" is the difference between "we cannot read this
        // one yet" and "this file is broken".
        string magic = Encoding.ASCII.GetString(data[..12]).TrimEnd('\u001a', ' ', '\0');
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

        if (count == 0 || count > MaxEntryCount)
            return Refuse(path, $"entry_count_implausible:{count}", magic, data.Length);

        long indexEnd = NumberedHeaderSize + (long)count * NumberedIndexEntrySize;
        if (indexEnd > data.Length)
            return Refuse(path, $"index_exceeds_file:{indexEnd}>{data.Length}", magic, data.Length);

        var ids = new int[count];
        var offsets = new long[count];
        for (uint i = 0; i < count; i++)
        {
            int at = NumberedHeaderSize + (int)i * NumberedIndexEntrySize;
            ids[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[at..]);
            offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
        }

        // The structural invariants that establish this layout. Failing any of them
        // means the bytes are not what this reader believes, so nothing is returned.
        if (offsets[0] != indexEnd)
            return Refuse(path, $"first_payload_not_after_index:{offsets[0]}!={indexEnd}", magic, data.Length);

        for (uint i = 1; i < count; i++)
        {
            if (offsets[i] < offsets[i - 1])
                return Refuse(path, $"offsets_not_ascending@entry_{i}", magic, data.Length);
        }

        if (offsets[count - 1] > data.Length)
            return Refuse(path, $"last_offset_beyond_file:{offsets[count - 1]}>{data.Length}", magic, data.Length);

        var entries = new List<NosArchiveEntry>((int)count);
        for (uint i = 0; i < count; i++)
        {
            long end = i + 1 < count ? offsets[i + 1] : data.Length;
            entries.Add(new NosArchiveEntry(null, ids[i], offsets[i], (int)(end - offsets[i])));
        }

        return new NosArchiveResult(
            path,
            NosArchiveFormat.NumberedIndex,
            DataSourceKind.Live,
            entries,
            null,
            Magic: magic,
            FileLength: data.Length,
            TrailerLength: 0);
    }

    /// <summary>A refusal that still reports the banner and the size it saw.</summary>
    private static NosArchiveResult Refuse(string path, string reason, string magic, int length) =>
        new(path, NosArchiveFormat.NumberedIndex, DataSourceKind.Unknown,
            Array.Empty<NosArchiveEntry>(), reason, magic, length);

    private static NosArchiveResult Truncated(string path, uint at, uint count, NosArchiveFormat format) =>
        NosArchiveResult.Unreadable(path, $"truncated_at_entry_{at}_of_{count}", format);

    /// <summary>
    /// Reads one entry's stored bytes.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Open"/> so a caller can walk a large archive's
    /// directory without pulling tens of megabytes of payload into memory.
    /// </remarks>
    public static MemoryReadOutcome ReadEntry(string path, NosArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Length < 0)
            return MemoryReadOutcome.Failed($"negative_length:{entry.Length}");

        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (entry.Offset + entry.Length > stream.Length)
                return MemoryReadOutcome.Failed(
                    $"entry_beyond_file:{entry.Offset}+{entry.Length}>{stream.Length}");

            stream.Seek(entry.Offset, SeekOrigin.Begin);
            var buffer = new byte[entry.Length];
            stream.ReadExactly(buffer);
            return MemoryReadOutcome.Read(buffer);
        }
        catch (EndOfStreamException)
        {
            // A short read is a failure, not a short success: half an entry is not
            // an entry, and returning it would hand the caller invented structure.
            return MemoryReadOutcome.Failed("unexpected_end_of_file");
        }
        catch (IOException ex)
        {
            return MemoryReadOutcome.Failed($"io_error:{ex.GetType().Name}");
        }
        catch (UnauthorizedAccessException)
        {
            return MemoryReadOutcome.Failed("access_denied");
        }
    }
}

/// <summary>Bytes read from an archive, or why they could not be.</summary>
public sealed record MemoryReadOutcome(byte[] Bytes, string? FailureReason)
{
    public bool Ok => FailureReason is null;

    public static MemoryReadOutcome Read(byte[] bytes) => new(bytes, null);

    public static MemoryReadOutcome Failed(string reason) => new(Array.Empty<byte>(), reason);
}
