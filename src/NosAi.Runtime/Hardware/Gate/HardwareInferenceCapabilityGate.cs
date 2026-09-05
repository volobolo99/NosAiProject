using CoreHardware = NosAi.Core.Hardware;

// See the namespace note at the top of RuntimeHardwareCapabilityProvider.cs: this
// file also lives in the new Hardware/Gate/ subfolder but declares the existing,
// already-declared-Integrated NosAi.Runtime.Hardware namespace rather than a new
// one, so it needs no change to the module-reachability manifest.
namespace NosAi.Runtime.Hardware;

/// <summary>
/// Runtime-facing capability gate: answers "are there resources to even attempt
/// this <see cref="CoreHardware.InferenceTier"/> right now", built directly on top
/// of <see cref="CoreHardware.IHardwareCapabilityProvider"/> and
/// <see cref="CoreHardware.InferenceTierFeasibility.CanRun"/>.
///
/// Scope boundary (see AP-00/A4 command,
/// docs/agents/phases/AP-00/A4_CURSOR_runtime_gate.md, and CLAUDE.md's product
/// boundary section): this gate decides only hardware feasibility for an
/// inference tier. It never decides whether a gameplay action is authorized —
/// that is Guard/Trust/Safety's job, unmodified and untouched by this type. A
/// future consumer (Gate3, Perception) is expected to call
/// <see cref="TryAuthorize"/> before scheduling Tier 1-3 work and to treat a
/// refusal as "fall back to a cheaper tier or Tier 0", never as a signal about
/// gameplay authorization.
/// </summary>
public interface IHardwareInferenceCapabilityGate
{
    /// <summary>
    /// True when <paramref name="tier"/> can be attempted on the hardware the
    /// underlying <see cref="CoreHardware.IHardwareCapabilityProvider"/> currently
    /// reports. When false, <paramref name="refusalReason"/> carries a
    /// machine-readable, non-null explanation; when true it is null.
    /// </summary>
    bool TryAuthorize(CoreHardware.InferenceTier tier, out string? refusalReason);
}

/// <summary>
/// Default <see cref="IHardwareInferenceCapabilityGate"/> implementation. Deliberately
/// thin: every actual feasibility rule lives in
/// <see cref="CoreHardware.InferenceTierFeasibility"/> (owned by A1) and is called,
/// never re-implemented, here — the AP-00/A4 test requirement is explicit that this
/// type must not duplicate that logic.
///
/// Fail-closed contract:
/// <list type="bullet">
/// <item>If <see cref="CoreHardware.IHardwareCapabilityProvider.GetSnapshot"/> throws,
/// the exception never reaches the caller: it is treated exactly like a snapshot the
/// provider legitimately reported as fully
/// <see cref="CoreHardware.DataSourceKind.Unknown"/>, and evaluated through the same
/// <see cref="CoreHardware.InferenceTierFeasibility.CanRun"/> call as every other
/// snapshot.</item>
/// <item>A fully-Unknown snapshot (thrown, provided, or literally
/// <see cref="CoreHardware.HardwareCapabilitySnapshot.Unknown"/>) can never satisfy
/// Tier 1, 2 or 3 — <see cref="CoreHardware.InferenceTierFeasibility.CanRun"/> already
/// guarantees this ("Unknown is not zero, false or empty"), so this gate never
/// authorizes a heavier tier "by default" when the hardware picture is missing or
/// broken.</item>
/// <item>Tier 0 (deterministic rules) is authorized unconditionally, including on a
/// provider exception. This is not a special case added by this gate: it is
/// <see cref="CoreHardware.InferenceTierFeasibility.CanRun"/>'s own unconditional
/// behaviour for Tier 0 (see its XML doc — "Safety/Guard/recovery must never depend
/// on a hardware probe having succeeded"), reached here through the exact same
/// Unknown-snapshot path as every other tier rather than a bypass. This gate does not
/// grant, and cannot grant, any gameplay or Safety authority by itself.</item>
/// </list>
/// </summary>
public sealed class HardwareInferenceCapabilityGate : IHardwareInferenceCapabilityGate
{
    private readonly CoreHardware.IHardwareCapabilityProvider _provider;

