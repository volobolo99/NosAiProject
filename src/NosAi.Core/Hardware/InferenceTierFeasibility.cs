namespace NosAi.Core.Hardware;

/// <summary>
/// Minimum hardware capability each <see cref="InferenceTier"/> requires
/// before <see cref="InferenceTierFeasibility.CanRun"/> reports it
/// executable. These are a conservative, documented coarse gate — a "can
/// this even be attempted without risking VRAM/RAM overcommit" check — not
/// the tuned scheduling policy A3 builds on top of this contract layer. A3
/// may apply additional, stricter constraints (queue depth, concurrent job
/// count, measured p95 latency, etc.); it must never relax these floors,
/// since that would reopen the overcommit risk this gate exists to close.
///
/// Rationale for each number is documented on the constant itself and
/// traces back to docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md and
/// docs/ROADMAP_ESECUTIVA.md S:5/S:7.
/// </summary>
public static class InferenceTierThresholds
{
    /// <summary>
    /// Tier 1 (lightweight local ML) needs at least a dual-core CPU to run
    /// without visibly starving the rest of the runtime loop (WorldState,
    /// planning, capture) that also runs on CPU
    /// (docs/NOSAI_ARCHITECTURE_BASELINE.md S:5 "CPU").
    /// </summary>
    public const int Tier1MinLogicalCores = 2;

    /// <summary>
    /// Tier 1 needs enough total system RAM that a small model plus its
    /// buffers cannot plausibly starve the rest of the 16 GB DDR5 baseline
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:5). 4 GB is a floor far
    /// below the 16 GB target, guarding only against misdetection or a
    /// severely constrained environment.
    /// </summary>
    public const long Tier1MinTotalRamMb = 4096;

    /// <summary>Tier 1 (CPU-only) tolerates elevated thermals but not active throttling.</summary>
    public const HardwareThrottleState Tier1MaxThrottleState = HardwareThrottleState.Elevated;

    /// <summary>
    /// Tier 2 (GPU vision/embeddings) requires a small resident model to
    /// fit; docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:4 directs "prefer one
    /// resident primary vision model" and "use quantized/small models", so
    /// 2 GB total VRAM is the floor a quantized detector/OCR/embedding model
    /// is expected to fit within on an 8 GB-class GPU.
    /// </summary>
    public const long Tier2MinTotalVramMb = 2048;

    /// <summary>
    /// Tier 2 additionally requires 1 GB of *free* VRAM headroom (not just
    /// total capacity) so loading the model does not overcommit VRAM
    /// already used by the game client or another resident model
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:4 point 8, "reject a model
    /// configuration that cannot fit the detected budget").
    /// </summary>
    public const long Tier2MinFreeVramMb = 1024;

    /// <summary>Tier 2 needs headroom in system RAM for staging/transfer buffers alongside the GPU work.</summary>
    public const long Tier2MinTotalRamMb = 8192;

    /// <summary>Tier 2 (GPU work) is more heat-sensitive than Tier 1 but still tolerates "elevated"; active throttling disqualifies it.</summary>
    public const HardwareThrottleState Tier2MaxThrottleState = HardwareThrottleState.Elevated;

    /// <summary>Tier 3 needs headroom in total system RAM regardless of which execution path (GPU or CPU fallback) is used.</summary>
    public const long Tier3MinTotalRamMb = 8192;

    /// <summary>
    /// On the GPU path, Tier 3 needs substantially more total VRAM than
    /// Tier 2 — 6 GB out of the 8 GB-class RTX 5060 Laptop ceiling
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:2) — leaving headroom for
    /// the client and OS compositor rather than assuming exclusive GPU use.
    /// </summary>
    public const long Tier3GpuPathMinTotalVramMb = 6144;

    /// <summary>Tier 3's GPU path needs more free VRAM headroom than Tier 2 because the model itself is larger.</summary>
    public const long Tier3GpuPathMinFreeVramMb = 3072;

    /// <summary>
    /// Tier 3 may fall back to CPU-only execution when no suitable GPU is
    /// available (docs/ROADMAP_ESECUTIVA.md S:5 allows Tier 3 "expensive
    /// local reasoning" without mandating a GPU). This path needs a
    /// meaningfully larger core count than Tier 1 to bound latency.
    /// </summary>
    public const int Tier3CpuFallbackMinLogicalCores = 8;

    /// <summary>The CPU fallback path checks *available* (not just total) RAM, since a Tier 3 CPU workload's memory footprint is large enough that current pressure matters.</summary>
    public const long Tier3CpuFallbackMinAvailableRamMb = 6144;

    /// <summary>
    /// Tier 3 is the heaviest, least interruptible tier, so unlike Tier 1/2
    /// it requires a fully nominal thermal state — even "elevated" is
    /// treated as insufficient headroom to start expensive reasoning
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:9 point 4, "switch
    /// expensive inference to lighter models/backends" under rising
    /// temperature).
    /// </summary>
    public const HardwareThrottleState Tier3MaxThrottleState = HardwareThrottleState.Nominal;
}

