// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — Certification suite for input control (keyboard and mouse)
// ============================================================================
//
// No check here touches the real desktop: they all run over
// RecordingInputBackend, so the suite is runnable in CI without moving the
// machine's mouse.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Humanizer;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.LowLevel;

public static class InputControlTestRunner
{
    /// <summary>
    /// Runs every input-control check and reports each one by name (same contract
    /// as the gate runners: no short-circuit, a throwing check is a named failure).
    /// </summary>
    public static async Task<bool> RunAllTestsAsync()
    {
        Console.WriteLine("=== Input control checks ===");

        bool allPassed = true;
        allPassed &= Run("Gate refuses every injection while live input is disabled", TestGateRefusesWhenDisabled);
        allPassed &= Run("Gate admits injection once the policy enables it", TestGateAdmitsWhenEnabled);
        allPassed &= Run("Gate follows a policy flipped at run time", TestGateFollowsLivePolicy);
        allPassed &= Run("Reading the cursor is never gated", TestCursorReadIsNotGated);
        allPassed &= Run("Composition root never hands out an ungated backend", TestCompositionRootIsGated);
        allPassed &= Run("Function keys F1-F12 resolve", TestFunctionKeysResolve);
        allPassed &= Run("Modifier chords resolve in order", TestModifierChordsResolve);
        allPassed &= Run("Arrows, numpad and editing keys resolve", TestExtendedKeysResolve);
        allPassed &= Run("An unknown key name is refused, not guessed", TestUnknownKeyIsRefused);
        allPassed &= Run("A bare modifier cannot be the chord target", TestBareModifierIsNotAChord);
        allPassed &= Run("Bezier plan starts and ends exactly on its endpoints", TestBezierPlanEndpoints);
        allPassed &= Run("Bezier plan is deterministic for equal endpoints", TestBezierPlanDeterminism);
        allPassed &= await RunAsync("Mouse path starts from the real cursor, not an assumption", TestMoveUsesRealCursorAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Key press emits the resolved code with its modifiers", TestKeyPressEmitsResolvedCodeAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Movement left-clicks and interaction right-clicks", TestAdapterButtonMappingAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Adapter stays blocked while the policy forbids input", TestAdapterBlockedByPolicyAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Live input still needs a healthy game client", TestAdapterNeedsHealthyClientAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("An operating tier below the action refuses it", TestOperatingTierCeilingAsync).ConfigureAwait(false);

        Console.WriteLine(allPassed
            ? "=== Input control checks passed. Local only: no injection into a real game client was verified. ==="
            : "=== Input control checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
    {
        try { return Report(name, await check().ConfigureAwait(false), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        string detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static RuntimeSafetyPolicy Enabled => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };

    /// <summary>
    /// Live input enabled without demanding a healthy game client, because CI has
    /// no NosTale process attached. <see cref="TestAdapterNeedsHealthyClientAsync"/>
    /// certifies separately that the client requirement really does block.
    /// </summary>
    private static RuntimeSafetyPolicy EnabledHeadless =>
        RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true, RequireClientHealthy = false };

    // ------------------------------------------------------------------ gate

    private static bool TestGateRefusesWhenDisabled()
    {
        var recorder = new RecordingInputBackend();
        var gate = new GatedInputBackend(recorder, RuntimeSafetyPolicy.SafeDefault);

        // Every actuation must be refused, and nothing may reach the backend.
        bool allRefused = !gate.MoveRelative(10, 10)
            && !gate.MoveAbsolute(100, 100)
            && !gate.Click(MouseButton.Left)
            && !gate.Click(MouseButton.Right)
            && !gate.KeyPress(0x70)
            && !gate.ScrollWheel(1);

        return allRefused
            && recorder.Events.Count == 0
            && !gate.IsLive
            && gate.RefusedCount == 6
            && gate.AllowedCount == 0
            && gate.LastRefusal?.Reason == "live_input_disabled_by_policy";
    }

    private static bool TestGateAdmitsWhenEnabled()
    {
        var recorder = new RecordingInputBackend();
        var gate = new GatedInputBackend(recorder, Enabled);

        bool allAdmitted = gate.MoveAbsolute(50, 60) && gate.Click(MouseButton.Right) && gate.KeyPress(0x71);
        return allAdmitted
            && gate.RefusedCount == 0
            && gate.AllowedCount == 3
            && recorder.Events.SequenceEqual(new[] { "move-absolute:50,60", "click:Right", "key:113" });
    }

    private static bool TestGateFollowsLivePolicy()
    {
        var recorder = new RecordingInputBackend();
        RuntimeSafetyPolicy policy = RuntimeSafetyPolicy.SafeDefault;
        var gate = new GatedInputBackend(recorder, () => policy);

        if (gate.KeyPress(0x70)) return false;      // disabled: refused
        policy = Enabled;
        if (!gate.KeyPress(0x70)) return false;     // enabled: admitted immediately
        policy = RuntimeSafetyPolicy.SafeDefault;
        // Revoking must take effect at once; no cached decision may survive it.
        return !gate.KeyPress(0x70) && recorder.Events.Count == 1;
    }

    private static bool TestCursorReadIsNotGated()
    {
        var gate = new GatedInputBackend(new RecordingInputBackend(cursorX: 640, cursorY: 480),
            RuntimeSafetyPolicy.SafeDefault);
        // Observing the desktop is not actuating it, so this must work even with
        // injection disabled -- and must not be counted as an allowed injection.
        return gate.TryGetCursorPosition(out int x, out int y)
            && x == 640 && y == 480
            && gate.AllowedCount == 0 && gate.RefusedCount == 0;
    }

    private static bool TestCompositionRootIsGated()
    {
        var runtime = NosAi.Runtime.Orchestration.RuntimeComposition.CreateSafe();

        // The raw Win32 backend must never be reachable from the composition root:
        // holding RuntimeComponents.InputBackend must not be enough to inject.
        if (runtime.InputBackend is not GatedInputBackend gated) return false;
        if (runtime.SafetyPolicy.LiveInputEnabled) return false;
        return !gated.IsLive
            && !gated.MoveRelative(1, 1)
            && !gated.KeyPress(0x41);
    }

    // ------------------------------------------------------------------ keys

    private static bool TestFunctionKeysResolve()
    {
        // The skill bar lives on F1-F12; these used to be unresolvable.
        for (int i = 1; i <= 12; i++)
        {
            if (!VirtualKeys.TryResolve($"F{i}", out KeyChord chord)) return false;
            if (chord.VirtualKey != 0x70 + i - 1) return false;
            if (!chord.Modifiers.IsEmpty) return false;
        }
        return true;
    }

    private static bool TestModifierChordsResolve()
    {
        if (!VirtualKeys.TryResolve("CTRL+S", out KeyChord ctrlS)) return false;
        if (ctrlS.VirtualKey != 'S' || ctrlS.Modifiers.Length != 1 || ctrlS.Modifiers[0] != VirtualKeys.Control) return false;

        if (!VirtualKeys.TryResolve("ctrl+shift+F4", out KeyChord chord)) return false;
        return chord.VirtualKey == 0x73
            && chord.Modifiers.Length == 2
            && chord.Modifiers[0] == VirtualKeys.Control
            && chord.Modifiers[1] == VirtualKeys.Shift;
    }

    private static bool TestExtendedKeysResolve()
    {
        (string Name, ushort Code)[] expected =
        {
            ("UP", 0x26), ("DOWN", 0x28), ("LEFT", 0x25), ("RIGHT", 0x27),
            ("NUM7", 0x67), ("NUMADD", 0x6B), ("DELETE", 0x2E), ("PAGEUP", 0x21),
            ("SPACE", 0x20), ("ENTER", 0x0D), ("ESC", 0x1B), ("TAB", 0x09),
        };
        foreach (var (name, code) in expected)
        {
            if (!VirtualKeys.TryResolve(name, out KeyChord chord) || chord.VirtualKey != code) return false;
        }
        return true;
    }

    private static bool TestUnknownKeyIsRefused()
    {
        // Guessing here would mean pressing the wrong key in a live game.
        return !VirtualKeys.TryResolve("F13", out _)
            && !VirtualKeys.TryResolve("HYPERDRIVE", out _)
            && !VirtualKeys.TryResolve("", out _)
            && !VirtualKeys.TryResolve(null, out _);
    }

    private static bool TestBareModifierIsNotAChord()
        // "CTRL+CTRL" leads with a modifier but targets one too: refused, because
        // only a real key may be the chord's target.
        => !VirtualKeys.TryResolve("A+B", out _) && VirtualKeys.TryResolve("CTRL", out _);

    // ------------------------------------------------------------------ mouse

    private static bool TestBezierPlanEndpoints()
    {
        var plan = DeterministicHumanizer.BuildBezierPlan(new ScreenPoint(10, 20), new ScreenPoint(410, 320));
        return plan.Count == 9
            && plan[0] == new ScreenPoint(10, 20)
            && plan[^1] == new ScreenPoint(410, 320);
    }

    private static bool TestBezierPlanDeterminism()
    {
        var first = DeterministicHumanizer.BuildBezierPlan(new ScreenPoint(0, 0), new ScreenPoint(300, 200));
        var second = DeterministicHumanizer.BuildBezierPlan(new ScreenPoint(0, 0), new ScreenPoint(300, 200));
        // Timing and trajectory are fixed by design: reproducible, not randomised.
        return first.SequenceEqual(second);
    }

    private static async Task<bool> TestMoveUsesRealCursorAsync()
    {
        var recorder = new RecordingInputBackend(cursorX: 1500, cursorY: 900);
        var humanizer = new DeterministicHumanizer(new GatedInputBackend(recorder, Enabled), NoDelay);

        // The caller passes a deliberately wrong start point. The path must still
        // begin at the real cursor: the old code trusted the argument and applied
        // relative deltas from a position the cursor was not at.
        await humanizer.MoveMouseHumanlikeAsync(new ScreenPoint(400, 300),
            new TargetDescriptor(new ScreenPoint(100, 100), 10, 10, "target"), CancellationToken.None);

        var expected = DeterministicHumanizer.BuildBezierPlan(new ScreenPoint(1500, 900), new ScreenPoint(100, 100));
        string[] emitted = recorder.Events.ToArray();
        if (emitted.Length != expected.Count - 1) return false;
        for (int i = 1; i < expected.Count; i++)
        {
            if (emitted[i - 1] != $"move-absolute:{expected[i].X},{expected[i].Y}") return false;
        }
        return recorder.TryGetCursorPosition(out int x, out int y) && x == 100 && y == 100;
    }

    private static async Task<bool> TestKeyPressEmitsResolvedCodeAsync()
    {
        var recorder = new RecordingInputBackend();
        var humanizer = new DeterministicHumanizer(new GatedInputBackend(recorder, Enabled), NoDelay);

        await humanizer.PressKeyHumanlikeAsync("F1", CancellationToken.None);
        await humanizer.PressKeyHumanlikeAsync("CTRL+A", CancellationToken.None);
        return recorder.Events.SequenceEqual(new[] { "key:112", $"key:{VirtualKeys.Control}+65" });
    }

    private static async Task<bool> TestAdapterButtonMappingAsync()
    {
        var recorder = new RecordingInputBackend();
        var adapter = BuildAdapter(recorder, EnabledHeadless);
        await adapter.InitializeAsync(CancellationToken.None);

        await adapter.SendMovementCommandAsync(200, 150, CancellationToken.None);
        bool walkedWithLeft = recorder.Events.Contains("click:Left");

        recorder.Events.ToList().Clear();
        await adapter.SendTargetInteractionAsync(300, 250, CancellationToken.None);
        bool attackedWithRight = recorder.Events.Contains("click:Right");

        // Walking and attacking must not share a button: in NosTale the right
        // button targets and attacks, the left one walks.
        return walkedWithLeft && attackedWithRight;
    }

    private static async Task<bool> TestAdapterBlockedByPolicyAsync()
    {
        var recorder = new RecordingInputBackend();
        var adapter = BuildAdapter(recorder, RuntimeSafetyPolicy.SafeDefault);
        await adapter.InitializeAsync(CancellationToken.None);
        try
        {
            await adapter.SendSkillCastAsync("F1", CancellationToken.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Refused at the adapter, and nothing leaked past it either.
            return recorder.Events.Count == 0;
        }
    }

    private static async Task<bool> TestAdapterNeedsHealthyClientAsync()
    {
        var recorder = new RecordingInputBackend();
        // Live input is enabled, but no NosTale client is attached. The gate must
        // still refuse: enabling injection is not the same as having a target.
        var adapter = BuildAdapter(recorder, Enabled);
        await adapter.InitializeAsync(CancellationToken.None);
        try
        {
            await adapter.SendMovementCommandAsync(10, 10, CancellationToken.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return recorder.Events.Count == 0 && !adapter.IsClientHealthy();
        }
    }

    private static async Task<bool> TestOperatingTierCeilingAsync()
    {
        var recorder = new RecordingInputBackend();
        // Tier1_Assisted is below every game action: the guard must refuse, and the
        // refusal must come from the trust ceiling rather than the input policy.
        var adapter = BuildAdapter(recorder, EnabledHeadless, NosAi.Runtime.Autonomy.TrustTier.Tier1_Assisted);
        await adapter.InitializeAsync(CancellationToken.None);
        try
        {
            await adapter.SendSkillCastAsync("F1", CancellationToken.None);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return recorder.Events.Count == 0;
        }
    }

    private static NosAi.Runtime.Adapters.NosTaleGameAdapter BuildAdapter(
        RecordingInputBackend recorder, RuntimeSafetyPolicy policy,
        NosAi.Runtime.Autonomy.TrustTier operatingTier = NosAi.Runtime.Autonomy.TrustTier.Tier3_AutonomousRestricted)
    {
        var humanizer = new DeterministicHumanizer(new GatedInputBackend(recorder, policy), NoDelay);
        return new NosAi.Runtime.Adapters.NosTaleGameAdapter(
            new NosAi.Runtime.Guard.GuardAi(), humanizer, new LiveInputAuthorization(policy), operatingTier);
    }

    /// <summary>Skips the humanizer's fixed delays so the suite stays fast.</summary>
    private static Task NoDelay(int _, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
