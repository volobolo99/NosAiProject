using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>HasTarget</c> from the client's own memory (ADR-0021 § 1), now that the
/// behavioural oracle has established where the selection lives.
/// </summary>
/// <remarks>
/// The three states ADR-0018 insisted on stay three, and the middle one is the reason
/// this is worth a test of its own: zero at
/// <see cref="NosTaleClientLayout.TargetPointerOffset"/> is the client saying <i>no
/// target</i>, not a chain that broke, and reporting it as UNKNOWN would collapse two
/// states into one.
/// </remarks>
public sealed class MemoryTargetProviderTests
{
    private static readonly IntPtr Manager = new(0x21BF5C60);
    private static readonly IntPtr TargetObject = new(0x22C8A4F0);

    private sealed class BlankProvider : IGameplayProvider
    {
        public ClassifiedValue<bool>? Preset { get; init; }

        public string Name => "blank";

        public GameplayObservation Observe()
        {
            GameplayObservation observation = GameplayObservation.Unobserved("nothing_read");
            return Preset is { } preset ? observation with { HasTarget = preset } : observation;
        }
    }

    private static MemoryTargetGameplayProvider Over(
        TargetPointerReading? reading,
        string? failure = null,
        ClassifiedValue<bool>? preset = null) =>
        new(new BlankProvider { Preset = preset }, () => reading, () => failure);

    [Fact]
    public void APointerToSomethingIsATarget()
    {
        ClassifiedValue<bool> hasTarget = Over(
            new TargetPointerReading(Manager, TargetObject, 313906, null)).Observe().HasTarget;

        Assert.True(hasTarget.HasValue);
        Assert.True(hasTarget.Value);
        Assert.Equal(DataSourceKind.Derived, hasTarget.Source);
    }

    /// <summary>Zero is an answer the client wrote, not a chain that broke.</summary>
    [Fact]
    public void ZeroIsNoTargetAndNotUnknown()
    {
        ClassifiedValue<bool> hasTarget = Over(
            new TargetPointerReading(Manager, IntPtr.Zero, null, null)).Observe().HasTarget;

        Assert.True(hasTarget.HasValue);
        Assert.False(hasTarget.Value);
        Assert.Equal(DataSourceKind.Derived, hasTarget.Source);
    }

    [Fact]
    public void NoReadingAtAllIsUnknownWithTheReason()
    {
        ClassifiedValue<bool> hasTarget = Over(null, failure: "player_manager_null").Observe().HasTarget;

        Assert.False(hasTarget.HasValue);
        Assert.Equal("player_manager_null", hasTarget.FailureReason);
    }

    [Fact]
    public void WithoutAStatedReasonTheDefaultOneIsStillNamed()
    {
        ClassifiedValue<bool> hasTarget = Over(null).Observe().HasTarget;

        Assert.Equal(MemoryTargetGameplayProvider.SessionUnavailableReason, hasTarget.FailureReason);
    }

    /// <summary>
    /// <b>Derived, not live.</b> The client stores no boolean: what is read is a pointer,
    /// and the flag is concluded from it being non-zero. Calling that LIVE would claim
    /// the client publishes a flag it does not publish.
    /// </summary>
    [Fact]
    public void TheFlagIsDerivedBecauseNothingPublishesIt()
    {
        Assert.Equal(
            DataSourceKind.Derived,
            Over(new TargetPointerReading(Manager, TargetObject, null, null)).Observe().HasTarget.Source);
    }

    /// <summary>A source that already answered is not overruled; this fills a gap.</summary>
    [Fact]
    public void AnAnswerAlreadyOnTheObservationStands()
    {
        ClassifiedValue<bool> hasTarget = Over(
            new TargetPointerReading(Manager, IntPtr.Zero, null, null),
            preset: ClassifiedValue<bool>.Live(true)).Observe().HasTarget;

        Assert.True(hasTarget.Value);
        Assert.Equal(DataSourceKind.Live, hasTarget.Source);
    }

    /// <summary>
    /// Composed inside the screen decorator, so when both sources are present memory
    /// answers and the screen stays the second source (ADR-0021 §§ 1 and 5).
    /// </summary>
    [Fact]
    public void MemoryAnswersBeforeTheScreenDoes()
    {
        // The screen decorator skips when a value is already there, so an inner memory
        // reading is what the outer one sees and leaves alone.
        var memory = new MemoryTargetGameplayProvider(
            new BlankProvider(),
            () => new TargetPointerReading(Manager, IntPtr.Zero, null, null));

        GameplayObservation observation = memory.Observe();

        Assert.True(observation.HasTarget.HasValue);
        Assert.False(observation.HasTarget.Value);
        Assert.Contains("target-memory", memory.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity stays out of the observation. It is an analogy with the player object
    /// and has never been checked against <c>ct</c>; publishing it would be the plausible
    /// unverified number this codebase refuses everywhere else.
    /// </summary>
    [Fact]
    public void KnowingThatThereIsATargetDoesNotPublishWhichOne()
    {
        GameplayObservation observation = Over(
            new TargetPointerReading(Manager, TargetObject, 313906, null)).Observe();

        Assert.True(observation.HasTarget.Value);
        Assert.False(observation.SelectedTarget.HasValue);
    }
}
