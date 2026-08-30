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
    /// <summary>
    /// Builds the runtime graph under <see cref="RuntimeSafetyPolicy.SafeDefault"/>,
    /// which keeps live input disabled.
    /// </summary>
    public static RuntimeComponents CreateSafe()
    {
        var policy = RuntimeSafetyPolicy.SafeDefault;
        // The raw Win32 backend is never handed out directly: it is wrapped so
        // that a consumer holding RuntimeComponents.InputBackend or .Humanizer
        // still cannot inject input while the policy forbids it.
        var input = new GatedInputBackend(new Win32InputBackend(), policy);
        var humanizer = new DeterministicHumanizer(input);
        var guard = new GuardAi();
        var safety = new SafetyGate();

        return new RuntimeComponents(policy, input, humanizer, guard, safety);
    }

    /// <summary>
    /// Builds the graph against an explicit policy and input backend. Used by the
    /// certification suites to exercise the authorised path without touching the
    /// real desktop.
    /// </summary>
    public static RuntimeComponents Create(RuntimeSafetyPolicy policy, IInputBackend rawInput)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(rawInput);
        var input = new GatedInputBackend(rawInput, policy);
        return new RuntimeComponents(policy, input, new DeterministicHumanizer(input), new GuardAi(), new SafetyGate());
    }
}

public sealed record RuntimeComponents(
    RuntimeSafetyPolicy SafetyPolicy,
    IInputBackend InputBackend,
    IHumanizer Humanizer,
    IGuardAi GuardAi,
    ISafetyGate SafetyGate);
