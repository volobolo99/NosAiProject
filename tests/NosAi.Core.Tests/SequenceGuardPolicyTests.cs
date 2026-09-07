using NosAi.Runtime.Gate1;
using NosAi.Security;
using Xunit;

namespace NosAi.Core.Tests;

/// <summary>
/// The two ends of the Gate 1 channel enforce different replay policies, and
/// this says which so a change to either is a decision.
/// </summary>
/// <remarks>
/// <para>
/// Both types were called <c>SequenceGuard</c>, in two namespaces, with two
/// behaviours. <c>DuplicateTypeNameTests</c> could not see the pair because it
/// read one assembly; widening it surfaced them, and the head of that file
/// describes exactly this shape of defect from the two <c>SafetyGate</c>s -- the
/// same name, two behaviours, months without anyone noticing, because reading
/// one file gave no hint the other existed.
/// </para>
/// <para>
/// The asymmetry is real: <c>GuardAiClient</c> numbers its own frames with the
/// monotonic guard, so it emits a strictly consecutive sequence, while
/// <c>NosAiHost</c> validates incoming frames with the sliding window, which
/// accepts a gap the client can never produce. Whether the host should be as
/// strict as the client's contract is a protocol decision on a channel verified
/// once against real hardware; these tests do not take it. They make it
/// impossible to change either policy by accident.
/// </para>
/// </remarks>
public sealed class SequenceGuardPolicyTests
{
    /// <summary>The client's guard refuses a gap by name.</summary>
    [Fact]
    public void TheMonotonicGuardRefusesAGap()
    {
        var guard = new MonotonicSequenceGuard();

        Assert.True(guard.ValidateAndAdvance(1, out _));
        Assert.False(guard.ValidateAndAdvance(3, out string? reason));
        Assert.Equal("sequence_gap", reason);

        // And the expected sequence has not moved: a refused frame teaches it
        // nothing, which is what stops a forged jump from resynchronising it.
        Assert.Equal(2u, guard.Next);
    }

    /// <summary>And a replay, distinctly.</summary>
    [Fact]
    public void TheMonotonicGuardRefusesAReplayWithItsOwnReason()
    {
        var guard = new MonotonicSequenceGuard();
        Assert.True(guard.ValidateAndAdvance(1, out _));
        Assert.True(guard.ValidateAndAdvance(2, out _));

        Assert.False(guard.ValidateAndAdvance(1, out string? reason));
        Assert.Equal("replay_or_duplicate", reason);
    }

    /// <summary>
    /// The host's guard accepts a gap. This is the asymmetry, asserted rather
    /// than described.
    /// </summary>
    /// <remarks>
    /// If someone tightens the host to match the client, this test goes red and
    /// names what changed -- which is the point of pinning a divergence you have
    /// decided not to resolve yet.
    /// </remarks>
    [Fact]
    public void TheSlidingWindowGuardAcceptsAGapTheClientCannotProduce()
    {
        var guard = new SlidingWindowSequenceGuard();

        Assert.True(guard.TryAccept(1));
        Assert.True(guard.TryAccept(500));
        Assert.Equal(500u, guard.HighWaterMark);
    }

    /// <summary>Both refuse a replay: that much they agree on.</summary>
    [Fact]
    public void BothRefuseTheSameSequenceTwice()
    {
        var monotonic = new MonotonicSequenceGuard();
        Assert.True(monotonic.ValidateAndAdvance(1, out _));
        Assert.False(monotonic.ValidateAndAdvance(1, out _));

        var window = new SlidingWindowSequenceGuard();
        Assert.True(window.TryAccept(7));
        Assert.False(window.TryAccept(7));
    }
}
