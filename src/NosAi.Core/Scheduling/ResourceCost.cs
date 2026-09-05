namespace NosAi.Core.Scheduling;

/// <summary>
/// A CPU/GPU/RAM/VRAM quantity: the same 4-dimensional shape is used both for
/// a job's declared estimated cost (docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md
/// S:8: "Each inference job declares ... estimated CPU/GPU/RAM/VRAM cost")
/// and for a live resource budget/ledger balance. CPU/GPU are estimated
/// compute time in milliseconds; RAM/VRAM are estimated resident bytes.
///
/// This type intentionally performs no validation and allows negative
/// components: as a *cost* (what a job needs) a negative component is a
/// caller bug and is rejected where costs are declared (see
/// <see cref="InferenceJob"/> and <see cref="ResourceBudgetLedger"/>), but as
/// a *ledger headroom* (capacity minus committed) a negative component is a
/// legitimate, meaningful state -- it is exactly how "the budget no longer
/// covers what is already committed" (e.g. a thermal/power throttle event,
/// docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md S:9) is represented, without
/// throwing on a perfectly normal degraded-operation condition.
/// </summary>
public readonly record struct ResourceCost(long CpuMillis, long GpuMillis, long RamBytes, long VramBytes)
{
    /// <summary>No cost / no committed resources.</summary>
    public static readonly ResourceCost Zero = default;

    /// <summary>True when every component is zero or positive -- required of a *declared* cost or capacity, not of a ledger headroom value (see remarks on the type).</summary>
    public bool IsNonNegative => CpuMillis >= 0 && GpuMillis >= 0 && RamBytes >= 0 && VramBytes >= 0;

    /// <summary>
    /// True when this cost fits within <paramref name="budget"/> component-wise.
    /// Used both to gate admission (<see cref="InferenceBudgetScheduler"/>) and
    /// to filter candidates during selection (<see cref="LowestCostJobSelector"/>).
    /// </summary>
    public bool IsWithin(ResourceCost budget) =>
        CpuMillis <= budget.CpuMillis &&
        GpuMillis <= budget.GpuMillis &&
        RamBytes <= budget.RamBytes &&
        VramBytes <= budget.VramBytes;

    public ResourceCost Add(ResourceCost other) => new(
        CpuMillis + other.CpuMillis,
        GpuMillis + other.GpuMillis,
        RamBytes + other.RamBytes,
        VramBytes + other.VramBytes);

    public ResourceCost Subtract(ResourceCost other) => new(
        CpuMillis - other.CpuMillis,
        GpuMillis - other.GpuMillis,
        RamBytes - other.RamBytes,
        VramBytes - other.VramBytes);

    public override string ToString() =>
        $"cpu={CpuMillis}ms gpu={GpuMillis}ms ram={RamBytes}B vram={VramBytes}B";
}
