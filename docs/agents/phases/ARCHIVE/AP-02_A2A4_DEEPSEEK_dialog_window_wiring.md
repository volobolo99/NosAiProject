# AP-02 / A2+A4 — DeepSeek — Wire dialog-window detection into `ScreenVitalsCapture`/`Program.cs`

## Why this task exists

`AP-02_STATUS.md` §12 (Claude, A1+A3, this session) built a real,
tested, screen-only presence/absence detector for dialog-window panels:
`DialogRoiCalibration` (operator-confirmed ROI + empty-baseline B/G/R),
`DialogWindowReader` (pure delta-from-baseline), `DialogWindowStateComposer`
(`Compose(calibration, observation) -> ClassifiedValue<bool>`),
`ScreenDialogWindowSource` (screen half, same guard order as
`ScreenTargetFrameSource`). `VisualObservation` already carries the new
field this pipes into (`HasDialogWindow`, added this session alongside
the placeholder below). **This task is pure wiring, mirroring Q-067
exactly**: no new type, no new logic, just replacing an honest
placeholder with the real composition already proven to work in
isolation by the 38 tests in `DialogRoiCalibrationTests.cs`/
`DialogWindowReaderTests.cs`/`DialogWindowStateComposerTests.cs`/
`ScreenDialogWindowSourceTests.cs`.

**Current state, read before writing code.** `ScreenVitalsCapture.Capture()`
currently ends with:

```csharp
        ClassifiedValue<bool> hasTarget = TargetStateComposer.Compose(
            _targetCalibration, screenTarget, _wire?.LastPlayerAttackAtUtc);

        // Not wired in this pass -- DialogRoiCalibration/DialogWindowReader/
        // DialogWindowStateComposer/ScreenDialogWindowSource exist (AP-02/A1+A3)
        // but no caller here has composed them into a reading yet. Same honest
        // shape HasTarget itself used before Q-067's wiring: an explicit,
        // named reason, never a guessed true/false.
        ClassifiedValue<bool> hasDialogWindow = ClassifiedValue<bool>.Unknown("dialog_window_composer_not_wired_in_this_pass");

        return new VisualObservation(result, vitals, hasTarget, hasDialogWindow, frame.CapturedUtc);
```

This task replaces only the `hasDialogWindow` line and adds the
constructor plumbing a real `DialogRoiCalibration` needs to reach it —
the exact same shape `targetCalibration`/Q-067 already established one
field over.

## Already built and real — read before writing code

- `src/NosAi.Runtime/Perception/DialogRoiCalibration.cs`,
  `DialogWindowReader.cs`, `DialogWindowStateComposer.cs`,
  `ScreenDialogWindowSource.cs` (all already on `main`, Claude/A1+A3): read
  each in full, and their tests, before touching `ScreenVitalsCapture.cs`.
  Do not change any of these four files: they are pure and already covered.
- `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs`: read the whole
  file, especially how `_targetCalibration`/`_wire`/`targetFrames`/
  `screenTarget`/`hasTarget` are wired a few lines above the placeholder
  this task replaces — §1 below adds the dialog-window equivalent
  immediately next to it, reusing the same already-acquired `frame` via
  the existing private `SingleFrameSource` nested class (do not add a
  second one, do not change `SingleFrameSource` itself).
- `src/NosAi.Runtime/Program.cs`, lines ~789-807: read the existing
  `targetCalibration` loading block and how it feeds
  `new ScreenVitalsCapture(AttachedProcessId, targetCalibration: targetCalibration)`
  — §2 below adds the identical block one field over, for
  `DialogRoiCalibration`.
- `src/NosAi.Runtime/Perception/VisualObservation.cs`: already carries
  `HasDialogWindow` (added this session, right after `HasTarget`). Do
  not change this file — the field already exists, this task only
  populates it with a real value instead of the placeholder.

## OWN (new files only)

None. Every change in this task is additive/replacing inside existing files.

## MODIFY

- `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs` (new field, new
  optional constructor parameter, replace the one placeholder line)
- `src/NosAi.Runtime/Program.cs` (one new calibration-loading block,
  one new named argument on the existing `ScreenVitalsCapture`
  construction)
- `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs`
  (new tests only)

Do not touch `DialogRoiCalibration.cs`, `DialogWindowReader.cs`,
`DialogWindowStateComposer.cs`, `ScreenDialogWindowSource.cs`,
`VisualObservation.cs`, or `TargetRoiCalibration.cs`/
`ScreenTargetFrameSource.cs`/`TargetStateComposer.cs` (the target-frame
family stays exactly as-is — this task adds a parallel path, it does not
touch the existing one).

## Scope, stated explicitly

