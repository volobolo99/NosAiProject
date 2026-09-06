using NosAi.Core.Hardware;

namespace NosAi.Core.Scheduling;

/// <summary>
/// Scheduling priority used only as a tie-breaker inside the AI budget
/// policy (<see cref="LowestCostJobSelector"/>, <see cref="InferenceBudgetScheduler"/>).
/// This is not a Guard/Trust/Safety concept and carries no authorization
/// weight whatsoever: the runtime's execution authorization chain
/// (Guard -&gt; Trust -&gt; Safety) lives entirely outside this module and never
/// consults it (docs/ROADMAP_ESECUTIVA.md S:3).
/// </summary>
public enum JobPriority : byte
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>The explicit degradation profile a job falls back to when it cannot be admitted as declared.</summary>
public sealed record DegradationPlan(InferenceTier Tier, ResourceCost EstimatedCost, long EstimatedDurationMs, double MinimumConfidence)
{
    public ResourceCost EstimatedCost { get; init; } = EstimatedCost.IsNonNegative
        ? EstimatedCost
        : throw new ArgumentOutOfRangeException(nameof(EstimatedCost), "Degraded estimated cost must be non-negative in every component.");

    public long EstimatedDurationMs { get; init; } = EstimatedDurationMs >= 0
        ? EstimatedDurationMs
        : throw new ArgumentOutOfRangeException(nameof(EstimatedDurationMs), "Degraded estimated duration cannot be negative.");

    public double MinimumConfidence { get; init; } = MinimumConfidence is >= 0d and <= 1d
        ? MinimumConfidence
        : throw new ArgumentOutOfRangeException(nameof(MinimumConfidence), "MinimumConfidence must be in [0,1].");
}

/// <summary>What a job's failure to be admitted resolves to. There is no "silent drop": every kind here is an explicit, structured outcome.</summary>
public enum FallbackKind : byte
{
    /// <summary>No degradation is possible; a failure to admit becomes an explicit, structured rejection.</summary>
    Reject = 0,

    /// <summary>Retry admission once, at the cheaper profile described by <see cref="FallbackStrategy.Degradation"/>.</summary>
    Degrade = 1,

    /// <summary>Serve a previously computed, explicitly stale-marked result instead of running new inference (never mislabeled as live -- docs/ROADMAP_ESECUTIVA.md invariant: real/derived/cached/simulated data stay distinguishable).</summary>
    UseCachedResult = 2,

    /// <summary>Fall back to a Tier 0 deterministic default (a fixed rule/heuristic result) instead of running the requested inference.</summary>
    UseDeterministicDefault = 3
}

/// <summary>
/// A job's explicit degradation/fallback strategy (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md
/// S:8: "Each inference job declares ... fallback strategy"). Exactly one
/// non-degrade kind or a fully-specified <see cref="Degradation"/> plan --
/// never an ambiguous or partially-specified fallback.
/// </summary>
public sealed record FallbackStrategy
{
    public required FallbackKind Kind { get; init; }
    public DegradationPlan? Degradation { get; init; }
    public string? Reason { get; init; }

    public static FallbackStrategy Reject(string? reason = null) => new() { Kind = FallbackKind.Reject, Reason = reason };
    public static FallbackStrategy DegradeTo(DegradationPlan plan) => new() { Kind = FallbackKind.Degrade, Degradation = plan ?? throw new ArgumentNullException(nameof(plan)) };
    public static FallbackStrategy UseCachedResult(string? reason = null) => new() { Kind = FallbackKind.UseCachedResult, Reason = reason };
    public static FallbackStrategy UseDeterministicDefault(string? reason = null) => new() { Kind = FallbackKind.UseDeterministicDefault, Reason = reason };

