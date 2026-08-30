using System.Buffers.Binary;
using System.Text;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// LAN discovery for the Gate 1 runtime, so the phone never has to be told an
/// address.
/// </summary>
/// <remarks>
/// <para>
/// The phone broadcasts a request on <see cref="Port"/>; a runtime on the same
/// network answers with the port its Guard channel is listening on. Separate from
/// the NOSA channel on purpose: this is unauthenticated UDP that only says "a
/// runtime is here", and keeping it in its own format makes that impossible to
/// confuse with the authenticated session.
/// </para>
/// <para>
/// <b>Discovery is not a trust decision.</b> Anything on the LAN can answer, so a
/// reply proves only that something claimed to be a runtime. The phone still has
/// to complete the RSA handshake, and the runtime still refuses any key it does
/// not trust. See docs/adr/ADR-0007 for what this does and does not protect.
/// </para>
/// </remarks>
public static class DiscoveryProtocol
{
    /// <summary>UDP port for discovery. Deliberately not the Guard channel's port.</summary>
    public const int Port = 17472;

    public const byte Version = 1;

    /// <summary>"NOSD" — NosAi discovery, distinct from the "NOSA" session magic.</summary>
    public static ReadOnlySpan<byte> Magic => "NOSD"u8;

    private const byte RequestType = 0x01;
    private const byte ResponseType = 0x02;

    private const int MinimumFrame = 6;

    /// <summary>Maximum bytes of host name carried in a reply.</summary>
    public const int MaxHostNameBytes = 64;

    public static byte[] CreateRequest()
    {
        var frame = new byte[MinimumFrame];
        Magic.CopyTo(frame);
        frame[4] = Version;
        frame[5] = RequestType;
        return frame;
    }

    public static bool IsRequest(ReadOnlySpan<byte> frame)
        => frame.Length >= MinimumFrame
           && frame[..4].SequenceEqual(Magic)
           && frame[4] == Version
           && frame[5] == RequestType;

    /// <param name="hostName">
    /// Shown to the operator so two runtimes on one network can be told apart. It
    /// is a label, never an authorisation: nothing is decided from it.
    /// </param>
    public static byte[] CreateResponse(int guardPort, string hostName)
    {
        if (guardPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(guardPort), guardPort, "Guard port must be between 1 and 65535.");

        var nameBytes = Encoding.UTF8.GetBytes(hostName ?? string.Empty);
        if (nameBytes.Length > MaxHostNameBytes)
            nameBytes = nameBytes[..MaxHostNameBytes];

        var frame = new byte[MinimumFrame + 2 + 1 + nameBytes.Length];
        Magic.CopyTo(frame);
        frame[4] = Version;
        frame[5] = ResponseType;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), (ushort)guardPort);
        frame[8] = (byte)nameBytes.Length;
        nameBytes.CopyTo(frame.AsSpan(9));
        return frame;
    }

    /// <summary>
    /// Parses a reply. Returns false rather than throwing: this is unsolicited
    /// traffic from an open network port, and any datagram at all can arrive.
    /// </summary>
    public static bool TryReadResponse(ReadOnlySpan<byte> frame, out int guardPort, out string hostName)
    {
        guardPort = 0;
        hostName = string.Empty;

        if (frame.Length < MinimumFrame + 3
            || !frame[..4].SequenceEqual(Magic)
            || frame[4] != Version
            || frame[5] != ResponseType)
            return false;

        guardPort = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(6, 2));
        if (guardPort == 0)
            return false;

        int nameLength = frame[8];
        if (nameLength > MaxHostNameBytes || frame.Length < 9 + nameLength)
            return false;

        hostName = Encoding.UTF8.GetString(frame.Slice(9, nameLength));
        return true;
    }
}