- Screen-only: `DialogWindowStateComposer.Compose` takes no wire-side
  argument (unlike `TargetStateComposer.Compose`) because no NosTale
  opcode for dialog/quest text exists in this repository — nothing to
  wire from the network side, so this task's `Compose` call has no third
  argument, unlike the `hasTarget` line above it.
- `VisualObservationFusion.FuseVitals` (WorldModel fusion) is **not**
  touched — that file is explicitly scoped to vitals only today, and
  `HasTarget` itself is not fused into `WorldModelSnapshot` either. This
  task only makes `VisualObservation.HasDialogWindow` carry a real
  composed value; consuming it downstream is a separate, later decision.
- An uncalibrated `DialogRoiCalibration` (the default, same as
  `TargetRoiCalibration`) must still produce an honest
  `Unknown(DialogRoiCalibration.NotCalibratedReason)` through this
  wiring — `DialogWindowStateComposer.Compose` already guarantees this,
  this task must not special-case or short-circuit around it.

## 1. `ScreenVitalsCapture.cs`

Add a new field, next to `_targetCalibration`:

```csharp
    private readonly TargetRoiCalibration _targetCalibration;
    private readonly DialogRoiCalibration _dialogCalibration;
    private readonly IPlayerAttackObserver? _wire;
```

Add a new constructor parameter, immediately after `targetCalibration`
and before `wire`, with its own XML doc paragraph (mirroring the
existing `targetCalibration` doc):

```csharp
    /// <param name="targetCalibration">
    /// Where the target frame sits on this operator's client (ADR-0018). Defaults to
    /// <see cref="TargetRoiCalibration.Uncalibrated"/>, which
    /// <see cref="TargetStateComposer.Compose"/> already reports honestly as
    /// <c>target_roi_not_calibrated</c> rather than a confident wrong answer -- see
    /// <c>HudProbe</c> for how an operator calibrates one.
    /// </param>
    /// <param name="dialogCalibration">
    /// Where the dialog-window panel sits on this operator's client, plus its
    /// confirmed-empty color baseline. Defaults to
    /// <see cref="DialogRoiCalibration.Uncalibrated"/>, which
    /// <see cref="DialogWindowStateComposer.Compose"/> already reports honestly as
    /// <see cref="DialogRoiCalibration.NotCalibratedReason"/> rather than a confident
    /// wrong answer.
    /// </param>
    /// <param name="wire">
    /// The wire's side of ADR-0018, used only to contradict a screen reading that
    /// says no target. Null (the default) means no contradiction check is available
    /// at this composition site today -- the screen stands alone, the same accepted
    /// shape <c>TargetAwareGameplayProvider</c> already uses for the same reason.
    /// </param>
    public ScreenVitalsCapture(
        Func<int?> processId,
        ScreenVitalReader? reader = null,
        TargetRoiCalibration? targetCalibration = null,
        DialogRoiCalibration? dialogCalibration = null,
        IPlayerAttackObserver? wire = null,
        uint adapterIndex = 0,
        uint outputIndex = 0,
        uint acquireTimeoutMs = 250,
        Func<DateTime>? clock = null)
    {
        _processId = processId ?? throw new ArgumentNullException(nameof(processId));
        _reader = reader ?? new ScreenVitalReader();
        _targetCalibration = targetCalibration ?? TargetRoiCalibration.Uncalibrated;
        _dialogCalibration = dialogCalibration ?? DialogRoiCalibration.Uncalibrated;
        _wire = wire;
        _adapterIndex = adapterIndex;
        _outputIndex = outputIndex;
        _acquireTimeoutMs = acquireTimeoutMs;
        _clock = clock ?? (() => DateTime.UtcNow);
    }
```

(Only `dialogCalibration` and its assignment line are new; every other
line above is shown for anchoring and must remain otherwise unchanged.)

In `Capture()`, replace exactly this block:

```csharp
        // Not wired in this pass -- DialogRoiCalibration/DialogWindowReader/
        // DialogWindowStateComposer/ScreenDialogWindowSource exist (AP-02/A1+A3)
        // but no caller here has composed them into a reading yet. Same honest
        // shape HasTarget itself used before Q-067's wiring: an explicit,
        // named reason, never a guessed true/false.
        ClassifiedValue<bool> hasDialogWindow = ClassifiedValue<bool>.Unknown("dialog_window_composer_not_wired_in_this_pass");
```

with:

```csharp
        // ScreenDialogWindowSource reads the same already-acquired `frame`
        // (via SingleFrameSource, never a second real DXGI acquisition -- see
        // the class remarks, "One frame, three readers" (now four)) at the
        // operator-calibrated dialog ROI. Unlike hasTarget above, Compose
        // takes no wire-side argument: no NosTale opcode for dialog/quest
        // text exists in this repository, so the screen is the only source
        // there is (see DialogWindowStateComposer's own remarks). An
        // uncalibrated DialogRoiCalibration (the default) still produces an
        // honest Unknown(NotCalibratedReason) here, same as hasTarget does
        // for an uncalibrated TargetRoiCalibration.
        var dialogFrames = new ScreenDialogWindowSource(
            new SingleFrameSource(frame, _recordingSource!.Source),
            _dialogCalibration,
            () => window.ClientArea);
        DialogWindowObservation screenDialog = dialogFrames.Read();
        ClassifiedValue<bool> hasDialogWindow = DialogWindowStateComposer.Compose(_dialogCalibration, screenDialog);
```