    /// <summary>
    /// Applies this strategy to <paramref name="original"/> exactly once,
    /// producing the degraded candidate job. The degraded candidate's own
    /// fallback is always <see cref="Reject"/> so a second admission failure
    /// is a hard, explicit stop rather than a silent chain of demotions --
    /// degradation in this policy is single-hop by construction.
    /// </summary>
    public bool TryDegrade(InferenceJob original, out InferenceJob degraded)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (Kind != FallbackKind.Degrade || Degradation is null)
        {
            degraded = original;
            return false;
        }

        degraded = original with
        {
            Tier = Degradation.Tier,
            EstimatedCost = Degradation.EstimatedCost,
            EstimatedDurationMs = Degradation.EstimatedDurationMs,
            MinimumConfidence = Degradation.MinimumConfidence,
            Fallback = Reject($"already degraded once from {original.Tier} to {Degradation.Tier}")
        };
        return true;
    }
}

/// <summary>
/// An immutable inference-job declaration: exactly the five facts
/// docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8 requires every inference job
/// to declare (priority, deadline, estimated CPU/GPU/RAM/VRAM cost, minimum
/// required confidence, fallback strategy), plus an id for logging/tests and
/// an estimated duration -- without a time estimate neither the deadline
/// check in <see cref="InferenceBudgetScheduler"/> nor the deadline filter
/// in <see cref="LowestCostJobSelector"/> can be evaluated.
///
/// A job never carries execution authority: it is a request/description
/// consumed by this budget policy, never by Guard/Trust/Safety/Execute.
///
/// Validation lives in each redeclared property's initializer (the
/// documented pattern for adding checks to a positional record's primary
/// constructor): every property below runs its check exactly once, against
/// the primary-constructor parameter of the same name, before the instance
/// is considered constructed -- there is no path that produces a
/// partially-valid <see cref="InferenceJob"/>.
/// </summary>
public sealed record InferenceJob(
    string Id,
    InferenceTier Tier,
    JobPriority Priority,
    long DeadlineUnixMillis,
    long EstimatedDurationMs,
    ResourceCost EstimatedCost,
    double MinimumConfidence,
    FallbackStrategy Fallback)
{
    public string Id { get; init; } = string.IsNullOrWhiteSpace(Id)
        ? throw new ArgumentException("Id must be a non-empty, non-whitespace identifier.", nameof(Id))
        : Id;

    public long DeadlineUnixMillis { get; init; } = DeadlineUnixMillis > 0
        ? DeadlineUnixMillis
        : throw new ArgumentOutOfRangeException(nameof(DeadlineUnixMillis), "DeadlineUnixMillis must be a positive Unix-epoch millisecond timestamp.");

    public long EstimatedDurationMs { get; init; } = EstimatedDurationMs >= 0
        ? EstimatedDurationMs
        : throw new ArgumentOutOfRangeException(nameof(EstimatedDurationMs), "EstimatedDurationMs cannot be negative.");

    public ResourceCost EstimatedCost { get; init; } = EstimatedCost.IsNonNegative
        ? EstimatedCost
        : throw new ArgumentOutOfRangeException(nameof(EstimatedCost), "EstimatedCost must be non-negative in every component.");

    public double MinimumConfidence { get; init; } = MinimumConfidence is >= 0d and <= 1d
        ? MinimumConfidence
        : throw new ArgumentOutOfRangeException(nameof(MinimumConfidence), "MinimumConfidence must be in [0,1].");

    public FallbackStrategy Fallback { get; init; } = ValidateFallback(Fallback, Tier);

    private static FallbackStrategy ValidateFallback(FallbackStrategy fallback, InferenceTier tier)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        if (fallback.Kind == FallbackKind.Degrade)
        {
            if (fallback.Degradation is null)
                throw new ArgumentException("A Degrade fallback must carry a DegradationPlan.", nameof(fallback));
            if (fallback.Degradation.Tier >= tier)
                throw new ArgumentException("A degradation target must be a strictly cheaper tier than the job's own tier.", nameof(fallback));
        }

        return fallback;
    }
}