    public HardwareInferenceCapabilityGate(CoreHardware.IHardwareCapabilityProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <inheritdoc />
    public bool TryAuthorize(CoreHardware.InferenceTier tier, out string? refusalReason)
    {
        var snapshot = CaptureSnapshotFailClosed();

        bool canRun;
        try
        {
            canRun = CoreHardware.InferenceTierFeasibility.CanRun(tier, snapshot);
        }
        catch (ArgumentOutOfRangeException)
        {
            // CanRun throws only for a tier value outside the known enum range. That
            // is a caller/programming defect, not a hardware condition, but this gate
            // still fails closed rather than letting the exception escape into a
            // scheduler that might not expect it.
            refusalReason = $"unrecognized_inference_tier:{tier}";
            return false;
        }

        if (canRun)
        {
            refusalReason = null;
            return true;
        }

        refusalReason = DescribeRefusal(tier, snapshot);
        return false;
    }

    private CoreHardware.HardwareCapabilitySnapshot CaptureSnapshotFailClosed()
    {
        try
        {
            return _provider.GetSnapshot()
                ?? CoreHardware.HardwareCapabilitySnapshot.Unknown("hardware_capability_provider_returned_null");
        }
        catch (Exception ex)
        {
            return CoreHardware.HardwareCapabilitySnapshot.Unknown(
                $"hardware_capability_provider_threw:{ex.GetType().Name}:{ex.Message}");
        }
    }

    /// <summary>
    /// Builds a diagnosable refusal reason without re-deriving
    /// <see cref="CoreHardware.InferenceTierFeasibility"/>'s numeric/thermal
    /// thresholds: it only names which of the fields that tier's feasibility check
    /// reads (per <see cref="CoreHardware.InferenceTierFeasibility"/>'s own
    /// implementation and XML docs) are Unknown right now, carrying each field's own
    /// <see cref="CoreHardware.ClassifiedValue{T}.FailureReason"/>. When every
    /// relevant field is known and the refusal still happened, the numeric/thermal
    /// threshold itself (owned by A1, not this gate) is what refused it.
    /// </summary>
    private static string DescribeRefusal(CoreHardware.InferenceTier tier, CoreHardware.HardwareCapabilitySnapshot snapshot)
    {
        var unknownFields = new List<string>();

        void NoteIfUnknown<T>(CoreHardware.ClassifiedValue<T> value, string label)
        {
            if (!value.HasValue)
                unknownFields.Add($"{label}={value.FailureReason ?? "unknown"}");
        }

        switch (tier)
        {
            case CoreHardware.InferenceTier.Tier0DeterministicRules:
                // InferenceTierFeasibility.CanRun never refuses Tier 0, so this branch
                // is unreachable in practice; it is kept only so the switch is total
                // and does not silently fall through to "below threshold" for a tier
                // that has no threshold at all.
                break;

            case CoreHardware.InferenceTier.Tier1LightweightLocalMl:
                NoteIfUnknown(snapshot.Cpu.LogicalCores, "cpu.logicalCores");
                NoteIfUnknown(snapshot.Memory.TotalRamMb, "memory.totalRamMb");
                NoteIfUnknown(snapshot.Thermal.ThrottleState, "thermal.throttleState");
                break;

            case CoreHardware.InferenceTier.Tier2GpuAcceleratedVision:
                NoteIfUnknown(snapshot.Gpu.Model, "gpu.model");
                NoteIfUnknown(snapshot.Gpu.TotalVramMb, "gpu.totalVramMb");
                NoteIfUnknown(snapshot.Gpu.FreeVramMb, "gpu.freeVramMb");
                NoteIfUnknown(snapshot.Memory.TotalRamMb, "memory.totalRamMb");
                NoteIfUnknown(snapshot.Thermal.ThrottleState, "thermal.throttleState");
                break;

            case CoreHardware.InferenceTier.Tier3ExpensiveLocalReasoning:
                NoteIfUnknown(snapshot.Thermal.ThrottleState, "thermal.throttleState");
                NoteIfUnknown(snapshot.Memory.TotalRamMb, "memory.totalRamMb");
                NoteIfUnknown(snapshot.Gpu.Model, "gpu.model");
                NoteIfUnknown(snapshot.Gpu.TotalVramMb, "gpu.totalVramMb");
                NoteIfUnknown(snapshot.Gpu.FreeVramMb, "gpu.freeVramMb");
                NoteIfUnknown(snapshot.Cpu.LogicalCores, "cpu.logicalCores");
                NoteIfUnknown(snapshot.Memory.AvailableRamMb, "memory.availableRamMb");
                break;
        }

        return unknownFields.Count > 0
            ? $"tier_{(int)tier}_blocked_by_unknown_capability:{string.Join(',', unknownFields)}"
            : $"tier_{(int)tier}_below_required_hardware_threshold";
    }
}
