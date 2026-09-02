using System.Diagnostics;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate3;

/// <summary>
/// Turns a game map coordinate into a pixel of the client window.
/// </summary>
/// <remarks>
/// <para>
/// The seam F2-3 fills. The transform depends on the resolution, the zoom and
/// the client's isometric projection, so it has to be calibrated against a real
/// client rather than derived — and until it is, this returns <i>I do not know</i>
/// and the effector refuses every action that needs a point on the screen.
/// </para>
/// <para>
/// A fallback transform would click somewhere in the window, which is worse than
/// not clicking: the cycle would only discover it at verification, after having
/// already acted.
/// </para>
/// </remarks>
public interface IScreenProjection
{
    /// <summary>
    /// Projects a map coordinate, or says why it cannot.
    /// </summary>
    /// <param name="failureReason">
    /// Non-null exactly when this returns false. A point outside the client area
    /// is a refusal with its own reason, not a click on the border.
    /// </param>
    bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason);

    /// <summary>
    /// The window geometry the transform was fitted under.
    /// </summary>
    /// <remarks>
    /// Carried so the commit point's fifth condition compares the scale the coordinate
    /// was <i>computed</i> under against the scale that is live now. Re-reading the live
    /// window for both sides would make that condition compare a value with itself,
    /// which is the one way to turn a guard into decoration. A projection that has not
    /// been calibrated has no scale, and <see cref="GeometryShape.IsKnown"/> says so
    /// rather than reporting zeroes that would compare unequal for the wrong reason.
    /// </remarks>
    GeometryShape Scale { get; }
}

/// <summary>The only projection that exists until F2-3 is calibrated: it refuses.</summary>
public sealed class UncalibratedScreenProjection : IScreenProjection
{
    /// <summary>Reported until the operator has calibrated the transform.</summary>
    public const string NotCalibratedReason = "screen_projection_not_calibrated";

    public static UncalibratedScreenProjection Instance { get; } = new();

    /// <summary>Unknown: there is no fit, so there is no geometry it was fitted under.</summary>
    public GeometryShape Scale => default;

    public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
    {
        screenX = 0;
        screenY = 0;
        failureReason = NotCalibratedReason;
        return false;
    }
}

/// <summary>
/// Applies an authorised action to the real client, as keystrokes and clicks.
/// </summary>
/// <remarks>
/// <para>
/// B4, the missing link. <see cref="Win32InputBackend"/> could reach the OS and
/// <see cref="GatedInputBackend"/> could refuse to, but nothing translated an
/// <see cref="ActionCandidate"/> into a gesture, so Gate 3 planned and never
/// acted.
/// </para>
/// <para>
/// <b>Completed means the input was accepted.</b> <c>SendInput</c> reports how
/// many events it queued and <see cref="IInputBackend"/> returns false when that
/// is not what was asked for; this reports <see cref="ExecutionState.Failed"/>
/// with a reason rather than success. The defect that made Gate 3 unable to tell
/// the truth was a <c>Completed</c> with no execution behind it
/// (<c>docs/GATE3_PIPELINE.md</c>), and it is not being reintroduced from this
/// side.
/// </para>
/// <para>
/// <b>Every missing ingredient is a named refusal.</b> No default key, no
/// fallback transform, no guessed position. The reason names what to configure,
/// so <c>keybind_not_configured:consumable.101</c> is the intent the operator has
/// to add rather than a generic failure.
/// </para>
/// <para>
/// <b>The gate is not optional.</b> The constructor takes
/// <see cref="GatedInputBackend"/> concretely, not <see cref="IInputBackend"/>,
/// so an effector cannot be built over a raw <see cref="Win32InputBackend"/> and
/// step around the Safety Gate. The gate sits at the boundary precisely so it
/// cannot be walked around (ADR-0003), and that is what
/// <see cref="GatedInputBackend"/> was written for.
/// </para>
/// </remarks>
public sealed class InputActionEffector : IActionEffector
{
    /// <summary>
    /// Where the operator's keybinds live, relative to the repository root.
    /// </summary>
    /// <remarks>
    /// Beside the glyph atlas and the target-frame calibration, in gitignored
    /// <c>data/</c>: which key means which intention is a fact about one person's
    /// quickbar, and a committed copy would be somebody else's.
    /// </remarks>
    public const string KeybindsRelativePath = "data/keybinds.json";

