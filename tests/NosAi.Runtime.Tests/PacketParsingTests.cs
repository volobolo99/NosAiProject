using System.Buffers.Binary;
using System.Net;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Reading IPv4 + TCP headers off a raw packet, and the honest framer above it.
/// </summary>
/// <remarks>
/// A mis-parsed header downstream reads as real application data, so the parser
/// refuses anything it cannot fully account for rather than guessing. The frames
/// above are labelled by how they were obtained — never a plausible message from
/// a protocol this code does not yet decode.
/// </remarks>
public sealed class PacketParsingTests
{
    /// <summary>Builds a minimal IPv4 + TCP packet with the given payload.</summary>
    private static byte[] BuildPacket(
        string source, int sourcePort, string destination, int destinationPort,
        uint sequence, byte[] payload, bool syn = false, bool fin = false)
    {
        const int ipHeader = 20, tcpHeader = 20;
        var packet = new byte[ipHeader + tcpHeader + payload.Length];

        packet[0] = 0x45; // version 4, IHL 5
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6; // TCP
        IPAddress.Parse(source).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(destination).GetAddressBytes().CopyTo(packet, 16);

        var tcp = packet.AsSpan(ipHeader);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(0, 2), (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), (ushort)destinationPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), sequence);
        tcp[12] = 5 << 4; // data offset 5 words
        tcp[13] = (byte)((syn ? 0x02 : 0) | (fin ? 0x01 : 0));
        payload.CopyTo(tcp[tcpHeader..]);

        return packet;
    }

    [Fact]
    public void AWellFormedPacketIsParsed()
    {
        var packet = BuildPacket("192.168.0.4", 56027, "79.110.84.175", 4006, 12345,
            Encoding.ASCII.GetBytes("hi"));

        var parsed = Ipv4TcpParser.Parse(packet);

        Assert.True(parsed.Ok);
        Assert.Equal(IPAddress.Parse("192.168.0.4"), parsed.Source);
        Assert.Equal(56027, parsed.SourcePort);
        Assert.Equal(4006, parsed.DestinationPort);
        Assert.Equal(12345u, parsed.SequenceNumber);
        Assert.Equal("hi", Encoding.ASCII.GetString(parsed.Payload.Span));
    }

    [Fact]
    public void DirectionIsLabelledAgainstTheServerEndpoint()
    {
        var server = IPAddress.Parse("79.110.84.175");

        var fromClient = BuildPacket("192.168.0.4", 56027, "79.110.84.175", 4006, 1, new byte[] { 1 });
        Assert.True(Ipv4TcpParser.TryParseSegment(fromClient, server, 4006, out var outSeg, out _));
        Assert.Equal(StreamDirection.Outbound, outSeg.Direction);

        var fromServer = BuildPacket("79.110.84.175", 4006, "192.168.0.4", 56027, 1, new byte[] { 1 });
        Assert.True(Ipv4TcpParser.TryParseSegment(fromServer, server, 4006, out var inSeg, out _));
        Assert.Equal(StreamDirection.Inbound, inSeg.Direction);
    }

    [Fact]
    public void APacketForAnotherEndpointIsRefused()
    {
        // Folding an unrelated conversation into either stream would corrupt it.
        var server = IPAddress.Parse("79.110.84.175");
        var elsewhere = BuildPacket("192.168.0.4", 5000, "93.184.216.34", 443, 1, new byte[] { 1 });

        Assert.False(Ipv4TcpParser.TryParseSegment(elsewhere, server, 4006, out _, out var reason));
        Assert.Equal("endpoint_not_in_conversation", reason);
    }

    [Fact]
    public void SynAndFinFlagsAreCarried()
    {
        var packet = BuildPacket("192.168.0.4", 5000, "79.110.84.175", 4006, 7, Array.Empty<byte>(), syn: true);
        var parsed = Ipv4TcpParser.Parse(packet);

        Assert.True(parsed.Syn);
        Assert.False(parsed.Fin);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(0)]
    public void APacketTooShortForItsHeadersIsRefused(int length)
    {
        var parsed = Ipv4TcpParser.Parse(new byte[length]);
        Assert.False(parsed.Ok);
        Assert.NotNull(parsed.Reason);
    }

    [Fact]
    public void AnIPv6PacketIsRefusedRatherThanMisread()
    {
        // The layouts differ; treating a v6 header as v4 is exactly the silent
        // mis-parse this parser is careful to avoid.
        var packet = new byte[40];
        packet[0] = 0x60; // version 6
        var parsed = Ipv4TcpParser.Parse(packet);

        Assert.False(parsed.Ok);
        Assert.StartsWith("not_ipv4", parsed.Reason);
    }

    [Fact]
    public void ANonTcpPacketIsRefused()
    {
        var packet = new byte[20];
        packet[0] = 0x45;
        packet[9] = 17; // UDP
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), 20);

        var parsed = Ipv4TcpParser.Parse(packet);
        Assert.False(parsed.Ok);
        Assert.StartsWith("not_tcp", parsed.Reason);
    }

    [Fact]
    public void TrailingPaddingDoesNotLeakIntoThePayload()
    {
        // totalLength bounds the real bytes; a captured buffer with slack must not
        // fold that slack into the payload.
        var packet = BuildPacket("192.168.0.4", 5000, "79.110.84.175", 4006, 1, Encoding.ASCII.GetBytes("data"));
        var padded = packet.Concat(new byte[16]).ToArray();

        var parsed = Ipv4TcpParser.Parse(padded);
        Assert.Equal("data", Encoding.ASCII.GetString(parsed.Payload.Span));
    }

    // -------------------------------------------------- honest framer

    [Fact]
    public void TheFramerReportsBytesAsUnknownUntilADecoderExists()
    {
        // ADR-0014 lifted the ban on reading the traffic; it did not hand over the
        // ability to interpret it. Bytes from an undecoded protocol are UNKNOWN,
        // not a fabricated message.
        var framer = new UnknownGameStreamFramer(StreamDirection.Inbound);

        var frames = framer.Consume(Encoding.ASCII.GetBytes("anything"));

        var frame = Assert.Single(frames);
        Assert.Equal(DataSourceKind.Unknown, frame.Source);
        Assert.Equal("no_nostale_decoder", frame.Reason);
        Assert.Equal(8, framer.TotalBytes);
    }

    [Fact]
    public void TheFramerKeepsTheRawBytesForDiagnosis()
    {
        // Unknown does not mean discarded: the bytes are real and flowing, and a
        // future decoder needs them.
        var framer = new UnknownGameStreamFramer(StreamDirection.Outbound);
        var frame = Assert.Single(framer.Consume(new byte[] { 1, 2, 3 }));

        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Body.ToArray());
    }

    [Fact]
    public void EmptyInputProducesNoFrames()
    {
        var framer = new UnknownGameStreamFramer(StreamDirection.Inbound);
        Assert.Empty(framer.Consume(ReadOnlySpan<byte>.Empty));
    }

    // -------------------------------------- parser into reassembler

    [Fact]
    public void PacketsFlowThroughTheParserAndReassemblerToAnOrderedStream()
    {
        // End to end on this layer, with the connection captured from its SYN so
        // the stream is anchored: two out-of-order data packets become one ordered
        // stream, exactly as a live capture from the handshake would deliver them.
        var server = IPAddress.Parse("79.110.84.175");
        var conversation = new TcpConversation();

        var syn = BuildPacket("79.110.84.175", 4006, "192.168.0.4", 56027, 99, Array.Empty<byte>(), syn: true);
        var second = BuildPacket("79.110.84.175", 4006, "192.168.0.4", 56027, 105, Encoding.ASCII.GetBytes(" world"));
        var first = BuildPacket("79.110.84.175", 4006, "192.168.0.4", 56027, 100, Encoding.ASCII.GetBytes("hello"));

        Ipv4TcpParser.TryParseSegment(syn, server, 4006, out var synSeg, out _);
        Ipv4TcpParser.TryParseSegment(second, server, 4006, out var secondSeg, out _);
        Ipv4TcpParser.TryParseSegment(first, server, 4006, out var firstSeg, out _);

        // SYN at 99 anchors the stream; data starts at 100.
        conversation.Accept(synSeg);
        // The later half arrives first and is held, then the missing half releases both.
        Assert.Empty(conversation.Accept(secondSeg));
        Assert.Equal("hello world", Encoding.ASCII.GetString(conversation.Accept(firstSeg)));
    }

    [Fact]
    public void CaptureBeginningMidStreamAnchorsAtTheFirstPacketSeen()
    {
        // Documented, not a bug: without a SYN, the reassembler cannot know a byte
        // it never saw existed, so the first packet captured is the start of the
        // observed stream. This is what a passive sniffer joining late must do.
        var server = IPAddress.Parse("79.110.84.175");
        var conversation = new TcpConversation();

        var midstream = BuildPacket("79.110.84.175", 4006, "192.168.0.4", 56027, 5000, Encoding.ASCII.GetBytes("late"));
        Ipv4TcpParser.TryParseSegment(midstream, server, 4006, out var seg, out _);

        Assert.Equal("late", Encoding.ASCII.GetString(conversation.Accept(seg)));
    }
}
