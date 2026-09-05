namespace NosAi.Core.Hardware;

/// <summary>
/// Scheduling priority a caller declares for an inference job
/// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8 — "Each inference job
/// declares: priority; deadline; ..."). Ordered so a numeric comparison
/// (higher wins) is meaningful for whatever queue A3 builds on top of this
/// contract; this type declares the priority levels only, it does not
/// implement the scheduler.
/// </summary>
public enum InferenceJobPriority
{
    /// <summary>Opportunistic work that must yield under any resource pressure (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:3 "Background tier").</summary>
    Background = 0,

    /// <summary>Ordinary interactive-AI work (detection, tracking, OCR, embeddings).</summary>
    Normal = 1,

    /// <summary>Latency-sensitive work feeding an in-progress decision.</summary>
    Interactive = 2,

    /// <summary>Safety/Guard/recovery-adjacent work. Always Tier 0 in practice, but modelled here for completeness of the priority scale.</summary>
    Critical = 3
}

/// <summary>
/// The resource ceiling the runtime is willing to grant to jobs running at a
/// given <see cref="InferenceTier"/>, expressed across every dimension
/// listed in docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8 (AI scheduling):
/// CPU, GPU, VRAM, RAM, thermal headroom, SSD I/O and latency.
///
/// This is a pure data contract for the resource-budget policy (A3) to
/// compute and consume; <see cref="NosAi.Core.Hardware"/> does not implement
/// the scheduler/queue itself. Every field is a <see cref="ClassifiedValue{T}"/>
/// because a budget derived from an <see cref="DataSourceKind.Unknown"/>
/// hardware reading must itself be Unknown — never a synthetic zero that a
/// scheduler could misread as "budget is confirmed empty" rather than
/// "capacity cannot be confirmed, do not allocate" (fail-closed, no
/// overcommit — docs/ROADMAP_ESECUTIVA.md S:7 DoD "nessun overcommit
/// VRAM/RAM").
/// </summary>
public sealed record TierResourceBudget(
    InferenceTier Tier,
    ClassifiedValue<double> CpuBudgetPercent,
    ClassifiedValue<double> GpuBudgetPercent,
    ClassifiedValue<long> VramBudgetMb,
    ClassifiedValue<long> RamBudgetMb,
    ClassifiedValue<double> ThermalHeadroomCelsius,
    ClassifiedValue<double> SsdIoBudgetMbPerSecond,
    ClassifiedValue<double> LatencyBudgetMs)
{
    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> budget for <paramref name="tier"/>, used when the underlying hardware capability could not be classified.</summary>
    public static TierResourceBudget Unknown(InferenceTier tier, string reason) => new(
        tier,
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<long>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason));
}

/// <summary>
/// What a caller declares when asking the scheduler (A3) to run one
/// inference job, matching docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8
/// verbatim: priority, deadline, estimated CPU/GPU/RAM/VRAM cost, minimum
/// confidence required, and a fallback strategy.
///
/// Unlike <see cref="HardwareCapabilitySnapshot"/> and
/// <see cref="TierResourceBudget"/>, these fields are not hardware
/// observations — they are the job's own declared requirements — so they
/// are plain values rather than <see cref="ClassifiedValue{T}"/>. A job
/// that cannot honestly estimate one of these numbers should declare a
/// conservative (higher-cost, lower-confidence) estimate rather than omit
/// the field; there is deliberately no "unknown cost" escape hatch here,
/// because an unbounded job estimate is exactly what the RAM/VRAM
/// overcommit invariant forbids.
/// </summary>
public sealed record InferenceJobBudgetRequest(
    string JobId,
    InferenceTier Tier,
    InferenceJobPriority Priority,
    TimeSpan Deadline,
    double EstimatedCpuPercent,
    double EstimatedGpuPercent,
    long EstimatedRamMb,
    long EstimatedVramMb,
    double MinimumConfidence,
    InferenceTier? FallbackTier)
{
    /// <summary>
    /// Structural validity only (non-negative costs, confidence in [0,1],
    /// non-negative deadline, a non-empty job id, and — when a fallback is
    /// declared — a fallback that is strictly cheaper than <see cref="Tier"/>).
    /// This does not check the request against any actual hardware budget;
    /// that comparison is the scheduler's (A3's) job.
    /// </summary>
    public bool IsStructurallyValid =>
        !string.IsNullOrWhiteSpace(JobId)
        && Deadline >= TimeSpan.Zero
        && EstimatedCpuPercent >= 0
        && EstimatedGpuPercent >= 0
        && EstimatedRamMb >= 0
        && EstimatedVramMb >= 0
        && MinimumConfidence is >= 0 and <= 1
        && (FallbackTier is null || FallbackTier.Value < Tier);
}

/// <summary>
/// The complete per-tier budget table for one point in time, one budget per
/// <see cref="InferenceTier"/> (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8
/// — "budget ... per tier/sessione"). Modelled as four fixed named fields
/// rather than a dictionary/list so the type keeps full record value
/// equality (collections do not participate in record structural equality
/// in .NET, which would silently break the determinism/serializability this
/// contract is meant to guarantee).
/// </summary>
public sealed record InferenceResourceBudgetPlan(
    DateTime ComputedAtUtc,
    TierResourceBudget Tier0,
    TierResourceBudget Tier1,
    TierResourceBudget Tier2,
    TierResourceBudget Tier3)
{
    /// <summary>Looks up the budget for <paramref name="tier"/> without the caller needing a switch over the four fixed properties.</summary>
    public TierResourceBudget GetBudget(InferenceTier tier) => tier switch
    {
        InferenceTier.Tier0DeterministicRules => Tier0,
        InferenceTier.Tier1LightweightLocalMl => Tier1,
        InferenceTier.Tier2GpuAcceleratedVision => Tier2,
        InferenceTier.Tier3ExpensiveLocalReasoning => Tier3,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unrecognized InferenceTier.")
    };

    /// <summary>An all-<see cref="DataSourceKind.Unknown"/> budget plan for every tier, used when the underlying <see cref="HardwareCapabilitySnapshot"/> is itself Unknown.</summary>
    public static InferenceResourceBudgetPlan Unknown(string reason, DateTime? computedAtUtc = null) => new(
        computedAtUtc ?? DateTime.UtcNow,
        TierResourceBudget.Unknown(InferenceTier.Tier0DeterministicRules, reason),
        TierResourceBudget.Unknown(InferenceTier.Tier1LightweightLocalMl, reason),
        TierResourceBudget.Unknown(InferenceTier.Tier2GpuAcceleratedVision, reason),
        TierResourceBudget.Unknown(InferenceTier.Tier3ExpensiveLocalReasoning, reason));
}