    /// <summary>
    /// Held down long enough for the client to register the key. Not tuned
    /// against the real client; F5-2's sequences are what will confirm it.
    /// </summary>
    private const int KeyPressMs = 80;

    private readonly GatedInputBackend _input;
    private readonly KeybindMap _keybinds;
    private readonly IScreenProjection _projection;
    private readonly Func<RuntimeSafetyPolicy> _policySource;
    private readonly Func<string?>? _sessionAuthority;

    /// <param name="input">
    /// The gated boundary. Concrete on purpose: see the class remarks.
    /// </param>
    /// <param name="keybinds">
    /// Which key means which intention, per the operator. An unconfigured intent
    /// has no default (C3/F2-4) and becomes a named refusal here.
    /// </param>
    /// <param name="policySource">
    /// Read on every call so a policy flipped at run time is obeyed at once, and
    /// so a refusal by policy reports as <see cref="ExecutionState.Disabled"/>
    /// rather than as a failure. The gate still enforces; this only reports.
    /// </param>
    /// <param name="projection">
    /// Map coordinates to screen pixels. Defaults to the uncalibrated one, which
    /// refuses — the safe default until F2-3 exists.
    /// </param>
    /// <param name="sessionAuthority">
    /// Why this session cannot be acted on, or null when it can
    /// (<see cref="SessionActuationAuthority.CurrentRefusal"/>). A pure read: it is
    /// consulted on every question and never probes, so asking whether the capability
    /// exists cannot itself emit input. Null leaves the effector answering on the
    /// policy alone, which is what the certification suites want against a recording
    /// backend where there is no session to bind to.
    /// </param>
    public InputActionEffector(
        GatedInputBackend input,
        KeybindMap keybinds,
        Func<RuntimeSafetyPolicy> policySource,
        IScreenProjection? projection = null,
        Func<string?>? sessionAuthority = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _keybinds = keybinds ?? throw new ArgumentNullException(nameof(keybinds));
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
        _projection = projection ?? UncalibratedScreenProjection.Instance;
        _sessionAuthority = sessionAuthority;
    }

    /// <inheritdoc />
    public bool CanApply => UnavailableReason is null;

    /// <summary>
    /// Why the decision level is not being offered actuation, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent reasons, and the order between them is not arbitrary: the policy
    /// is the operator's switch and answers first, because "you have not armed this" is
    /// the more useful thing to be told when both are true.
    /// </para>
    /// <para>
    /// The second is § 4's rule. A session this runtime cannot drive exposes <i>no</i>
    /// actuation capability — not a capability that fails on use — so the planner never
    /// selects an action it cannot carry out, and the failure stops looking like a
    /// client that is not responding. Observation is untouched.
    /// </para>
    /// </remarks>
    public string? UnavailableReason =>
        !_policySource().LiveInputEnabled ? "live_input_disabled_by_policy" : _sessionAuthority?.Invoke();

    /// <inheritdoc />
    public Task<ExecutionResult> ApplyAsync(
        ActionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing is attempted while the policy is closed, and it reports as
        // suppressed rather than failed: the pipeline treats the cycle as not
        // executed, which is the difference the safe posture depends on.
        if (!CanApply)
            return Task.FromResult(Result(candidate, ExecutionState.Disabled, 0, UnavailableReason));

        var clock = Stopwatch.StartNew();
        ExecutionResult result = candidate.Type switch
        {
            // The slot is the target for a consumable, so the intent names the
            // slot the operator configured rather than the item id, which is a
            // catalogue number and not something on their quickbar.
            ActionType.UseConsumable => candidate.Target is ActionTarget.InventorySlot slot
                ? PressKey(candidate, $"consumable.{slot.Slot}", clock)
                : Result(candidate, ExecutionState.Refused, clock, "target_slot_unknown"),

            ActionType.UseSkill => PressKey(candidate, $"skill.{candidate.SkillOrItemId}", clock),

            ActionType.UseBasicAttack or ActionType.TargetEntity
                or ActionType.MoveToPosition or ActionType.EmergencyFlee
                => ClickPoint(candidate, clock),

            // Named rather than silently unhandled. Neither has a gesture yet, and
            // an effector that quietly did nothing for them would report a cycle
            // as executed with nothing behind it.
            ActionType.CollectGroundItem or ActionType.RestAndRecover
                => Result(candidate, ExecutionState.Refused, clock, $"action_not_implemented:{candidate.Type}"),

            _ => Result(candidate, ExecutionState.Refused, clock, $"action_type_not_executable:{candidate.Type}"),
        };

        return Task.FromResult(result);
    }

