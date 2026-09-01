using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="NosTaleWorldDecoder"/>, checked against bytes the real server sent.
/// </summary>
/// <remarks>
/// <para>
/// The golden vector below is 35 bytes lifted verbatim from the T-04 recording of
/// a live session. It is not a constructed example: it is what came off the wire,
/// and it covers both branches of the decoder -- the literal one that carries the
/// opcode and the packed one that carries the numbers.
/// </para>
/// <para>
/// A decoder for a protocol nobody wrote down can only be trusted by what it
/// produces, so the assertions are on the decoded text rather than on internals.
/// The whole recording decodes to 2490 of 2490 fully printable packets; that
/// ratio, not any single case, is why this shape is believed.
/// </para>
/// </remarks>
public sealed class NosTaleWorldDecoderTests
{
    /// <summary>Two consecutive packets exactly as the server sent them.</summary>
    private const string GoldenHex =
        "0292899217175D81565155419EFF048C8B9E8B9C1B7491B749158641586414155C8EFF";

    private static byte[] Golden() => Convert.FromHexString(GoldenHex);

    [Fact]
    public void TheRecordedBytesDecodeToTheTwoPacketsTheClientRead()
    {
        IReadOnlyList<string> packets = NosTaleWorldDecoder.Decode(Golden());

        Assert.Equal(2, packets.Count);
        Assert.Equal("mv 3 3194 121 110 5", packets[0]);
        Assert.Equal("stat 7305 7305 1420 1420 0 1184", packets[1]);
    }

    [Fact]
    public void TheStatPacketCarriesTheVitalsTheClientWasShowing()
    {
        // The character's HUD read HP 7305/7305 and MP 1420/1420 while this was
        // captured. That correspondence is the whole of T-05: the wire says the
        // same thing the screen does, so it can be the source rather than a guess.
        string stat = NosTaleWorldDecoder.Decode(Golden()).Single(p => p.StartsWith("stat"));
        string[] fields = stat.Split(' ');

        Assert.Equal("stat", fields[0]);
        Assert.Equal("7305", fields[1]);   // HP
        Assert.Equal("7305", fields[2]);   // max HP
        Assert.Equal("1420", fields[3]);   // MP
        Assert.Equal("1420", fields[4]);   // max MP
    }

    [Fact]
    public void EveryDecodedCharacterIsPrintable()
    {
        // The signal that the decoder is right at all. Bytes read the wrong way
        // produce control characters and mojibake; these produce a grammar.
        foreach (string packet in NosTaleWorldDecoder.Decode(Golden()))
            Assert.All(packet, c => Assert.InRange(c, ' ', '~'));
    }

    [Fact]
    public void APacketWithNoTerminatorIsDroppedRatherThanReturnedHalfRead()
    {
        // Half a packet parses into fields that look like values and are not. A
        // stream that ends mid-packet is one still arriving, so the fragment waits
        // rather than being handed on as an observation.
        byte[] whole = Golden();
        byte[] truncated = whole[..^6];

        IReadOnlyList<string> packets = NosTaleWorldDecoder.Decode(truncated);

        Assert.Single(packets);
        Assert.Equal("mv 3 3194 121 110 5", packets[0]);
    }

    [Fact]
    public void AnEmptyStreamDecodesToNothing()
    {
        Assert.Empty(NosTaleWorldDecoder.Decode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void TheLiteralBranchComplementsItsBytes()
    {
        // 0x02 says two literal bytes follow; complemented they are 'm' and 'v'.
        // This is the observation the whole decoder was derived from.
        byte[] move = { 0x02, 0x92, 0x89, NosTaleWorldDecoder.PacketTerminator };

        Assert.Equal("mv", Assert.Single(NosTaleWorldDecoder.Decode(move)));
    }

    [Fact]
    public void OddLengthPackedFieldsDropTheirPaddingNibble()
    {
        // The low nibble of the last byte is padding when the length is odd.
        // Appending it would add a character the server never sent -- a digit,
        // here, which would silently multiply a value by ten.
        // Nibble n selects NibbleAlphabet[n-1], so 7, 8 and 9 are '3', '4', '5'.
        // The trailing 0 nibble of the second byte is the padding.
        byte[] threeNibbles = { 0x83, 0x78, 0x90, NosTaleWorldDecoder.PacketTerminator };

        string decoded = Assert.Single(NosTaleWorldDecoder.Decode(threeNibbles));

        Assert.Equal("345", decoded);
    }
}
