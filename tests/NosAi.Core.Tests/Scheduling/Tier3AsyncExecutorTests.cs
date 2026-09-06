using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

/// <summary>
/// Proves, with real timeouts and real cancellation (never a synchronous
/// wait), that Tier 3 work can never block a caller past the job's own
/// declared deadline -- the exact invariant ADR-0022's "Performance policy"
/// and docs/ROADMAP_ESECUTIVA.md S:5 require ("Tier 3 non può bloccare
/// Safety/recovery").
/// </summary>
public sealed class Tier3AsyncExecutorTests
{
    private static InferenceJob Tier3Job(long nowUnixMillis, long deadlineOffsetMs, long durationMs = 10) =>
        new("reasoning-job", InferenceTier.Tier3ExpensiveLocalReasoning, JobPriority.Normal,
            nowUnixMillis + deadlineOffsetMs, durationMs, new ResourceCost(1, 1, 1, 1), 0.5, FallbackStrategy.Reject());

    [Fact]
    public async Task WorkCompletingBeforeDeadlineReturnsCompletedWithItsValue()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: 5_000);

        Tier3ExecutionResult<string> result = await executor.RunWithDeadlineAsync(job, async ct =>
        {
            await Task.Delay(5, ct);
            return "reasoning-output";
        });

        Assert.Equal(Tier3ExecutionOutcome.Completed, result.Outcome);
        Assert.True(result.Completed);
        Assert.Equal("reasoning-output", result.Value);
    }

    [Fact]
    public async Task ASafetyEquivalentCallerIsNeverHeldPastTheJobsOwnDeadline()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        const long deadlineWindowMs = 100;
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: deadlineWindowMs, durationMs: deadlineWindowMs);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync(job, async ct =>
        {
            // Deliberately "hangs" for far longer than the job's deadline.
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return 42;
        });
        stopwatch.Stop();

        Assert.Equal(Tier3ExecutionOutcome.TimedOut, result.Outcome);
        Assert.False(result.Completed);
        // The caller got control back near the job's own ~100ms deadline, not
        // after the work's 5-second duration: proof this never degenerated
        // into a blocking wait on the underlying computation.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 2_000,
            $"caller was held for {stopwatch.ElapsedMilliseconds}ms; expected well under the 5s work duration.");
    }

    [Fact]
    public async Task TimingOutRequestsRealCancellationOnTheUnderlyingWork()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: 80, durationMs: 80);

        bool cancellationObserved = false;

        Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync(job, async ct =>
        {
            ct.Register(() => cancellationObserved = true);
            await Task.Delay(Timeout.Infinite, ct); // never completes on its own
            return 1;
        });

        Assert.Equal(Tier3ExecutionOutcome.TimedOut, result.Outcome);
        // Real cancellation, not an abandoned task: CancellationTokenSource.Cancel()
        // invokes registered callbacks synchronously, so this is true by the
        // time RunWithDeadlineAsync has returned.
        Assert.True(cancellationObserved, "the work's cancellation token was never actually signalled.");
    }

    [Fact]
    public async Task DeadlineAlreadyElapsedReturnsTimedOutWithoutInvokingWork()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: -1); // deadline already in the past

        bool invoked = false;
        Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync(job, ct =>
        {
            invoked = true;
            return Task.FromResult(1);
        });

        Assert.Equal(Tier3ExecutionOutcome.TimedOut, result.Outcome);
        Assert.False(invoked, "work must never be invoked once its deadline has already elapsed.");
    }

    [Fact]
    public async Task NonTier3JobIsRejectedByConstruction()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        var job = new InferenceJob("wrong-tier", InferenceTier.Tier2GpuAcceleratedVision, JobPriority.Normal,
            clock.UnixMillis + 1_000, 10, new ResourceCost(1, 1, 1, 1), 0.5, FallbackStrategy.Reject());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            executor.RunWithDeadlineAsync(job, ct => Task.FromResult(1)));
    }

    [Fact]
    public async Task CallerSuppliedCancellationEndsTheRacePromptly()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: 60_000, durationMs: 10); // a deadline far in the future

        using var callerCts = new CancellationTokenSource();
        Task<Tier3ExecutionResult<int>> resultTask = executor.RunWithDeadlineAsync(job, async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 1;
        }, callerCts.Token);

        callerCts.Cancel();

        // Bounded wait so a regression that reintroduces blocking fails this
        // test instead of hanging the whole suite.
        Tier3ExecutionResult<int> result = await resultTask.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(Tier3ExecutionOutcome.Canceled, result.Outcome);
    }

    [Fact]
    public async Task WorkThatThrowsIsReportedAsFaultedNotSwallowed()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: 5_000);

        Tier3ExecutionResult<int> result = await executor.RunWithDeadlineAsync<int>(job, _ =>
            throw new InvalidOperationException("simulated reasoning failure"));

        Assert.Equal(Tier3ExecutionOutcome.Faulted, result.Outcome);
        Assert.Contains("simulated reasoning failure", result.Explanation);
    }

    [Fact]
    public async Task NullJobThrows()
    {
        var executor = new Tier3AsyncExecutor(new ManualMonotonicClock());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.RunWithDeadlineAsync<int>(null!, ct => Task.FromResult(1)));
    }

    [Fact]
    public async Task NullWorkThrows()
    {
        var clock = new ManualMonotonicClock(1_000_000);
        var executor = new Tier3AsyncExecutor(clock);
        InferenceJob job = Tier3Job(clock.UnixMillis, deadlineOffsetMs: 1_000);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.RunWithDeadlineAsync<int>(job, null!));
    }
}