/// <summary>
/// Determines whether an <see cref="InferenceTier"/> can be attempted given
/// a <see cref="HardwareCapabilitySnapshot"/>. This is the "coarse feasibility
/// gate" referenced throughout docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:8:
/// it answers "is it even safe to try", not "should the scheduler pick this
/// right now" (queueing, priority, concurrent-job accounting and deadline
/// matching belong to A3's budget/scheduling policy, built on top of this
/// contract layer).
///
/// Every check fails closed: a capability the snapshot reports as
/// <see cref="DataSourceKind.Unknown"/> is treated as insufficient, never as
/// "assume it's fine". This directly implements the "no VRAM/RAM overcommit"
/// DoD in docs/ROADMAP_ESECUTIVA.md S:7 and the "Unknown is not zero, false
/// or empty" invariant in CLAUDE.md: an unconfirmed GPU can never satisfy a
/// GPU-tier requirement.
/// </summary>
public static class InferenceTierFeasibility
{
    /// <summary>
    /// True when <paramref name="tier"/> can be attempted on the hardware
    /// described by <paramref name="snapshot"/>. <see cref="InferenceTier.Tier0DeterministicRules"/>
    /// is always true — Safety/Guard/recovery must never depend on a
    /// hardware probe having succeeded.
    /// </summary>
    public static bool CanRun(InferenceTier tier, HardwareCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return tier switch
        {
            InferenceTier.Tier0DeterministicRules => true,
            InferenceTier.Tier1LightweightLocalMl => CanRunTier1(snapshot),
            InferenceTier.Tier2GpuAcceleratedVision => CanRunTier2(snapshot),
            InferenceTier.Tier3ExpensiveLocalReasoning => CanRunTier3(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unrecognized InferenceTier value.")
        };
    }

    private static bool CanRunTier1(HardwareCapabilitySnapshot snapshot) =>
        AtLeast(snapshot.Cpu.LogicalCores, InferenceTierThresholds.Tier1MinLogicalCores)
        && AtLeast(snapshot.Memory.TotalRamMb, InferenceTierThresholds.Tier1MinTotalRamMb)
        && ThrottleAtMost(snapshot.Thermal.ThrottleState, InferenceTierThresholds.Tier1MaxThrottleState);

    private static bool CanRunTier2(HardwareCapabilitySnapshot snapshot) =>
        Known(snapshot.Gpu.Model)
        && AtLeast(snapshot.Gpu.TotalVramMb, InferenceTierThresholds.Tier2MinTotalVramMb)
        && AtLeast(snapshot.Gpu.FreeVramMb, InferenceTierThresholds.Tier2MinFreeVramMb)
        && AtLeast(snapshot.Memory.TotalRamMb, InferenceTierThresholds.Tier2MinTotalRamMb)
        && ThrottleAtMost(snapshot.Thermal.ThrottleState, InferenceTierThresholds.Tier2MaxThrottleState);

    private static bool CanRunTier3(HardwareCapabilitySnapshot snapshot)
    {
        if (!ThrottleAtMost(snapshot.Thermal.ThrottleState, InferenceTierThresholds.Tier3MaxThrottleState))
            return false;

        if (!AtLeast(snapshot.Memory.TotalRamMb, InferenceTierThresholds.Tier3MinTotalRamMb))
            return false;

        var gpuPath = Known(snapshot.Gpu.Model)
            && AtLeast(snapshot.Gpu.TotalVramMb, InferenceTierThresholds.Tier3GpuPathMinTotalVramMb)
            && AtLeast(snapshot.Gpu.FreeVramMb, InferenceTierThresholds.Tier3GpuPathMinFreeVramMb);

        var cpuFallbackPath =
            AtLeast(snapshot.Cpu.LogicalCores, InferenceTierThresholds.Tier3CpuFallbackMinLogicalCores)
            && AtLeast(snapshot.Memory.AvailableRamMb, InferenceTierThresholds.Tier3CpuFallbackMinAvailableRamMb);

        return gpuPath || cpuFallbackPath;
    }

    private static bool Known<T>(ClassifiedValue<T> value) => value.HasValue;

    private static bool AtLeast<T>(ClassifiedValue<T> value, T minimum) where T : IComparable<T>
        => value.HasValue && value.Value.CompareTo(minimum) >= 0;

    private static bool ThrottleAtMost(ClassifiedValue<HardwareThrottleState> value, HardwareThrottleState maximum)
        => value.HasValue && value.Value <= maximum;
}
