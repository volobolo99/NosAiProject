using NosAi.Security;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class SequenceGuardTests
{
    [Fact]
    public void FirstSequenceIsAlwaysAccepted()
    {
        var guard = new SequenceGuard();

        Assert.True(guard.TryAccept(42));
        Assert.Equal(42u, guard.HighWaterMark);
    }

    [Fact]
    public void IncreasingSequencesAreAccepted()
    {
        var guard = new SequenceGuard();

        for (uint sequence = 0; sequence < 50; sequence++)
            Assert.True(guard.TryAccept(sequence));

        Assert.Equal(49u, guard.HighWaterMark);
    }

    [Fact]
    public void ExactReplayOfTheCurrentHighWaterMarkIsRejected()
    {
        var guard = new SequenceGuard();
        Assert.True(guard.TryAccept(10));

        Assert.False(guard.TryAccept(10));
    }

    [Fact]
    public void ReplayOfAnOlderAlreadyAcceptedSequenceIsRejected()
    {
        var guard = new SequenceGuard();
        Assert.True(guard.TryAccept(100));
        Assert.True(guard.TryAccept(95));

        Assert.False(guard.TryAccept(95));
    }

    [Fact]
    public void OutOfOrderSequenceWithinTheWindowIsAcceptedOnce()
    {
        var guard = new SequenceGuard();
        Assert.True(guard.TryAccept(100));

        Assert.True(guard.TryAccept(80));
        Assert.False(guard.TryAccept(80));
    }

    [Fact]
    public void SequenceOlderThanTheWindowIsRejected()
    {
        var guard = new SequenceGuard(windowBits: 1024);
        Assert.True(guard.TryAccept(2000));

        // 2000 - 1024 = 976: exactly at the boundary, must be rejected as too old.
        Assert.False(guard.TryAccept(976));
        // One newer than the boundary is still in range and accepted.
        Assert.True(guard.TryAccept(977));
    }

    [Fact]
    public void LargeForwardJumpAgesOutEverySequenceItPasses()
    {
        var guard = new SequenceGuard(windowBits: 1024);
        Assert.True(guard.TryAccept(10));
        Assert.True(guard.TryAccept(20));

        Assert.True(guard.TryAccept(10_000));
        Assert.Equal(10_000u, guard.HighWaterMark);

        // Everything before the jump is now further back than the window can see.
        Assert.False(guard.TryAccept(20));
        Assert.False(guard.TryAccept(10));
    }

    [Fact]
    public void EveryPositionInTheWindowIsIndividuallyTrackedAcrossWordBoundaries()
    {
        var guard = new SequenceGuard(windowBits: 128);
        Assert.True(guard.TryAccept(1000));

        // Word boundary sits at offset 64 for a 128-bit window; probe both sides of it.
        Assert.True(guard.TryAccept(1000 - 63));
        Assert.True(guard.TryAccept(1000 - 64));
        Assert.True(guard.TryAccept(1000 - 65));
        Assert.True(guard.TryAccept(1000 - 127));

        Assert.False(guard.TryAccept(1000 - 63));
        Assert.False(guard.TryAccept(1000 - 64));
        Assert.False(guard.TryAccept(1000 - 65));
        Assert.False(guard.TryAccept(1000 - 127));

        // 128 positions back from the high-water mark is outside a 128-bit window.
        Assert.False(guard.TryAccept(1000 - 128));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(-64)]
    public void ConstructorRejectsWindowSizesThatAreNotAPositiveMultipleOf64(int windowBits)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SequenceGuard(windowBits));
    }
}
