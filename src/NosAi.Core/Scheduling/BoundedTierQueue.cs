using NosAi.Core.Hardware;

namespace NosAi.Core.Scheduling;

/// <summary>Outcome of a single bounded-capacity admission attempt into one tier's queue.</summary>
public enum QueueAdmission : byte
{
    Enqueued,
    Rejected
}

/// <summary>A structured, always-explicit result of a queue admission attempt -- never a silent drop.</summary>
public readonly record struct QueueAdmissionResult(QueueAdmission Outcome, string Explanation)
{
    public bool Accepted => Outcome == QueueAdmission.Enqueued;
}

/// <summary>
/// A bounded, per-<see cref="InferenceTier"/> FIFO queue. Each tier has its
/// own fixed capacity (docs/adr/ADR-0022-hybrid-cognitive-control-loop.md
/// "Performance policy": "Queues are bounded"); a job offered to a full tier
/// is rejected immediately with an explicit reason -- it is never silently
/// dropped and this call never blocks waiting for room to free up.
///
/// This type is a pure capacity gate: it knows nothing about deadlines,
/// confidence or resource budgets, and it does not apply a job's
/// <see cref="FallbackStrategy"/> itself. <see cref="InferenceBudgetScheduler"/>
/// is the single place that combines capacity, live budget and a job's
/// fallback into one admission decision; keeping this queue dumb makes both
/// halves independently testable and keeps fallback/degradation logic in
/// exactly one place (<see cref="FallbackStrategy.TryDegrade"/>).
///
/// Thread-safety: a single <see cref="object"/> lock guards the per-tier
/// dictionaries. The critical section is O(1) queue/dictionary bookkeeping
/// only -- it is never held across an inference call, so it cannot become
/// the kind of blocking this budget policy exists to prevent.
/// </summary>
public sealed class BoundedTierQueue
{
    private readonly Dictionary<InferenceTier, int> _capacities;
    private readonly Dictionary<InferenceTier, Queue<InferenceJob>> _queues;
    private readonly object _gate = new();

    public BoundedTierQueue(IReadOnlyDictionary<InferenceTier, int> capacities)
    {
        ArgumentNullException.ThrowIfNull(capacities);
        if (capacities.Count == 0)
            throw new ArgumentException("At least one tier capacity must be configured.", nameof(capacities));

        _capacities = new Dictionary<InferenceTier, int>();
        _queues = new Dictionary<InferenceTier, Queue<InferenceJob>>();
        foreach (KeyValuePair<InferenceTier, int> entry in capacities)
        {
            if (entry.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(capacities), $"Capacity for {entry.Key} must be >= 0.");
            _capacities[entry.Key] = entry.Value;
            _queues[entry.Key] = new Queue<InferenceJob>(entry.Value);
        }
    }

    /// <summary>The configured capacity for <paramref name="tier"/>, or 0 when the tier has no configured queue (meaning it is never admitted here).</summary>
    public int Capacity(InferenceTier tier) => _capacities.TryGetValue(tier, out int c) ? c : 0;

    /// <summary>The number of jobs currently queued (not yet dequeued) for <paramref name="tier"/>.</summary>
    public int Count(InferenceTier tier)
    {
        lock (_gate)
            return _queues.TryGetValue(tier, out Queue<InferenceJob>? q) ? q.Count : 0;
    }

    /// <summary>
    /// Attempts to enqueue <paramref name="job"/> under its own <see cref="InferenceJob.Tier"/>.
    /// Rejects immediately and explicitly (never blocks, never throws for a
    /// full queue) when that tier is at capacity or has no configured queue.
    /// </summary>
    public QueueAdmissionResult TryEnqueue(InferenceJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        lock (_gate)
        {
            if (!_queues.TryGetValue(job.Tier, out Queue<InferenceJob>? q))
                return new QueueAdmissionResult(QueueAdmission.Rejected, $"No bounded queue is configured for {job.Tier}.");

            int capacity = _capacities[job.Tier];
            if (q.Count >= capacity)
                return new QueueAdmissionResult(QueueAdmission.Rejected, $"{job.Tier} queue is full ({q.Count}/{capacity}); job '{job.Id}' was not enqueued.");

            q.Enqueue(job);
            return new QueueAdmissionResult(QueueAdmission.Enqueued, $"Admitted '{job.Id}' into {job.Tier} ({q.Count}/{capacity}).");
        }
    }

    /// <summary>Pops the next job (FIFO) queued for <paramref name="tier"/>, if any.</summary>
    public bool TryDequeue(InferenceTier tier, out InferenceJob? job)
    {
        lock (_gate)
        {
            if (_queues.TryGetValue(tier, out Queue<InferenceJob>? q) && q.Count > 0)
            {
                job = q.Dequeue();
                return true;
            }

            job = null;
            return false;
        }
    }

    /// <summary>
    /// Removes one specific job (matched by <see cref="InferenceJob.Id"/>) from
    /// <paramref name="tier"/>'s queue, wherever it currently sits in FIFO order,
    /// preserving the relative order of everything else.
    /// </summary>
    /// <remarks>
    /// This exists only to roll back an admission that enqueued successfully here
    /// but then failed a later, independent gate -- see
    /// <see cref="InferenceBudgetScheduler"/>'s handling of a losing
    /// <see cref="ResourceBudgetLedger.TryReserve"/> race. That is a rare path,
    /// not the normal admit/dequeue hot path, so an O(n) rebuild under the lock
    /// (rather than a data structure that supports O(1) arbitrary removal) is an
    /// acceptable cost here. Assumes <see cref="InferenceJob.Id"/> is unique among
    /// jobs concurrently queued for the same tier, exactly as
    /// <see cref="InferenceBudgetScheduler"/> already assumes when reporting an
    /// <see cref="AdmissionResult"/> keyed by a job's <c>Id</c>.
    /// </remarks>
    public bool TryRemove(InferenceTier tier, string jobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        lock (_gate)
        {
            if (!_queues.TryGetValue(tier, out Queue<InferenceJob>? q) || q.Count == 0)
                return false;

            var kept = new Queue<InferenceJob>(q.Count);
            bool removed = false;
            while (q.Count > 0)
            {
                InferenceJob candidate = q.Dequeue();
                if (!removed && candidate.Id == jobId)
                {
                    removed = true;
                    continue;
                }
                kept.Enqueue(candidate);
            }

            _queues[tier] = kept;
            return removed;
        }
    }
}
