# AP-02 / A2+A4 — DeepSeek — Wire `TargetStateComposer` into `ScreenVitalsCapture`

## Why this task exists

`AP-02_STATUS.md` §"Limite dichiarato"/§10 names a real, contained gap:
`ScreenVitalsCapture.Capture()` always returns `HasTarget` as
`ClassifiedValue<bool>.Unknown("target_state_composer_not_wired_in_this_pass")`
— a **code** reason, not a data reason. `docs/agents/DEEPSEEK_TASKS.md`'s
"Candidati da investigare" flagged this as possibly unblocked already,
since a screen calibration mechanism exists independent of the OCR/ONNX
gap that blocks other AP-02 work. Investigation (this session) confirmed
it: `TargetRoiCalibration`, `ScreenTargetFrameSource` and
`TargetStateComposer` are **all already built, real, and already used by
a working consumer** (`TargetAwareGameplayProvider`, the wire-side
decorator) — this task only threads the same, already-correct mechanism
into the screen-side capture class that AP-02 built, which currently
skips it.

**What this buys, and what it deliberately does not.** Once wired,
`HasTarget` stops being permanently `Unknown` for a code reason and
starts being `Unknown` only for the honest data reason
(`TargetRoiCalibration.NotCalibratedReason`, until an operator actually
calibrates via the existing `HudProbe` workflow) — calibrating unlocks
real `Derived(true/false)` values with no further code change. It does
**not** add a wire-side contradiction check: `TargetAwareGameplayProvider`
accepts an optional `IPlayerAttackObserver? wire` and documents `null` as
an accepted, honest degradation ("the screen stands alone, which is
weaker and is never wrong in the direction that matters") — no
`IPlayerAttackObserver` is available at `ScreenVitalsCapture`'s
composition site in `Program.cs` today (it would require building a
whole new WinDivert network capture there, a materially bigger and
unrelated task), so this pass wires `wire: null` explicitly, not as a
punt but as the same accepted shape the wire-side consumer already uses.

## Already built and real — read before writing code

- `src/NosAi.Runtime/Perception/TargetStateComposer.cs`: `Compose(TargetRoiCalibration, TargetFrameObservation, DateTime? lastPlayerAttackAtUtc)`
  — pure, total, already checks `calibration.IsCalibrated` first and
  returns `Unknown(NotCalibratedReason)` honestly when it is not. Do not
  change this file.
- `src/NosAi.Runtime/Perception/TargetRoiCalibration.cs`: `Uncalibrated`
  (safe default), `Load(path, out failureReason)` (missing file → honest
  `Uncalibrated` + `NotCalibratedReason`, never an exception),
  `RelativePath = "data/perception/target-roi.calibration"`. Do not
  change this file.
- `src/NosAi.Runtime/Perception/ScreenTargetFrameSource.cs`: `ITargetFrameSource`
  implementation — `Read()` checks calibration, resolves the ROI against
  a **live** client area (`Func<PixelRect?> clientArea`, re-read every
  call, never cached — "a window moves"), acquires one frame via its
  injected `IFrameSource`, crops and reads it. Do not change this file.
- `src/NosAi.Runtime/LiveIntegration/TargetAwareGameplayProvider.cs`: the
  wire-side consumer already composing these three types together — the
  exact call shape to mirror (`_screen.Read()` then
  `TargetStateComposer.Compose(_calibration, screen, _wire?.LastPlayerAttackAtUtc)`).
  Do not change this file.
- `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs`: read `Capture()`
  and the class remarks in full, especially "One frame, two readers" —
  `RecordingFrameSource` exists specifically so `PerceptionPipeline` and
  `ScreenVitalReader` share exactly one real DXGI acquisition per cycle.
  **This invariant must not regress.** `_recordingSource!.LastFrame` and
  the local `frame` (bound by `_recordingSource!.LastFrame is not { } frame`
  a few lines above) are already the one frame this cycle acquired — the
  new target-frame read must reuse that exact `CaptureFrame`, never call
  `TryAcquire` on the real DXGI source a second time.
- `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs`: read
  in full, including its own header comment explaining why this sandbox
  can only exercise the fail-closed paths (no process id, non-Windows) —
  the frame-acquired branch (where the new wiring lives) is exactly as
  untestable here as the existing vitals-reading code in that same
  branch already is. This is a pre-existing, declared limitation of the
  class, not something this task is expected to fix.
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs`, `RunWindows`: the exact
  repo-root-resolution + calibration-load pattern to mirror for
  `Program.cs` (§3 below) —
  `TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory) ?? TestSuiteRunner.FindRepositoryRoot() ?? Directory.GetCurrentDirectory()`,
  then `XCalibration.Load(Path.Combine(repo, XCalibration.RelativePath), out _)`.

## OWN (new files only)

None. Every change in this task is additive inside existing files.

## MODIFY

- `src/NosAi.Runtime/Perception/ScreenVitalsCapture.cs`
- `src/NosAi.Runtime/Program.cs` (the `visualCapture` composition site only, lines ~783-791 as read today)
- `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs` (new tests only, do not change or remove any existing test)

Do not touch `TargetStateComposer.cs`, `TargetRoiCalibration.cs`,
`ScreenTargetFrameSource.cs`, `TargetAwareGameplayProvider.cs`, or any
other file in `Program.cs` outside the one composition site named above.

## 1. `ScreenVitalsCapture.cs` — accept a calibration and an optional wire observer

Add two fields and two constructor parameters (both optional, both
defaulting to the same honest "nothing extra" shape
`TargetAwareGameplayProvider`'s own constructor already uses):

```csharp
private readonly TargetRoiCalibration _targetCalibration;
private readonly IPlayerAttackObserver? _wire;
```

Constructor signature becomes (new parameters inserted after `reader`,
before `adapterIndex`, so every existing positional call — there are
none in production code; `Program.cs` and the test file both use named
arguments or the single required parameter — keeps compiling unchanged):

```csharp
public ScreenVitalsCapture(
    Func<int?> processId,
    ScreenVitalReader? reader = null,
    TargetRoiCalibration? targetCalibration = null,
    IPlayerAttackObserver? wire = null,
    uint adapterIndex = 0,
    uint outputIndex = 0,
    uint acquireTimeoutMs = 250,
    Func<DateTime>? clock = null)
{
    _processId = processId ?? throw new ArgumentNullException(nameof(processId));
    _reader = reader ?? new ScreenVitalReader();
    _targetCalibration = targetCalibration ?? TargetRoiCalibration.Uncalibrated;
    _wire = wire;
    _adapterIndex = adapterIndex;
    _outputIndex = outputIndex;
    _acquireTimeoutMs = acquireTimeoutMs;
    _clock = clock ?? (() => DateTime.UtcNow);
}
```

Document both new parameters with the same `<param>` doc-comment
discipline the existing ones already have — `targetCalibration`: "Where
the target frame sits on this operator's client (ADR-0018). Defaults to
`TargetRoiCalibration.Uncalibrated`, which `TargetStateComposer.Compose`
already reports honestly as `target_roi_not_calibrated` rather than a
confident wrong answer — see `HudProbe` for how an operator calibrates
one." `wire`: "The wire's side of ADR-0018, used only to contradict a
screen reading that says no target. Null (the default) means no
contradiction check is available at this composition site today — the
screen stands alone, the same accepted shape
`TargetAwareGameplayProvider` already uses for the same reason."

## 2. `ScreenVitalsCapture.cs` — the actual composition, inside `Capture()`

Replace exactly this line:

```csharp
        var hasTarget = ClassifiedValue<bool>.Unknown("target_state_composer_not_wired_in_this_pass");
