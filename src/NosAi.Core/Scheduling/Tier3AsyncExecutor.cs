using NosAi.Core.Hardware;

namespace NosAi.Core.Scheduling;

/// <summary>How a Tier 3 execution attempt concluded. There is no case that silently returns a made-up value: every outcome is one of these four, explicit.</summary>
public enum Tier3ExecutionOutcome : byte
{
    Completed,
    TimedOut,
    Canceled,
    Faulted
}

/// <summary>The structured result of one <see cref="Tier3AsyncExecutor.RunWithDeadlineAsync{T}"/> call.</summary>
public readonly record struct Tier3ExecutionResult<T>(Tier3ExecutionOutcome Outcome, T? Value, string Explanation)
{
    public bool Completed => Outcome == Tier3ExecutionOutcome.Completed;
}

/// <summary>
/// The concrete mechanism behind "Tier 3 reasoning must never block
/// Safety/recovery" (docs/adr/ADR-0022-hybrid-cognitive-control-loop.md
/// "Performance policy": "Reflex work must not wait on LLM/ML inference" /
/// "Strategic reasoning is interruptible"; docs/ROADMAP_ESECUTIVA.md S:5:
/// "Tier 3 non può bloccare Safety/recovery").
///
/// This class exposes exactly one execution entry point, and it is async:
/// there is no synchronous, blocking method on this type at all, so nothing
/// calling into it can be turned back into a blocking call by accident. Work
/// always runs on a background <see cref="Task"/>, always racing a
/// deadline-derived timeout via a linked <see cref="CancellationTokenSource"/>:
/// whichever finishes first wins, and losing side is cancelled. This type
/// itself never calls <c>.Result</c>/<c>.Wait()</c> on any task -- the one
/// pattern that would turn this async design back into a blocking one.
///
/// A caller that itself must not be delayed beyond a hard bound (a
/// Safety-equivalent caller) gets that guarantee for free by construction:
/// this method's returned <see cref="Task"/> always completes at or before
/// <c>job.DeadlineUnixMillis</c> (plus scheduling overhead), regardless of
/// whether the underlying work has actually finished, unwound its
/// cancellation, or is still running in the background.
/// </summary>
public sealed class Tier3AsyncExecutor
{
    private readonly IMonotonicClock _clock;

    public Tier3AsyncExecutor(IMonotonicClock clock) =>
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    /// <summary>
    /// Runs <paramref name="work"/> on a background task, racing it against
    /// the time remaining until <paramref name="job"/>'s own deadline. If
    /// work has not completed by the deadline, cancellation is requested on
    /// the token passed to <paramref name="work"/> and this method returns a
    /// <see cref="Tier3ExecutionOutcome.TimedOut"/> result immediately --
    /// it does not wait for <paramref name="work"/> to observe that
    /// cancellation and unwind.
    /// </summary>
    /// <param name="job">Must be a <see cref="InferenceTier.Tier3ExpensiveLocalReasoning"/> job; lower tiers do not need this deadline race and using it for them is a caller error.</param>
    /// <param name="work">The Tier 3 computation. Must observe the supplied <see cref="CancellationToken"/> to actually stop promptly when the deadline is reached; failing to do so leaks the background task (it keeps running, detached) but never blocks the caller of this method.</param>
    /// <param name="callerToken">An additional, caller-owned cancellation source (e.g. "the whole request was abandoned"); either this or the deadline can end the race.</param>
    public async Task<Tier3ExecutionResult<T>> RunWithDeadlineAsync<T>(
        InferenceJob job,
        Func<CancellationToken, Task<T>> work,
        CancellationToken callerToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(work);
        if (job.Tier != InferenceTier.Tier3ExpensiveLocalReasoning)
            throw new ArgumentException(
                $"Tier3AsyncExecutor only runs {InferenceTier.Tier3ExpensiveLocalReasoning} jobs; '{job.Id}' declares {job.Tier}, which does not need the non-blocking deadline race.",
                nameof(job));

        long remainingMs = job.DeadlineUnixMillis - _clock.UnixMillis;
        if (remainingMs <= 0)
        {
            return new Tier3ExecutionResult<T>(
                Tier3ExecutionOutcome.TimedOut, default,
                $"Deadline for '{job.Id}' had already elapsed before Tier3 work could start; work was never invoked.");
        }

        using var timeoutCts = new CancellationTokenSource();
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, callerToken);

        Task<T> workTask = Task.Run(() => work(linked.Token), linked.Token);
        Task delayTask = Task.Delay(TimeSpan.FromMilliseconds(remainingMs), timeoutCts.Token);

        Task finished = await Task.WhenAny(workTask, delayTask).ConfigureAwait(false);

        if (finished == workTask)
        {
            // Work finished (or faulted/cancelled on its own) before the
            // deadline: stop the now-useless delay promptly and report the
            // real outcome. delayTask, if it later transitions to Canceled,
            // is never observed again -- a canceled (non-faulted) task never
            // raises an unobserved-exception signal, so this is safe.
            timeoutCts.Cancel();
            try
            {
                T value = await workTask.ConfigureAwait(false);
                return new Tier3ExecutionResult<T>(Tier3ExecutionOutcome.Completed, value, $"'{job.Id}' completed within its {remainingMs}ms deadline.");
            }
            catch (OperationCanceledException)
            {
                return new Tier3ExecutionResult<T>(Tier3ExecutionOutcome.Canceled, default, $"'{job.Id}' observed cancellation before completing.");
            }
            catch (Exception ex)
            {
                return new Tier3ExecutionResult<T>(Tier3ExecutionOutcome.Faulted, default, $"'{job.Id}' faulted: {ex.Message}");
            }
        }

        // Deadline reached first. Request cancellation and return control to
        // the caller NOW -- this is the guarantee this whole type exists to
        // provide: a Safety-equivalent caller is never held past the job's
        // own declared deadline waiting for Tier 3 work to actually stop.
        timeoutCts.Cancel();
        return new Tier3ExecutionResult<T>(
            Tier3ExecutionOutcome.TimedOut, default,
            $"'{job.Id}' did not complete within its {remainingMs}ms deadline; cancellation requested, not awaited.");
    }
}
