using System.Diagnostics;

namespace NosAi.Runtime.Security;

public static class ExecutionIntegrity
{
    public static bool Verify()
        => !Debugger.IsAttached;

    public static int ApplyTimingJitter(int baseDelayMs, Random? random = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseDelayMs);
        random ??= Random.Shared;
        return Math.Max(50, baseDelayMs + random.Next(-20, 35));
    }
}
