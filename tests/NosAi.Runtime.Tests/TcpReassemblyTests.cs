using System.Net;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Rebuilding a TCP conversation into the byte stream an application received.
/// </summary>
/// <remarks>
/// This is the layer a game-protocol decoder stands on, and the one that decides
/// whether anything above it is real. The cases that matter are the ones TCP
/// creates and a naive reader gets wrong: out-of-order arrival, retransmission,
/// gaps that must not be skipped, and the sequence-number wrap.
/// </remarks>
public sealed class TcpReassemblyTests
{
    private static TcpSegment Data(uint seq, string text, StreamDirection dir = StreamDirection.Inbound) =>
        new(dir, seq, Encoding.ASCII.GetBytes(text), Syn: false, Fin: false, Reset: false);

    private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes);

    // ------------------------------------------------------------ happy path

    [Fact]
    public void SegmentsInOrderProduceTheStream()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(1000);

        Assert.Equal("hello", Ascii(r.Accept(Data(1000, "hello"))));
        Assert.Equal(" world", Ascii(r.Accept(Data(1005, " world"))));
        Assert.Equal(11, r.DeliveredBytes);
        Assert.Equal(0, r.PendingBytes);
    }

    [Fact]
    public void TheFirstPayloadAnchorsAStreamCapturedMidConversation()
    {
        // No SYN seen: the stream is what followed the first payload, and it is
        // never pretended to have started earlier.
        var r = new TcpStreamReassembler();

        Assert.Equal("mid", Ascii(r.Accept(Data(50000, "mid"))));
        Assert.True(r.IsAnchored);
    }

    [Fact]
    public void ASynOccupiesOneSequenceBeforeTheData()
    {
        // The classic off-by-one: SYN consumes a sequence number, so the first
        // data byte is at seq+1. Getting this wrong shifts the whole stream.
        var r = new TcpStreamReassembler();
        var syn = new TcpSegment(StreamDirection.Inbound, 4000, ReadOnlyMemory<byte>.Empty, Syn: true, Fin: false, Reset: false);
        r.Accept(syn);

        Assert.Equal("first", Ascii(r.Accept(Data(4001, "first"))));
    }

    // --------------------------------------------------------- out of order

    [Fact]
    public void AnOutOfOrderSegmentIsHeldUntilTheGapFills()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(1000);

        // The later half arrives first: nothing is deliverable, and it is held,
        // not emitted out of order.
        Assert.Empty(r.Accept(Data(1005, "world")));
        Assert.Equal(5, r.PendingBytes);

        // The missing half arrives and unblocks both at once.
        Assert.Equal("helloworld", Ascii(r.Accept(Data(1000, "hello"))));
        Assert.Equal(0, r.PendingBytes);
    }

    [Fact]
    public void SeveralSegmentsQueuedBehindAGapAllReleaseTogether()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(0);

        r.Accept(Data(3, "lo"));
        r.Accept(Data(5, " wor"));
        r.Accept(Data(9, "ld"));
        Assert.Equal(8, r.PendingBytes);

        Assert.Equal("hello world", Ascii(r.Accept(Data(0, "hel"))));
    }

    // ------------------------------------------------------ retransmission

    [Fact]
    public void AFullRetransmissionOfDeliveredBytesIsIgnored()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(1000);
        r.Accept(Data(1000, "hello"));

        // The same bytes again: already delivered, so nothing new.
        Assert.Empty(r.Accept(Data(1000, "hello")));
        Assert.Equal(5, r.DeliveredBytes);
    }

    [Fact]
    public void AnOverlappingRetransmissionContributesOnlyItsNewTail()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(1000);
        r.Accept(Data(1000, "hello"));

        // Starts inside delivered data, carries three new bytes past it.
        Assert.Equal("123", Ascii(r.Accept(Data(1003, "lo123"))));
        Assert.Equal(8, r.DeliveredBytes);
    }

    [Fact]
    public void AnOverlapWithHeldDataKeepsTheFirstSeenBytes()
    {
        var r = new TcpStreamReassembler();
        r.Anchor(0);

        r.Accept(Data(4, "abcd"));           // held past a gap, seq 4..7
        r.Accept(Data(6, "XX"));             // fully inside the held block: first-seen wins, dropped
        var released = Ascii(r.Accept(Data(0, "0123")));

        Assert.Equal("0123abcd", released);
    }

    // -------------------------------------------------------------- gaps

    [Fact]
    public void AGapStopsOutputAndTheBytesPastItAreNotSkipped()
    {
        // The property that keeps this honest: a missing segment must not splice
        // unrelated bytes together. Output stops at the gap.
        var r = new TcpStreamReassembler();
        r.Anchor(1000);

        Assert.Equal("AAAA", Ascii(r.Accept(Data(1000, "AAAA"))));
        // seq 1004..1005 never arrives; this is at 1006. It cannot be delivered
        // yet, so nothing comes out and it is held, not spliced onto "AAAA".
        Assert.Empty(r.Accept(Data(1006, "CCCC")));
        Assert.Equal(4, r.PendingBytes);
        Assert.Equal(4, r.DeliveredBytes);

        // The gap fills: the held bytes release, in order, right after it.
        Assert.Equal("BBCCCC", Ascii(r.Accept(Data(1004, "BB"))));
        Assert.Equal(0, r.PendingBytes);
    }

    // --------------------------------------------------- sequence wrap

    [Fact]
    public void TheStreamSurvivesTheSequenceNumberWrap()
    {
        // Sequence space wraps through zero. Comparing the raw uint32s would put
        // the post-wrap segment before the pre-wrap one and misorder the stream.
        var r = new TcpStreamReassembler();
        uint start = uint.MaxValue - 3; // 0xFFFFFFFC
        r.Anchor(start);

        Assert.Equal("WXYZ", Ascii(r.Accept(Data(start, "WXYZ"))));   // ...FC,FD,FE,FF
        // Next byte is at seq 0 — the wrap.
        Assert.Equal("abcd", Ascii(r.Accept(Data(0, "abcd"))));
        Assert.Equal(8, r.DeliveredBytes);
    }

    [Fact]
    public void AnOutOfOrderSegmentAcrossTheWrapIsOrderedCorrectly()
    {
        var r = new TcpStreamReassembler();
        uint start = uint.MaxValue - 1; // 0xFFFFFFFE
        r.Anchor(start);

        // "pre" is 2 bytes at FFFFFFFE, FFFFFFFF; the next byte is at 0 (the wrap).
        // "post" at 0 arrives first — its raw value is far below the anchor, so
        // only serial comparison keeps it after rather than before.
        Assert.Empty(r.Accept(Data(0, "post")));
        Assert.Equal("XYpost", Ascii(r.Accept(Data(start, "XY"))));
    }

    // ------------------------------------------------ both directions

    [Fact]
    public void TheTwoDirectionsAreReassembledIndependently()
    {
        var conversation = new TcpConversation();

        Assert.Equal("ping", Ascii(conversation.Accept(Data(0, "ping", StreamDirection.Outbound))));
        Assert.Equal("pong", Ascii(conversation.Accept(Data(0, "pong", StreamDirection.Inbound))));
        // Same sequence numbers, different streams: they must not interfere.
        Assert.Equal(4, conversation.Outbound.DeliveredBytes);
        Assert.Equal(4, conversation.Inbound.DeliveredBytes);
    }
}
