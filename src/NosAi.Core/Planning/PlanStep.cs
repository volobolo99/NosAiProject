using System.Runtime.InteropServices;

namespace NosAi.Core.Planning;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct PlanStep
{
    public readonly ushort ActionId;
    public readonly uint TargetEntityId;
    public readonly ushort DelayMs;
    public readonly ushort TimeoutMs;
    public readonly uint RequiredScope;

    public PlanStep(ushort actionId, uint targetEntityId, ushort delayMs, ushort timeoutMs, uint requiredScope)
    {
        ActionId = actionId;
        TargetEntityId = targetEntityId;
        DelayMs = delayMs;
        TimeoutMs = timeoutMs;
        RequiredScope = requiredScope;
    }
}

public readonly record struct ActionIntent(
    ReadOnlyMemory<PlanStep> Steps,
    long DeadlineUnixMs,
    uint RequiredScope,
    byte MinimumTrustTier)
{
    public static ActionIntent Empty => new(ReadOnlyMemory<PlanStep>.Empty, 0, 0, 0);
}