    /// <summary>Presses the key the operator bound to an intention.</summary>
    private ExecutionResult PressKey(ActionCandidate candidate, string intent, Stopwatch clock)
    {
        // No default key. "The potion is on 1" would press some key during a real
        // fight, which is the reason C3 refuses to invent one.
        if (!_keybinds.TryGet(intent, out Keybind bind))
            return Result(candidate, ExecutionState.Refused, clock, $"keybind_not_configured:{intent}");

        bool accepted = _input.KeyPress(bind.VirtualKey, KeyPressMs);

        return accepted
            ? Result(candidate, ExecutionState.Completed, clock, null)
            : Result(candidate, ExecutionState.Failed, clock, $"input_not_accepted:{intent}");
    }

    /// <summary>Moves the cursor to the projected point and clicks it.</summary>
    private ExecutionResult ClickPoint(ActionCandidate candidate, Stopwatch clock)
    {
        if (!TryTargetPoint(candidate, out int mapX, out int mapY, out string? positionReason))
            return Result(candidate, ExecutionState.Refused, clock, positionReason);

        if (!_projection.TryProject(mapX, mapY, out int screenX, out int screenY, out string? projectionReason))
            return Result(candidate, ExecutionState.Refused, clock, projectionReason ?? "screen_projection_failed");

        // The move has to be accepted before the click, or the click lands
        // wherever the cursor happened to be — a real gesture at the wrong point,
        // which is the failure mode this whole path is written to avoid.
        if (!_input.MoveAbsolute(screenX, screenY))
            return Result(candidate, ExecutionState.Failed, clock, "input_not_accepted:cursor_move");

        return _input.Click(MouseButton.Left)
            ? Result(candidate, ExecutionState.Completed, clock, null)
            : Result(candidate, ExecutionState.Failed, clock, "input_not_accepted:click");
    }

    /// <summary>
    /// The map coordinate this action is aimed at, or why there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The target says this itself now. It used to be two loose integers where
    /// <c>0,0</c> stood for "no position", which is also a real corner of the map
    /// — the ambiguity F2-1 removed.
    /// </para>
    /// <para>
    /// An entity nobody has identified is refused by name rather than clicked at
    /// wherever a placeholder would have pointed. The planner knows <i>that</i>
    /// there is a target and not <i>which</i> until F2-2 picks the nearest
    /// observed sighting, and acting on the difference is the mistake this whole
    /// card exists to prevent.
    /// </para>
    /// </remarks>
    private static bool TryTargetPoint(
        ActionCandidate candidate, out int mapX, out int mapY, out string? failureReason)
    {
        mapX = 0;
        mapY = 0;

        switch (candidate.Target)
        {
            case ActionTarget.Position position:
                mapX = position.At.X;
                mapY = position.At.Y;
                failureReason = null;
                return true;

            case ActionTarget.Entity { IsResolved: false }:
                failureReason = "target_entity_unresolved";
                return false;

            case ActionTarget.Entity { At: null }:
                failureReason = "target_position_unknown";
                return false;

            case ActionTarget.Entity { At: { } at }:
                mapX = at.X;
                mapY = at.Y;
                failureReason = null;
                return true;

            default:
                failureReason = "target_position_unknown";
                return false;
        }
    }

    private static ExecutionResult Result(
        ActionCandidate candidate, ExecutionState state, Stopwatch clock, string? reason)
        => Result(candidate, state, (int)clock.ElapsedMilliseconds, reason);

    private static ExecutionResult Result(
        ActionCandidate candidate, ExecutionState state, int durationMs, string? reason)
        => new(candidate.CandidateId, state, durationMs, reason);
}
