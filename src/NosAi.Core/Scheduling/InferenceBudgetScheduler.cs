namespace NosAi.Core.Scheduling;

/// <summary>How one admission attempt concluded.</summary>
public enum AdmissionOutcome : byte
{
    Accepted,
    AcceptedDegraded,
    Rejected
}

/// <summary>Why an admission attempt was ultimately rejected. <see cref="None"/> only ever appears on a non-rejected result.</summary>
public enum RejectionReason : byte
{
    None = 0,
    DeadlineUnreachable,
    InsufficientBudget,
    QueueFull
}

/// <summary>A structured, always-explicit admission outcome -- accepted (as declared or degraded) or rejected with a specific, logged reason. Never a silent drop, never a blocking call.</summary>
public readonly record struct AdmissionResult(AdmissionOutcome Outcome, InferenceJob Job, RejectionReason Reason, string Explanation)
{
    public bool Accepted => Outcome != AdmissionOutcome.Rejected;
}

/// <summary>
/// The AI budget policy's single admission authority for AP-00: combines a
/// live <see cref="ResourceBudgetLedger"/>, a <see cref="BoundedTierQueue"/>
/// and a job's own <see cref="FallbackStrategy"/> into one admission
/// decision per job. This is the type a future Strategic
/// Orchestrator/HTN integration should call to admit an
/// <see cref="InferenceJob"/> that <see cref="LowestCostJobSelector"/> (or
/// equivalent upstream logic) already picked as the candidate to run.
///
/// Every admission attempt checks, in this fixed order: (1) can the job
/// still meet its own deadline given its estimated duration; (2) does the
/// live resource ledger currently have headroom for its estimated cost;
/// (3) does its tier's bounded queue have room. The first check that fails
/// triggers exactly one attempt at the job's declared fallback (never a
/// cascade of further degradations -- see <see cref="FallbackStrategy.TryDegrade"/>);
/// if the degraded candidate also fails any of the three checks, admission
/// is rejected with a specific, explicit reason. Nothing here is ever
/// silently dropped, and nothing here blocks: this type performs only O(1)
/// bookkeeping and never itself invokes Tier 3 work (see
/// <see cref="Tier3AsyncExecutor"/> for that).
/// </summary>
public sealed class InferenceBudgetScheduler
{
    private readonly BoundedTierQueue _queue;
    private readonly ResourceBudgetLedger _ledger;
    private readonly IMonotonicClock _clock;

    public InferenceBudgetScheduler(BoundedTierQueue queue, ResourceBudgetLedger ledger, IMonotonicClock clock)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>The live resource ledger backing this scheduler's admission decisions.</summary>
    public ResourceBudgetLedger Ledger => _ledger;

    /// <summary>The bounded per-tier queue backing this scheduler's admission decisions.</summary>
    public BoundedTierQueue Queue => _queue;

    /// <summary>Attempts to admit <paramref name="job"/>, applying its own fallback exactly once on the first failing check.</summary>
    public AdmissionResult TryAdmit(InferenceJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return TryAdmitCore(job, originalJob: job, degradedOnce: false);
    }

    /// <summary>Releases the resource cost reserved for a previously admitted job, once its work is done (successfully, cancelled, or timed out -- reservation release does not depend on outcome).</summary>
    public void Complete(InferenceJob admittedJob)
    {
        ArgumentNullException.ThrowIfNull(admittedJob);
        _ledger.Release(admittedJob.EstimatedCost);
    }

    private AdmissionResult TryAdmitCore(InferenceJob job, InferenceJob originalJob, bool degradedOnce)
    {
        long now = _clock.UnixMillis;

        if (now + job.EstimatedDurationMs > job.DeadlineUnixMillis)
        {
            return FailOrDegradeOnce(
                job, originalJob, degradedOnce, RejectionReason.DeadlineUnreachable,
                $"'{job.Id}' at {job.Tier} cannot meet its deadline: now={now}, estimatedDuration={job.EstimatedDurationMs}ms, deadline={job.DeadlineUnixMillis}.");
        }

        ResourceCost available = _ledger.Available;
        if (!job.EstimatedCost.IsWithin(available))
        {
            return FailOrDegradeOnce(
                job, originalJob, degradedOnce, RejectionReason.InsufficientBudget,
                $"'{job.Id}' at {job.Tier} needs {job.EstimatedCost} but only {available} is available.");
        }

        QueueAdmissionResult queued = _queue.TryEnqueue(job);
        if (!queued.Accepted)
        {
            return FailOrDegradeOnce(job, originalJob, degradedOnce, RejectionReason.QueueFull, queued.Explanation);
        }

        // Reserve after a successful enqueue: the queue admission is the
        // last of the three gates, so nothing is reserved for a job that
        // was not actually accepted. The headroom check above (step 2) and
        // this reservation are two separate lock acquisitions, so a
        // concurrent admitter can pass that check against the same
        // still-unreserved headroom and then lose the actual reservation
        // race here -- the ledger is the only atomic source of truth for
        // that. A losing caller must not be reported as Accepted: undo the
        // enqueue and fall through the same fallback/degradation path as
        // any other rejection (AP-00/A5 audit finding; see
        // InferenceBudgetSchedulerConcurrencyTests).
        if (!_ledger.TryReserve(job.EstimatedCost))
        {
            _queue.TryRemove(job.Tier, job.Id);
            return FailOrDegradeOnce(
                job, originalJob, degradedOnce, RejectionReason.InsufficientBudget,
                $"'{job.Id}' at {job.Tier} passed the optimistic headroom check but lost a concurrent race to actually reserve {job.EstimatedCost}.");
        }

        AdmissionOutcome outcome = degradedOnce ? AdmissionOutcome.AcceptedDegraded : AdmissionOutcome.Accepted;
        string explanation = degradedOnce
            ? $"'{job.Id}' was degraded to {job.Tier} (from its originally declared tier) and admitted there."
            : $"'{job.Id}' admitted at {job.Tier}.";
        return new AdmissionResult(outcome, job, RejectionReason.None, explanation);
    }

    private AdmissionResult FailOrDegradeOnce(InferenceJob job, InferenceJob originalJob, bool alreadyDegraded, RejectionReason reason, string explanation)
    {
        if (!alreadyDegraded && job.Fallback.TryDegrade(job, out InferenceJob degraded))
        {
            AdmissionResult inner = TryAdmitCore(degraded, originalJob, degradedOnce: true);
            if (inner.Outcome != AdmissionOutcome.Rejected)
                return inner;

            return new AdmissionResult(
                AdmissionOutcome.Rejected, originalJob, inner.Reason,
                $"{explanation} Fallback to {degraded.Tier} also failed: {inner.Explanation}");
        }

        return new AdmissionResult(AdmissionOutcome.Rejected, originalJob, reason, explanation);
    }
}
