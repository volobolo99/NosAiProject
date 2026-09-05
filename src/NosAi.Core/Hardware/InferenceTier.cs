namespace NosAi.Core.Hardware;

/// <summary>
/// The four resource-aware AI policy tiers from docs/ROADMAP_ESECUTIVA.md
/// section 5 ("Resource-aware AI policy") and
/// docs/NOSAI_ARCHITECTURE_BASELINE.md section 6 ("AI execution policy").
///
/// This is a distinct taxonomy from the ADR-0022 cognitive-loop split
/// (reflex/tactical/strategic/reflective): that classification is about
/// *when in the decision cycle* a computation runs, while
/// <see cref="InferenceTier"/> is about *how expensive/hardware-hungry* a
/// computation is. The two are orthogonal — e.g. a reflex-loop check can
/// still be Tier 0, and a strategic-loop computation can be Tier 3 — and
/// must not be conflated.
///
/// The runtime always prefers the lowest tier that meets the required
/// confidence and deadline for a job (ROADMAP_ESECUTIVA.md S:5). Tier
/// numbers are ordered by cost/resource requirement, not by decision
/// importance: Tier 0 work (Safety/Guard/recovery) is the most important
/// work in the system despite being the cheapest to run.
/// </summary>
public enum InferenceTier
{
    /// <summary>
    /// Tier 0 — deterministic rules, geometry and cached knowledge. No model
    /// inference, no GPU, no meaningful CPU/RAM cost beyond ordinary
    /// application logic. Always executable regardless of the detected
    /// hardware capabilities, including when those capabilities are
    /// entirely <see cref="DataSourceKind.Unknown"/>. Safety, Guard, Trust
    /// and recovery/watchdog logic MUST live at this tier so they are never
    /// starved by a hardware budget (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md
    /// S:3 "Real-time tier").
    /// </summary>
    Tier0DeterministicRules = 0,

    /// <summary>
    /// Tier 1 — lightweight local ML inference: small CPU-bound classical ML
    /// models or tiny quantized networks. Cheap enough to run without a
    /// dedicated accelerator, but still consumes measurable CPU/RAM and must
    /// respect the RAM budget on a 16 GB baseline
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:5).
    /// </summary>
    Tier1LightweightLocalMl = 1,

    /// <summary>
    /// Tier 2 — GPU-accelerated vision/embeddings: object detection,
    /// tracking, OCR acceleration, map feature extraction, embeddings
    /// (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:3 "Interactive AI tier").
    /// Requires a known, sufficiently capable GPU with free VRAM headroom;
    /// never assumed present on an 8 GB-class laptop GPU shared with the
    /// game client.
    /// </summary>
    Tier2GpuAcceleratedVision = 2,

    /// <summary>
    /// Tier 3 — expensive local reasoning / heavy model inference. The
    /// highest resource cost and latency in the taxonomy, and therefore the
    /// last tier the scheduler ever selects. Advisory only: a Tier 3 result
    /// never gains direct execution authority (docs/ROADMAP_ESECUTIVA.md
    /// S:3), and a pending or slow Tier 3 computation MUST NEVER block
    /// Safety, Guard or recovery execution (docs/ROADMAP_ESECUTIVA.md S:5 —
    /// "Tier 3 non può bloccare Safety/recovery"; identical requirement in
    /// docs/NOSAI_ARCHITECTURE_BASELINE.md S:6).
    /// </summary>
    Tier3ExpensiveLocalReasoning = 3
}
