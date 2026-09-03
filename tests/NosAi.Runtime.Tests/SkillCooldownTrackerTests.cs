using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Skill readiness from the wire, which is where it was all along.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 hunted a cooldown word through 196 MB of the client's memory and used
/// the wire's <c>sr</c> packet as the referee for every candidate. The referee
/// was the answer: <c>sr</c> says a skill has become available again, the runtime
/// knows when it used one, and those are the two ends of a cooldown.
/// </para>
/// <para>
/// The times here are the ones a live client produced: a use, then a restoration
/// announced at 13:00:14 and again at 13:00:47.
/// </para>
/// </remarks>
public sealed class SkillCooldownTrackerTests
{
    private static readonly DateTime Used = new(2026, 9, 3, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ready = new(2026, 9, 3, 13, 0, 14, DateTimeKind.Utc);
    private const int Slot = 6;

    // ---------- nothing is assumed

    [Fact]
    public void ASlotNobodyHasUsedIsUnknownAndNotReady()
    {
        // Guessing ready is how a Ranking proposes an act the Verify then finds it
        // could not take. Unknown is the honest state and it is not "false".
        var tracker = new SkillCooldownTracker();

        SkillCooldownReading reading = tracker.Read(Slot, Used);

        Assert.Equal(DataSourceKind.Unknown, reading.Source);
        Assert.Equal(SkillCooldownTracker.NeverObservedReason, reading.FailureReason);
        Assert.False(reading.Ready);
    }

    [Fact]
    public void AnImplausibleSlotIsRefusedWithItsNumber()
    {
        var tracker = new SkillCooldownTracker();

        SkillCooldownReading reading = tracker.Read(9000, Used);

        Assert.Equal(DataSourceKind.Unknown, reading.Source);
        Assert.StartsWith(SkillCooldownTracker.SlotImplausiblePrefix, reading.FailureReason, StringComparison.Ordinal);
        Assert.Contains("9000", reading.FailureReason, StringComparison.Ordinal);
    }

    // ---------- the two ends of a cooldown

    [Fact]
    public void AUsedSkillIsNotReadyUntilTheWireSaysSo()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);

        SkillCooldownReading during = tracker.Read(Slot, Used.AddSeconds(5));

        Assert.False(during.Ready);
        Assert.Equal(DataSourceKind.Derived, during.Source);
        Assert.Null(during.FailureReason);
    }

    [Fact]
    public void TheWiresAnnouncementMakesItReady()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);

        Assert.True(tracker.Read(Slot, Ready.AddSeconds(1)).Ready);
    }

    [Fact]
    public void UsingItAgainPutsItBackOnCooldown()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);
        tracker.NoteUsed(Slot, Ready.AddSeconds(20));

        Assert.False(tracker.Read(Slot, Ready.AddSeconds(21)).Ready);
    }

    // ---------- the length is measured, never assumed

    [Fact]
    public void TheFirstCooldownHasNoRemainingTimeBecauseNobodyHasTimedItYet()
    {
        // Null is unknown, and unknown is not zero. A remaining time invented on
        // the first use would be a number nothing observed.
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);

        SkillCooldownReading reading = tracker.Read(Slot, Used.AddSeconds(3));

        Assert.False(reading.Ready);
        Assert.Null(reading.Remaining);
        Assert.Null(reading.Measured);
    }

    [Fact]
    public void OneCompleteCycleMeasuresTheCooldownFromTheWire()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);

        Assert.Equal(TimeSpan.FromSeconds(14), tracker.Read(Slot, Ready).Measured);
    }

    [Fact]
    public void AfterOneCycleTheRemainingTimeIsCountedDown()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);

        DateTime again = Ready.AddSeconds(30);
        tracker.NoteUsed(Slot, again);

        SkillCooldownReading reading = tracker.Read(Slot, again.AddSeconds(4));

        Assert.False(reading.Ready);
        Assert.Equal(TimeSpan.FromSeconds(10), reading.Remaining);
    }

    [Fact]
    public void AnOverrunCooldownReportsZeroLeftRatherThanANegativeTime()
    {
        // The measurement is the last one observed, not a promise. A skill still
        // waiting past it has zero left to predict and is still not ready.
        var tracker = new SkillCooldownTracker();
        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);
        tracker.NoteUsed(Slot, Ready.AddSeconds(30));

        SkillCooldownReading reading = tracker.Read(Slot, Ready.AddSeconds(90));

        Assert.False(reading.Ready);
        Assert.Equal(TimeSpan.Zero, reading.Remaining);
    }

    [Fact]
    public void AnAnnouncementWithNoUseBehindItMakesItReadyAndMeasuresNothing()
    {
        // The player used the skill by hand, or the runtime attached mid-session.
        // Ready is still true; a duration derived from it would be invented.
        var tracker = new SkillCooldownTracker();
        tracker.NoteReady(Slot, Ready);

        SkillCooldownReading reading = tracker.Read(Slot, Ready.AddSeconds(1));

        Assert.True(reading.Ready);
        Assert.Null(reading.Measured);
        Assert.Equal(DataSourceKind.Derived, reading.Source);
    }

    // ---------- how it reads

    [Fact]
    public void EveryStateSaysWhichItIsAndWhereItCameFrom()
    {
        var tracker = new SkillCooldownTracker();

        Assert.Contains("UNKNOWN", tracker.Read(Slot, Used).Describe(), StringComparison.Ordinal);

        tracker.NoteUsed(Slot, Used);
        tracker.NoteReady(Slot, Ready);
        Assert.Contains("DERIVED", tracker.Read(Slot, Ready).Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void EverySlotSeenIsReportedInOrder()
    {
        var tracker = new SkillCooldownTracker();
        tracker.NoteReady(6, Ready);
        tracker.NoteReady(0, Ready);
        tracker.NoteReady(4, Ready);

        IReadOnlyList<SkillCooldownReading> all = tracker.ReadAll(Ready);

        Assert.Equal(new[] { 0, 4, 6 }, all.Select(r => r.Slot).ToArray());
    }
}
