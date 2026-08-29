using NosAi.Runtime.Adapters;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Humanizer;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Orchestration;

/// <summary>Single composition root for the C# runtime dependency graph.</summary>
public static class RuntimeComposition
{
    public static RuntimeComponents CreateSafe()
    {
        var policy = RuntimeSafetyPolicy.SafeDefault;
        var input = new Win32InputBackend();
        var humanizer = new DeterministicHumanizer(input);
        var guard = new GuardAi();
        var safety = new SafetyGate();

        return new RuntimeComponents(policy, input, humanizer, guard, safety);
    }
}

public sealed record RuntimeComponents(
    RuntimeSafetyPolicy SafetyPolicy,
    IInputBackend InputBackend,
    IHumanizer Humanizer,
    IGuardAi GuardAi,
    ISafetyGate SafetyGate);
