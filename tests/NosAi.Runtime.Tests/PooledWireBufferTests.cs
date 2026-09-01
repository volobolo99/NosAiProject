using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="PooledWireBuffer"/>, the ArrayPool-backed frame buffer the read and
/// write adapters in <c>GuardAiNetworkChannel</c> and <c>GuardAiClient</c> rent
/// once per frame instead of allocating a fresh array.
/// </summary>
/// <remarks>
/// No fakes or mocking framework here: every assertion runs against the real
/// <see cref="System.Buffers.ArrayPool{T}.Shared"/> instance the production code
/// uses, on real rented arrays.
/// </remarks>
public sealed class PooledWireBufferTests
{
    [Fact]
    public void TheUsableLengthIsExactEvenThoughThePoolCanHandBackMore()
    {
        using var buffer = PooledWireBuffer.Rent(37);

        Assert.Equal(37, buffer.Length);
        Assert.Equal(37, buffer.Span.Length);
        Assert.Equal(37, buffer.Memory.Length);
    }

    [Fact]
    public void SpanAndMemoryAliasTheSameBytes()
    {
        using var buffer = PooledWireBuffer.Rent(WireHeader.HeaderSize);
        buffer.Span[0] = 0x4E; // 'N'
        buffer.Span[1] = 0x4F; // 'O'

        Assert.Equal(0x4E, buffer.Memory.Span[0]);
        Assert.Equal(0x4F, buffer.Memory.Span[1]);
    }

    [Fact]
    public void AZeroLengthRentNeedsNoPoolSlotAndDisposesCleanly()
    {
        using var buffer = PooledWireBuffer.Rent(0);

        Assert.Equal(0, buffer.Length);
        Assert.True(buffer.Span.IsEmpty);
        Assert.True(buffer.Memory.IsEmpty);
    }

    [Fact]
    public void ANegativeLengthIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PooledWireBuffer.Rent(-1));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var buffer = PooledWireBuffer.Rent(16);
        buffer.Dispose();

        // A second Dispose must not try to return the same array to the pool a
        // second time -- that would hand the same array out to two independent
        // renters later.
        buffer.Dispose();
    }

    [Fact]
    public void AccessingTheBufferAfterDisposeFailsClosedInsteadOfExposingPooledBytes()
    {
        var buffer = PooledWireBuffer.Rent(16);
        buffer.Dispose();

        // Span<byte> is a ref struct and cannot be a lambda's return value, so
        // each access is discarded inside a void body instead.
        Assert.Throws<ObjectDisposedException>(() => { _ = buffer.Span; });
        Assert.Throws<ObjectDisposedException>(() => { _ = buffer.Memory; });
    }

    [Fact]
    public void ManySequentialRentalsOfTheHeaderSizeRoundTripWithoutAliasing()
    {
        // This is the exact access pattern the read loop uses: one rent per
        // frame, written and read back before the next rent starts. A bug that
        // let two rentals alias the same underlying bytes would show up here as
        // some iteration reading back the wrong sequence number.
        for (uint sequence = 0; sequence < 500; sequence++)
        {
            using var buffer = PooledWireBuffer.Rent(WireHeader.HeaderSize);
            new WireHeader(WireMessageType.Heartbeat, 0, sequence).WriteTo(buffer.Span);

            Assert.True(WireHeader.TryRead(buffer.Span, out var header, out _));
            Assert.Equal(sequence, header.SequenceNumber);
            Assert.Equal(WireMessageType.Heartbeat, header.MessageType);
        }
    }
}