```

with:

```csharp
        var targetFrames = new ScreenTargetFrameSource(
            new SingleFrameSource(frame, _recordingSource!.Source),
            _targetCalibration,
            () => window.ClientArea);
        TargetFrameObservation screenTarget = targetFrames.Read();
        ClassifiedValue<bool> hasTarget = TargetStateComposer.Compose(
            _targetCalibration, screenTarget, _wire?.LastPlayerAttackAtUtc);
```

`frame` and `window` are already in scope at this point in `Capture()`
(bound earlier in the same method, before the vitals read) — do not
re-fetch either. Replace the paragraph comment immediately above the
old line (the one starting "`TargetStateComposer.Compose` needs a
calibrated `TargetRoiCalibration` that nothing in this wiring pass
establishes...") with an accurate one, e.g.: "`ScreenTargetFrameSource`
reads the same already-acquired `frame` (via `SingleFrameSource`, never
a second real DXGI acquisition — see the class remarks, `One frame, two
readers`) at the operator-calibrated ROI, and `TargetStateComposer`
composes the result against the wire side, when one is available. An
uncalibrated `TargetRoiCalibration` (the default) still produces an
honest `Unknown(NotCalibratedReason)` here — this wiring does not
require calibration to exist, only makes real calibration take effect
once it does."

## 3. New private nested type — `SingleFrameSource`

Add as a private nested class inside `ScreenVitalsCapture`, next to the
existing `RecordingFrameSource` nested class (same section, same
visibility pattern):

```csharp
    /// <summary>
    /// Wraps one already-acquired frame as an <see cref="IFrameSource"/> that
    /// returns exactly that frame on every <see cref="TryAcquire"/> call,
    /// never a fresh real capture. Exists so <see cref="ScreenTargetFrameSource"/>
    /// reads the exact same pixels <see cref="PerceptionPipeline"/> and
    /// <see cref="ScreenVitalReader"/> already consumed this cycle -- see the
    /// class remarks, "One frame, two readers" (now three): a second real
    /// DXGI acquisition per cycle would cost double the capture and could
    /// legitimately observe a different instant of the screen than the
    /// entities/vitals this cycle already produced.
    /// </summary>
    private sealed class SingleFrameSource : IFrameSource
    {
        private readonly CaptureFrame _frame;

        public SingleFrameSource(CaptureFrame frame, DataSourceKind source)
        {
            _frame = frame;
            Source = source;
        }

        public DataSourceKind Source { get; }

        public bool TryAcquire(out CaptureFrame frame)
        {
            frame = _frame;
            return true;
        }
    }
