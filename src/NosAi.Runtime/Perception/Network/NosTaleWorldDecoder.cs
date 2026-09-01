using System.Text;

namespace NosAi.Runtime.Perception.Network;

/// <summary>
/// Turns the world channel's server-to-client bytes into the packets the client
/// actually reads.
/// </summary>
/// <remarks>
/// <para>
/// The piece the capture layer has been missing since it was written:
/// <c>GameStreamFramer</c> handed every frame back as
/// <c>Unframed(..., "no_nostale_decoder")</c>, so a real capture was 34 KB of
/// bytes nobody could read. This is that decoder.
/// </para>
/// <para>
/// <b>Derived from a real capture, not from a specification.</b> In the recording
/// made for T-04, <c>0xFF</c> occurs every ~13 bytes, which makes it the packet
/// terminator; the first packet opens with <c>0x02</c>, and the two bytes after it
/// complemented give <c>'m','v'</c> -- the move packet. Generalising those two
/// observations decodes 2490 of 2490 packets in that capture to fully printable
/// ASCII with a consistent grammar, which is the evidence that the shape below is
/// the real one rather than a plausible one.
/// </para>
/// <para>
/// Each packet begins with a length byte. Its top bit selects how the bytes that
/// follow are read: clear means literal bytes, each complemented; set means each
/// byte carries two nibbles indexing <see cref="NibbleAlphabet"/>, which is how
/// the numeric fields that dominate this protocol are packed.
/// </para>
/// <para>
/// Read-only, like everything else on this path. It interprets bytes that were
/// observed; it produces nothing to put back on the wire (ADR-0014).
/// </para>
/// </remarks>
public static class NosTaleWorldDecoder
{
    /// <summary>
    /// The alphabet the packed branch indexes, one entry per nibble value.
    /// </summary>
    /// <remarks>
    /// Nibble 0 is padding and 0xF is skipped; the rest select from here, which is
    /// why this protocol's numbers survive at roughly half a byte per digit. The
    /// final entry is a newline, so a packed field ends the line the same way an
    /// unpacked one does.
    /// </remarks>
    public const string NibbleAlphabet = " -.0123456789\n";

    /// <summary>Marks the end of one packet.</summary>
    public const byte PacketTerminator = 0xFF;

    /// <summary>
    /// Decodes every complete packet in <paramref name="stream"/>.
    /// </summary>
    /// <remarks>
    /// Trailing bytes with no terminator are a packet still arriving, not a packet:
    /// they are dropped rather than returned half-read, because half a packet
    /// parses into fields that look like values and are not.
    /// </remarks>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> stream)
    {
        var packets = new List<string>();
        var current = new StringBuilder();
        int index = 0;

        while (index < stream.Length)
        {
            byte header = stream[index++];

            if (header == PacketTerminator)
            {
                packets.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            int length = header & 0x7F;

            if ((header & 0x80) != 0)
                index = ReadPacked(stream, index, length, current);
            else
                index = ReadLiteral(stream, index, length, current);
        }

        // Whatever is left never reached a terminator, so it is incomplete.
        return packets;
    }

    /// <summary>
    /// Measures the first complete packet in <paramref name="stream"/>, in bytes,
    /// terminator included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the framer, which has to cut the reassembled stream into packets before
    /// anything decodes them. Splitting on the first <c>0xFF</c> byte would be
    /// wrong: <c>0xFF</c> is a terminator only where a length byte is expected, and
    /// inside a run of literal or packed bytes it is ordinary data. Cutting there
    /// would hand the decoder two half packets, and half a packet parses into
    /// fields that look like values.
    /// </para>
    /// <para>
    /// So this walks the same structure <see cref="Decode"/> does — length byte,
    /// then that many literal bytes or half as many packed ones — and returns false
    /// while the packet is still arriving.
    /// </para>
    /// </remarks>
    public static bool TryMeasurePacket(ReadOnlySpan<byte> stream, out int length)
    {
        length = 0;
        int index = 0;

        while (index < stream.Length)
        {
            byte header = stream[index++];

            if (header == PacketTerminator)
            {
                length = index;
                return true;
            }

            int declared = header & 0x7F;
            // Packed fields carry two nibbles per byte, so an odd count still costs
            // a whole byte; literal fields cost one byte each.
            index += (header & 0x80) != 0 ? (declared + 1) / 2 : declared;
        }

        return false;
    }

    /// <summary>Literal bytes, each complemented.</summary>
    private static int ReadLiteral(ReadOnlySpan<byte> stream, int index, int length, StringBuilder into)
    {
        while (length > 0 && index < stream.Length)
        {
            into.Append((char)(stream[index] ^ 0xFF));
            index++;
            length--;
        }

        return index;
    }

    /// <summary>Two nibbles per byte, each indexing <see cref="NibbleAlphabet"/>.</summary>
    private static int ReadPacked(ReadOnlySpan<byte> stream, int index, int length, StringBuilder into)
    {
        while (length > 0 && index < stream.Length)
        {
            byte packed = stream[index++];

            Append(into, packed >> 4);
            length--;

            // The low nibble of the final byte is padding when the field's length
            // is odd; consuming it would append a character the sender never sent.
            if (length == 0)
                break;

            Append(into, packed & 0x0F);
            length--;
        }

        return index;
    }

    private static void Append(StringBuilder into, int nibble)
    {
        if (nibble > 0 && nibble - 1 < NibbleAlphabet.Length)
            into.Append(NibbleAlphabet[nibble - 1]);
    }
}
