using System.Text;
using NosAi.Core;
using NosAi.Security;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class FrameCodecTests
{
    private static readonly byte[] SessionKey = Encoding.UTF8.GetBytes("gate1-session-key-for-tests-only");

    [Fact]
    public void RoundTripEncodeThenDecodeRecoversTheOriginalPayload()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("hello gate 1");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];

        int written = FrameCodec.Encode(opCode: 7, sequence: 42, payload, calculator, buffer);

        Assert.Equal(buffer.Length, written);
        Assert.True(FrameCodec.TryDecode(buffer, calculator, out NosFrameHeader header, out ReadOnlySpan<byte> decodedPayload, out FaultCode fault));
        Assert.Equal(FaultCode.None, fault);
        Assert.Equal(NosFrameHeader.CurrentVersion, header.Version);
        Assert.Equal(7, header.OpCode);
        Assert.Equal((ushort)payload.Length, header.Length);
        Assert.Equal(42u, header.Sequence);
        Assert.True(decodedPayload.SequenceEqual(payload));
    }

    [Fact]
    public void SingleBitFlipInThePayloadIsRejectedWithoutThrowing()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("integrity matters");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];
        FrameCodec.Encode(opCode: 1, sequence: 1, payload, calculator, buffer);

        buffer[FrameCodec.HeaderSize] ^= 0x01; // Flip one bit in the payload.

        bool ok = FrameCodec.TryDecode(buffer, calculator, out _, out _, out FaultCode fault);

        Assert.False(ok);
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void TamperedTagIsRejected()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];
        FrameCodec.Encode(opCode: 1, sequence: 1, payload, calculator, buffer);

        buffer[8] ^= 0xFF; // Tag occupies bytes 8..11.

        Assert.False(FrameCodec.TryDecode(buffer, calculator, out _, out _, out FaultCode fault));
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void WrongSessionKeyCannotDecodeAFrameEncodedByAnotherSession()
    {
        using var encoder = new FrameTagCalculator(SessionKey);
        using var wrongKey = new FrameTagCalculator(Encoding.UTF8.GetBytes("a completely different key"));

        byte[] payload = Encoding.UTF8.GetBytes("payload");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];
        FrameCodec.Encode(opCode: 1, sequence: 1, payload, encoder, buffer);

        Assert.False(FrameCodec.TryDecode(buffer, wrongKey, out _, out _, out FaultCode fault));
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void TruncatedFrameIsRejected()
    {
        using var calculator = new FrameTagCalculator(SessionKey);

        Assert.False(FrameCodec.TryDecode(new byte[5], calculator, out _, out _, out FaultCode fault));
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void WrongProtocolVersionIsRejected()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];
        FrameCodec.Encode(opCode: 1, sequence: 1, payload, calculator, buffer);

        buffer[0] = 0x02; // Only 0x01 is a valid version.

        Assert.False(FrameCodec.TryDecode(buffer, calculator, out _, out _, out FaultCode fault));
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void DeclaredLengthBeyondBufferIsRejected()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("payload");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];
        FrameCodec.Encode(opCode: 1, sequence: 1, payload, calculator, buffer);

        buffer[2] = 0xFF; // Corrupt the big-endian Length field to something implausible.
        buffer[3] = 0xFF;

        Assert.False(FrameCodec.TryDecode(buffer, calculator, out _, out _, out FaultCode fault));
        Assert.Equal(FaultCode.FrameInvalid, fault);
    }

    [Fact]
    public void EncodeRejectsPayloadsLargerThanTheFrameLimit()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] tooLarge = new byte[FrameCodec.MaxPayloadLength + 1];
        byte[] buffer = new byte[FrameCodec.HeaderSize + tooLarge.Length];

        Assert.Throws<ArgumentOutOfRangeException>(() => FrameCodec.Encode(1, 1, tooLarge, calculator, buffer));
    }

    [Fact]
    public void EncodingTenThousandFramesWithAWarmedUpCalculatorDoesNotGrowManagedHeapUsage()
    {
        using var calculator = new FrameTagCalculator(SessionKey);
        byte[] payload = Encoding.UTF8.GetBytes("steady-state payload for allocation check");
        Span<byte> buffer = new byte[FrameCodec.HeaderSize + payload.Length];

        // Warm up: JIT tiering and any one-time provider setup happens here, not in the measured region.
        for (int i = 0; i < 1000; i++)
            FrameCodec.Encode(1, (uint)i, payload, calculator, buffer);

        GC.Collect();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 10_000; i++)
            FrameCodec.Encode(1, (uint)i, payload, calculator, buffer);

        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