```

## 4. Wiring `Program.cs` — load the calibration once, pass it in

Immediately before the existing `visualCapture` declaration (currently
lines ~789-791, right after the `AttachedProcessId` local function),
insert:

```csharp
        NosAi.Runtime.Perception.TargetRoiCalibration targetCalibration = NosAi.Runtime.Perception.TargetRoiCalibration.Uncalibrated;
        if (options.FuseWorldModel)
        {
            string repo = NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? NosAi.Runtime.Testing.TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            targetCalibration = NosAi.Runtime.Perception.TargetRoiCalibration.Load(
                Path.Combine(repo, NosAi.Runtime.Perception.TargetRoiCalibration.RelativePath), out _);
        }

```

then change the existing construction line from:

```csharp
        using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = options.FuseWorldModel
            ? new NosAi.Runtime.Perception.ScreenVitalsCapture(AttachedProcessId)
            : null;
```

to:

```csharp
        using NosAi.Runtime.Perception.ScreenVitalsCapture? visualCapture = options.FuseWorldModel
            ? new NosAi.Runtime.Perception.ScreenVitalsCapture(AttachedProcessId, targetCalibration: targetCalibration)
            : null;
```

The calibration load is guarded by `options.FuseWorldModel` (same flag
`visualCapture`'s own construction already checks) so no file I/O runs
when the flag is off. Use fully-qualified type names exactly as shown —
`Program.cs` does not import `NosAi.Runtime.Perception`/`NosAi.Runtime.Testing`
and every other reference to a type in those namespaces in this method
is already fully qualified; match that, do not add new `using`
directives to this file.

## Tests

Add to `tests/NosAi.Runtime.Tests/Perception/ScreenVitalsCaptureTests.cs`
(same file, same flat `NosAi.Runtime.Tests` namespace as the rest of it
— read the header comment explaining why before adding anything). New
tests only, every existing test must still pass unchanged:

- `SingleFrameSource` is `private` and has no dedicated unit test — same
  as `RecordingFrameSource`, its sibling nested type, which also has none
  today. Do not change its accessibility just to test it in isolation.
- Add a regression test proving the new constructor parameters do not
  break any existing fail-closed path:
  `Capture_WithNoProcessId_ReturnsUnobserved_EvenWithATargetCalibrationSupplied`
  — construct `ScreenVitalsCapture` with a *confirmed* (non-default)
  `TargetRoiCalibration` (build one with `TargetRoiCalibration.Confirmed(0.1, 0.1, 0.2, 0.2, 1920, 1080, DateTime.UtcNow)`)
  and a non-null `processId: () => null`, assert the result is still
  `Unobserved`/`NoProcessIdReason` exactly like the existing
  no-process-id test — proving the new parameters are inert until the
  frame-acquired branch is actually reached (which this sandbox cannot
  reach, per the class's own declared limitation).
- Add `Constructor_AcceptsATargetCalibrationAndAWireObserver_WithoutThrowing`
  — construct with both new parameters supplied (a confirmed calibration
  and a simple test double implementing `IPlayerAttackObserver` with a
  fixed `LastPlayerAttackAtUtc`), assert no exception and that `Capture()`
  still returns cleanly (same non-Windows/no-process-id fail-closed
  reasons as today, since the frame-acquired branch is unreachable here).

## Build / verification

```
export PATH="$PATH:/root/.dotnet"
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~ScreenVitalsCaptureTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

All green, 0 warnings/0 errors, no regression in either full suite —
every existing `ScreenVitalsCaptureTests` assertion must still pass
byte-for-byte identical to today (the new constructor parameters are
optional and additive).

## Known limitation, stated plainly

The actual composed `HasTarget` value (a real `Derived(true/false)` once
an operator calibrates) is **not** exercised by any test in this
sandbox — reaching it requires a located Windows client window and a
real DXGI frame, exactly the same declared limitation the rest of
`ScreenVitalsCapture.Capture()`'s frame-acquired branch already has (the
vitals-reading call a few lines above is equally untested here). This
task does not make that limitation worse; it does not claim to fix it
either. Report `Present` for this change, `Integrated` only once a human
confirms `--fuse-world-model` on a real Windows target with a calibrated
`data/perception/target-roi.calibration` actually produces a non-Unknown
`HasTarget` — do not claim that here.

## Report back

Files modified; build/test evidence with exact pass counts; verification
level (`Present`) and why not `Integrated`; anything found in
`ScreenVitalsCapture.cs`/`TargetStateComposer.cs`/`ScreenTargetFrameSource.cs`
that looks wrong (do not fix it yourself — report it).
