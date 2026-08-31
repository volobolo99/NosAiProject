using System.Buffers.Binary;
using System.Net;

namespace NosAi.LiveIntegration.Capture;

/// <summary>A parsed IPv4 + TCP packet, or the reason it could not be parsed.</summary>
public readonly record struct ParsedPacket(
    bool Ok,
    IPAddress? Source,
    IPAddress? Destination,
    int SourcePort,
    int DestinationPort,
    uint SequenceNumber,
    bool Syn,
    bool Fin,
    bool Reset,
    ReadOnlyMemory<byte> Payload,
    string? Reason)
{
    public static ParsedPacket Failed(string reason) =>
        new(false, null, null, 0, 0, 0, false, false, false, ReadOnlyMemory<byte>.Empty, reason);
}

/// <summary>
/// Reads the IPv4 and TCP headers off a raw packet.
/// </summary>
/// <remarks>
/// <para>
/// WinDivert hands over whole IPv4 packets; this turns one into the fields
/// <see cref="TcpStreamReassembler"/> needs. It is deliberately strict — a packet
/// it cannot fully account for is refused with a reason, never parsed to a
/// plausible-looking guess, because a mis-parsed header downstream reads as real
/// application data.
/// </para>
/// <para>
/// IPv4 only for now. An IPv6 packet is refused explicitly rather than parsed as
/// though its header were IPv4: the two layouts differ, and silently treating one
/// as the other is exactly the failure this class is careful to avoid.
/// </para>
/// </remarks>
public static class Ipv4TcpParser
{
    private const int MinIpv4Header = 20;
    private const int MinTcpHeader = 20;
    private const byte ProtocolTcp = 6;

    public static ParsedPacket Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < MinIpv4Header)
            return ParsedPacket.Failed("packet_shorter_than_ipv4_header");

        int version = packet[0] >> 4;
        if (version != 4)
            return ParsedPacket.Failed($"not_ipv4:version_{version}");

        int ihl = (packet[0] & 0x0F) * 4;
        if (ihl < MinIpv4Header || packet.Length < ihl)
            return ParsedPacket.Failed("invalid_ipv4_header_length");

        if (packet[9] != ProtocolTcp)
            return ParsedPacket.Failed($"not_tcp:protocol_{packet[9]}");

        // totalLength lets a captured buffer carry trailing padding without it
        // leaking into the payload; clamp to what is actually present.
        int totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2, 2));
        int ipEnd = totalLength is >= MinIpv4Header && totalLength <= packet.Length ? totalLength : packet.Length;

        var source = new IPAddress(packet.Slice(12, 4).ToArray());
        var destination = new IPAddress(packet.Slice(16, 4).ToArray());

        ReadOnlySpan<byte> tcp = packet[ihl..ipEnd];
        if (tcp.Length < MinTcpHeader)
            return ParsedPacket.Failed("packet_shorter_than_tcp_header");

        int sourcePort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(0, 2));
        int destinationPort = BinaryPrimitives.ReadUInt16BigEndian(tcp.Slice(2, 2));
        uint sequence = BinaryPrimitives.ReadUInt32BigEndian(tcp.Slice(4, 4));

        int dataOffset = (tcp[12] >> 4) * 4;
        if (dataOffset < MinTcpHeader || tcp.Length < dataOffset)
            return ParsedPacket.Failed("invalid_tcp_data_offset");

        byte flags = tcp[13];
        bool fin = (flags & 0x01) != 0;
        bool syn = (flags & 0x02) != 0;
        bool reset = (flags & 0x04) != 0;

        var payload = tcp[dataOffset..].ToArray();

        return new ParsedPacket(
            true, source, destination, sourcePort, destinationPort, sequence,
            syn, fin, reset, payload, null);
    }

    /// <summary>
    /// Parses a packet and labels its direction against a known server endpoint.
    /// </summary>
    /// <remarks>
    /// A packet whose server side matches neither source nor destination is
    /// refused: it is not part of this conversation, and folding it into either
    /// stream would corrupt the reassembly.
    /// </remarks>
    public static bool TryParseSegment(
        ReadOnlySpan<byte> packet,
        IPAddress serverAddress,
        int serverPort,
        out TcpSegment segment,
        out string? reason)
    {
        segment = default;
        var parsed = Parse(packet);
        if (!parsed.Ok)
        {
            reason = parsed.Reason;
            return false;
        }

        bool fromServer = parsed.Source!.Equals(serverAddress) && parsed.SourcePort == serverPort;
        bool toServer = parsed.Destination!.Equals(serverAddress) && parsed.DestinationPort == serverPort;

        if (fromServer == toServer)
        {
            // Neither, or somehow both: not this conversation.
            reason = "endpoint_not_in_conversation";
            return false;
        }

        segment = new TcpSegment(
            fromServer ? StreamDirection.Inbound : StreamDirection.Outbound,
            parsed.SequenceNumber, parsed.Payload, parsed.Syn, parsed.Fin, parsed.Reset);
        reason = null;
        return true;
    }
}
