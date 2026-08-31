using System.Buffers.Binary;
using System.Net;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Writes captured packets to a file, and reads them back as a packet source.
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge that decouples decoding from the driver. Capturing a real
/// session needs WinDivert once; writing it here means the NosTale decoder can
/// then be written and re-run offline against the recording, as many times as it
/// takes, with no driver and no live game.
/// </para>
/// <para>
/// The format is deliberately plain and versioned: a magic, the server endpoint
/// the capture was filtered on, then length-prefixed packets each with a
/// timestamp. Raw packets, not parsed frames — the recording is of the wire, and
/// re-parsing it on replay means a fix to the parser applies to old captures too.
/// </para>
/// <para>
/// <b>What a recording contains.</b> Real game traffic: whatever the client and
/// server exchanged. It is the operator's own session on the operator's machine,
/// and it is written unencrypted to the path given. Treat it as the sensitive
/// capture it is — it is not committed, and `data/` and `tools/` are gitignored.
/// </para>
/// </remarks>
public static class CaptureFile
{
    /// <summary>"NOSCAP" + format version. A reader refuses anything else.</summary>
    private static ReadOnlySpan<byte> Magic => "NOSCAP01"u8;

    private const int MagicLength = 8;

    /// <summary>
    /// Records packets from a source to a file until the source ends or the token trips.
    /// </summary>
    /// <returns>How many packets were written.</returns>
    public static long Record(IPacketSource source, string path, CancellationToken cancellationToken = default, TimeSpan? readTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var timeout = readTimeout ?? TimeSpan.FromMilliseconds(500);
        long written = 0;

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeader(stream, source.ServerAddress, source.ServerPort);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!source.TryRead(timeout, out var packet))
            {
                if (source is IFinitePacketSource finite && finite.Ended)
                    break;
                continue;
            }
            WritePacket(stream, packet);
            written++;
        }

        stream.Flush();
        return written;
    }

    /// <summary>Opens a recording as a replay source.</summary>
    public static CaptureFileSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new CaptureFileSource(path);
    }

    private static void WriteHeader(Stream stream, IPAddress serverAddress, int serverPort)
    {
        stream.Write(Magic);

        // The address as 16 bytes so v4 and v6 share one layout: v4 is written
        // left-aligned and its length recorded, so a reader rebuilds the right kind.
        byte[] addressBytes = serverAddress.GetAddressBytes();
        Span<byte> address = stackalloc byte[16];
        addressBytes.CopyTo(address);

        stream.WriteByte((byte)addressBytes.Length);
        stream.Write(address);

        Span<byte> port = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(port, (ushort)serverPort);
        stream.Write(port);
    }

    private static void WritePacket(Stream stream, CapturedPacket packet)
    {
        Span<byte> prefix = stackalloc byte[12]; // 8 ticks + 4 length
        BinaryPrimitives.WriteInt64LittleEndian(prefix, packet.TimestampUtc.ToUniversalTime().Ticks);
        BinaryPrimitives.WriteInt32LittleEndian(prefix[8..], packet.Raw.Length);
        stream.Write(prefix);
        stream.Write(packet.Raw.Span);
    }

    /// <summary>Parsed file header.</summary>
    internal static (IPAddress Address, int Port) ReadHeader(Stream stream)
    {
        Span<byte> magic = stackalloc byte[MagicLength];
        if (stream.Read(magic) != MagicLength || !magic.SequenceEqual(Magic))
            throw new InvalidDataException("not_a_nosai_capture_or_wrong_version");

        int addressLength = stream.ReadByte();
        if (addressLength is not (4 or 16))
            throw new InvalidDataException($"invalid_address_length:{addressLength}");

        Span<byte> address = stackalloc byte[16];
        if (stream.Read(address) != 16)
            throw new InvalidDataException("truncated_header_address");

        Span<byte> port = stackalloc byte[2];
        if (stream.Read(port) != 2)
            throw new InvalidDataException("truncated_header_port");

        return (new IPAddress(address[..addressLength].ToArray()), BinaryPrimitives.ReadUInt16BigEndian(port));
    }

    internal static bool TryReadPacket(Stream stream, out CapturedPacket packet)
    {
        packet = default;

        Span<byte> prefix = stackalloc byte[12];
        int read = ReadFully(stream, prefix);
        if (read == 0)
            return false; // clean EOF
        if (read != prefix.Length)
            throw new InvalidDataException("truncated_packet_prefix");

        long ticks = BinaryPrimitives.ReadInt64LittleEndian(prefix);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix[8..]);
        if (length < 0 || length > 65535)
            throw new InvalidDataException($"invalid_packet_length:{length}");

        var raw = new byte[length];
        if (ReadFully(stream, raw) != length)
            throw new InvalidDataException("truncated_packet_body");

        packet = new CapturedPacket(new DateTime(ticks, DateTimeKind.Utc), raw);
        return true;
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }
}

/// <summary>Replays a recorded capture as a packet source.</summary>
/// <remarks>
/// Finite by nature: it ends at the last recorded packet, which is what lets the
/// engine's run loop stop on its own during offline decoding.
/// </remarks>
public sealed class CaptureFileSource : IPacketSource, IFinitePacketSource
{
    private readonly FileStream _stream;

    internal CaptureFileSource(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (address, port) = CaptureFile.ReadHeader(_stream);
        ServerAddress = address;
        ServerPort = port;
    }

    public IPAddress ServerAddress { get; }
    public int ServerPort { get; }
    public bool Ended { get; private set; }

    public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
    {
        if (Ended)
        {
            packet = default;
            return false;
        }

        if (CaptureFile.TryReadPacket(_stream, out packet))
            return true;

        Ended = true;
        return false;
    }

    public void Dispose() => _stream.Dispose();
}
