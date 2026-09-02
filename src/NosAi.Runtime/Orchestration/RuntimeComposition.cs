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
    /// Builds the runtime graph with the safety switches under operator control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policy used to be a fixed <c>SafeDefault</c> captured at construction, so
    /// nothing could turn live input on later — the operator had a refusal, not a
    /// switch. The graph is now built around a <see cref="RuntimeSafetyController"/>
    /// and every consumer reads the state in force at the moment it acts.
    /// </para>
    /// <para>
    /// It still starts with everything off. A runtime that came up already able to
    /// inject input would act before the operator had decided it should, and the
    /// switch is one call away.
    /// </para>
    /// </remarks>
    public static RuntimeComponents CreateSafe(RuntimeSafetyController? controller = null)
    {
        var safety = controller ?? new RuntimeSafetyController();

        // The raw Win32 backend is never handed out directly: it is wrapped so that
        // a consumer holding RuntimeComponents.InputBackend or .Humanizer still
        // cannot inject input while the policy forbids it. The policy is read per
        // call, so flipping the switch takes effect immediately and so does
        // flipping it back — which is what makes an emergency stop worth having.
        //
        // The commit point is a construction-time choice, not a runtime switch:
        // a switch is what a bypass looks like. The monitor is wired here and
        // started by the host; until it is watching, every irreversible act
        // refuses with commit_human_input_unknown rather than proceeding on
        // evidence nobody gathered.
        var humanInput = new HumanInputMonitor();
        var commitPoint = new CommitPointValidator(new Win32CommitEnvironment(), humanInput);
        var input = new GatedInputBackend(new Win32InputBackend(), () => safety.Policy, commitPoint);
        var humanizer = new DeterministicHumanizer(input);

        return new RuntimeComponents(safety, input, humanizer, new GuardAi(), new SafetyGate(), humanInput);
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

        var safety = new RuntimeSafetyController(policy);
        var input = new GatedInputBackend(rawInput, () => safety.Policy);
        return new RuntimeComponents(
            safety, input, new DeterministicHumanizer(input), new GuardAi(), new SafetyGate(),
            NotWatchingHumanInput.Instance);
    }
}

/// <summary>The composed runtime, with its live safety state.</summary>
public sealed record RuntimeComponents(
    RuntimeSafetyController Safety,
    IInputBackend InputBackend,
    IHumanizer Humanizer,
    IGuardAi GuardAi,
    ISafetyGate SafetyGate,
    IHumanInputMonitor HumanInput)
{
    /// <summary>
    /// The safety state in force right now.
    /// </summary>
    /// <remarks>
    /// A property rather than a stored value on purpose: a snapshot that captured
    /// the policy once would keep reporting the state at startup while the operator
    /// had since changed it, which is the sort of stale safety label this project
    /// treats as a defect.
    /// </remarks>
    public RuntimeSafetyPolicy SafetyPolicy => Safety.Policy;
}