No other line in `Capture()`, `TryEnsurePipeline`, `Dispose`,
`RecordingFrameSource`, or `SingleFrameSource` changes.

## 2. `Program.cs`

Immediately after the existing `targetCalibration` loading block (the
one ending `targetCalibration = NosAi.Runtime.Perception.TargetRoiCalibration.Load(...)`
inside `if (options.FuseWorldModel) { ... }`, right before the blank
line that precedes `using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = ...`),
add the identical block one type over:

```csharp
        // Same reasoning as targetCalibration immediately above: loaded once,
        // only when --fuse-world-model is on, and a missing/absent file loads
        // as Uncalibrated -- the state before an operator has confirmed a
        // dialog-window crop -- which DialogWindowStateComposer reports
        // honestly as dialog_roi_not_calibrated until an operator calibrates one.
        NosAi.Runtime.Perception.DialogRoiCalibration dialogCalibration = NosAi.Runtime.Perception.DialogRoiCalibration.Uncalibrated;
        if (options.FuseWorldModel)
        {
            dialogCalibration = NosAi.Runtime.Perception.DialogRoiCalibration.Load(
                Path.Combine(repo, NosAi.Runtime.Perception.DialogRoiCalibration.RelativePath), out _);
        }
```

`repo` is the same local already computed inside the `targetCalibration`
`if` block immediately above — it is scoped to that `if`, so this new
block needs its own `if (options.FuseWorldModel) { ... }` with its own
`repo` resolution reusing the identical two-line fallback
(`NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory) ?? ... ?? Directory.GetCurrentDirectory()`),
copied verbatim, not shared across the two `if` blocks (do not hoist
`repo` out to a wider scope — that is a larger refactor than this task's
"one new field, same pattern" scope).

Then change:

```csharp
        using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = options.FuseWorldModel
            ? new NosAi.Runtime.Perception.ScreenVitalsCapture(AttachedProcessId, targetCalibration: targetCalibration)
            : null;
```

to:

```csharp
        using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = options.FuseWorldModel
            ? new NosAi.Runtime.Perception.ScreenVitalsCapture(AttachedProcessId, targetCalibration: targetCalibration, dialogCalibration: dialogCalibration)
            : null;
```

No other line in `Program.cs` changes.

## Tests

Add to `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs`,
reusing whatever stub/helper pattern that file already uses for
`targetCalibration`/`wire` (do not invent new test infrastructure):

- A constructor test mirroring
  `Constructor_AcceptsATargetCalibrationAndAWireObserver_WithoutThrowing`:
  constructing `ScreenVitalsCapture` with a non-null `dialogCalibration`
  (and, if that existing test already also passes a `wire`, keep passing
  one here too) must not throw, on this host or a real Windows one
  (write it host-independently the same way that existing test already
  is, per its own comment — do not make it Windows-only if the existing
  one is not).
- A regression test mirroring
  `Capture_WithNoProcessId_ReturnsUnobserved_EvenWithATargetCalibrationSupplied`:
  with no process id attached, a supplied `dialogCalibration` must not
  change the fail-closed outcome — `Capture()` still returns
  `VisualObservation.Unobserved(NoProcessIdReason, ...)`, and
  `observation.HasDialogWindow.FailureReason` equals
  `ScreenVitalsCapture.NoProcessIdReason` (mirroring the existing
  assertion shape for `HasTarget` at the same call site).

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~ScreenVitalsCaptureTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

All green, 0 warnings/0 errors beyond the one pre-existing unrelated
xUnit2031 warning already present on `main`, no regression in either
full suite.

## Known limitation, stated plainly

Same limitation §11 already declared for `HasTarget`: the real composed
value of `HasDialogWindow` (`Derived(true/false)` once calibrated) is
not exercised by any test in this environment — no real Windows client,
no real dialog-window crop to calibrate against. Not worsened by this
task, not resolved by it. Report `Present` for the new wiring code,
`Integrated` only once a human confirms a real calibrated
`--fuse-world-model` run on Windows produces a real `Derived` (not
`Unknown`) `HasDialogWindow` value.

## Report back

Files modified; build/test evidence with exact pass counts;
verification level (`Present`) and why not `Integrated`; anything found
in `ScreenVitalsCapture.cs`/`Program.cs`/the dialog-window family that
looks wrong (do not fix it yourself — report it).
