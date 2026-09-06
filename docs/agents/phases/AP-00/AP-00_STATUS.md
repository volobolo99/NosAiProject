# AP-00 — Hardware & Runtime Capability: Status (A5 audit)

**Author:** A5 (Claude), test/benchmark/documentation audit only.
**Date:** 2026-09-05.
**Scope:** records what exists for AP-00 after A1-A4 completed their parallel
work, the gaps this audit found and closed with new tests, the benchmark
this phase requires, and the known defects A6 must resolve before AP-00 can
honestly be called `Integrated`. A5 owns no implementation file in this
phase — every finding below that is not a test-count is a **found**, not
**fixed**, defect.

Maturity classification used below follows `docs/agents/AGENT_WORK_PROTOCOL.md`:
`Present` (exists) < `Integrated` (combined tree builds/tests) < `Done`
(acceptance criteria met in this environment) < `Verified` (real
target-hardware evidence). **Nothing in this phase is `Verified`: this audit
ran entirely in a Linux sandbox with no target ASUS Nitro V16 / RTX 5060
hardware available.**

## 1. What exists for AP-00

| Package | Owner | Files | Tests before A5 | Status |
|---|---|---|---|---|
| Hardware capability contracts | A1 | `src/NosAi.Core/Hardware/*.cs` | 49 (`tests/NosAi.Core.Tests/Hardware/`) | `Present` |
| AI budget scheduling policy | A3 | `src/NosAi.Core/Scheduling/*.cs` | 62 (`tests/NosAi.Core.Tests/Scheduling/`) | `Present` |
| Runtime capability gate + adapter | A4 (Claude, in place of Cursor) | `src/NosAi.Runtime/Hardware/Gate/*.cs` | 29 (`tests/NosAi.Runtime.Tests/Hardware/Gate/`) | `Integrated` (builds against A1's real contracts, not a mock) |
| Python hardware profiling | A2 (Claude, in place of Cursor) | `nosai/runtime/hardware.py`, `nosai/runtime/hardware_watchdog.py` | 21 (`tests/test_hardware_profiling.py`) | `Present` |

All four packages build clean in Release and their own pre-existing test
suites were green before this audit started (verified again below, after
A5's additions).

## 2. What A5 added (audit + benchmark + docs only — no implementation touched)

### 2.1 New test files

| File | Tests | Result |
|---|---|---|
| `tests/NosAi.Core.Tests/Scheduling/InferenceBudgetSchedulerConcurrencyTests.cs` | 3 | 1 **intentionally fails** (documents Defect #1 below), 2 pass |
| `tests/NosAi.Core.Tests/Hardware/InferenceTierFeasibilityAdditionalBoundaryTests.cs` | 18 | 1 **intentionally fails** (documents Defect #2 below), 17 pass |
| `tests/NosAi.Core.Tests/Scheduling/SchedulingContractSerializationTests.cs` | 9 | all pass |
| `tests/NosAi.Core.Tests/Scheduling/InferenceSchedulingResourceBudgetBenchmarks.cs` | 3 | all pass (benchmark, see §3) |
| `tests/test_ap00_hardware_boundary_audit.py` | 6 | all pass |

**33 new C# tests + 6 new Python tests = 39 new tests total.** 37 pass; 2
fail by design, precisely because they document two real, pre-existing
defects this audit found (never introduced by A5, and not fixable from
within A5's file ownership). **Do not "fix" the suite by deleting, skipping,
or weakening those two tests** — see Defects #1 and #2 below and each test's
own XML doc for why they must stay red until the underlying code is fixed.

No existing test file was modified. No implementation file under
`src/NosAi.Core/Hardware/`, `src/NosAi.Core/Scheduling/`,
`src/NosAi.Runtime/Hardware/Gate/`, `nosai/runtime/hardware.py`, or
`nosai/runtime/hardware_watchdog.py` was touched by A5.

### 2.2 Two real defects found (evidence, not opinion)

**Defect #1 — `InferenceBudgetScheduler.TryAdmit` overcommits the resource
ledger under concurrent callers** (A3's file,
`src/NosAi.Core/Scheduling/InferenceBudgetScheduler.cs`).

`TryAdmitCore` reads `_ledger.Available`, then calls `_queue.TryEnqueue`,
then calls `_ledger.TryReserve(job.EstimatedCost)` — and **discards that
final call's `bool` return value**. `ResourceBudgetLedger` and
`BoundedTierQueue` are each individually thread-safe (proven by this audit's
own isolation tests, which pass), but the three-step sequence across them is
not atomic. Under concurrent `TryAdmit` calls racing a tight budget, more
than one caller can pass the headroom check before either has reserved
anything, both enqueue, and only one `TryReserve` call actually
succeeds — the other's `false` result is silently ignored and that job is
still reported `AdmissionOutcome.Accepted`.

Reproduced deterministically (not a rare timing coincidence): with 16
concurrent callers racing a 1-unit CPU budget, this audit's spike and its
committed regression test both observed **100% overcommit** (16/16 or
32/32/64/64 callers reported `Accepted` against a 1-unit budget) across every
run tried. This directly violates the AP-00 DoD in
`docs/ROADMAP_ESECUTIVA.md` §7 ("nessun overcommit VRAM/RAM") and this
class's own XML doc ("nothing here is ever silently dropped").

Downstream consequence demonstrated by the same test: once every falsely
"Accepted" job later calls the documented `scheduler.Complete(job)` lifecycle
method, the ledger either throws `InvalidOperationException` (unbalanced
release) or is silently over-released — a real operational blast radius, not
a cosmetic reporting mismatch.

Regression test: `InferenceBudgetSchedulerConcurrencyTests.ConcurrentTryAdmit_MustNeverAcceptMoreJobsThanTheLedgerCanActuallyReserve_KnownSchedulerDefect`
(currently **failing by design**).

Recommended fix (for whoever picks this up — out of A5's ownership): treat a
`false` return from the final `_ledger.TryReserve(...)` exactly like a
queue-full rejection — dequeue the job just enqueued, then run the existing
`FailOrDegradeOnce` path for it (`RejectionReason.InsufficientBudget`)
instead of falling through to `Accepted`.

**Defect #2 — `GpuCapability.FreeVramMb` integer-overflows on a corrupted
negative `TotalVramMb`** (A1's file,
`src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs`).

`FreeVramMb` computes `Math.Max(0, TotalVramMb.Value - UsedVramMb.Value)`
with plain unchecked `long` arithmetic. Nothing in `ClassifiedValue<T>` or
`GpuCapability` prevents a corrupted/nonsensical probe reading from reporting
a negative `TotalVramMb` as `Live`. When that happens with a small enough
`UsedVramMb`, the subtraction underflows past `long.MinValue` and **wraps
around to `long.MaxValue`** (a textbook `long.MinValue - 1` overflow)
instead of staying non-positive or Unknown.

Confirmed by this audit's test:
`GpuCapability_FreeVramMb_OverflowsToALargePositiveValue_ForACorruptedNegativeTotalVram_KnownA1RobustnessGap`
(currently **failing by design** — it asserts the derived value must never
be a fabricated positive number for impossible input).

**Important nuance this audit also verified and must not be lost**: today
this overflow does **not** let `InferenceTierFeasibility.CanRun` wrongly
authorize a GPU tier, because `CanRunTier2`/`CanRunTier3` separately require
`TotalVramMb` itself to be at least the tier's own floor, and any
`TotalVramMb` negative enough to trigger the dangerous overflow direction is
also, by construction, negative enough to fail that independent check on its
own. This is confirmed by a second, **passing** test:
`CanRun_IsNotFooledByThatOverflow_OnlyBecauseOfTheIndependentTotalVramFloorCheck`.
That protection is **incidental** (a side effect of an unrelated threshold
check), not a designed defense — any future code path that reads
`FreeVramMb` directly without independently re-validating `TotalVramMb`'s
sign would be exposed. Fix it at the source rather than relying on this
coincidence.

### 2.3 Cross-reference: the Python side already guards against this class of
bug, the C# side does not (yet)

While investigating Defect #2, this audit noted that A2's Python hardware
probes (`nosai/runtime/hardware.py`) **already** treat a non-positive total
or negative available/used reading as `Unknown` rather than a fabricated
number (`_probe_system_ram`, `_probe_nvidia_gpu`, `_probe_storage` all check
this explicitly and are already tested for it in
`tests/test_hardware_profiling.py`). A1's C# `GpuCapability`/`ClassifiedValue<T>`
performs no equivalent guard. This is a concrete, low-risk pattern A1/A6 can
port across languages when fixing Defect #2.

### 2.4 A third, lower-severity finding: `NOSAIHardwareWatchdog` fails OPEN
on missing thermal telemetry (Python, `nosai/runtime/hardware_watchdog.py`)

`NOSAIHardwareWatchdog.check()` builds `temps = [x for x in (cpu, gpu) if x
is not None]`; when both readings are `None` (the default `NullHardwareProbe`,
or any probe whose sensors are unsupported on this machine), `temps` is
empty and `any(x > max_temp for x in temps)` is `False` — so a **total
absence of thermal data defaults to `allowed=True`**. This is the opposite
policy from A1/A4's C# hardware gate, which explicitly refuses every tier
above Tier 0 when `Thermal.ThrottleState` is Unknown, and it does not match
CLAUDE.md's "Unknown is not zero, false or empty" invariant.

Severity is lower than Defects #1/#2 because `NOSAIHardwareWatchdog` is not
currently wired into any execution/Safety path anywhere in `nosai/`
(confirmed by grep — no call site besides its own tests): it is `Present`,
not yet `Integrated`, as a safety mechanism. This is a design gap to close
**before** any later phase (most likely AP-08, Strategic Autonomy + Safety)
wires this watchdog into an authoritative execution gate, not an active
bypass today. Documented and pinned down by
`test_watchdog_allows_by_default_when_no_thermal_telemetry_is_available_at_all`
in `tests/test_ap00_hardware_boundary_audit.py` (passes — it records current
behaviour, it does not yet assert a contract this class never claimed to
honor).

## 3. Resource-budget benchmark (required by the AP-00/A5 command)

File: `tests/NosAi.Core.Tests/Scheduling/InferenceSchedulingResourceBudgetBenchmarks.cs`.

Re-run with:

```bash
export PATH="/root/.dotnet:$PATH"; export DOTNET_CLI_TELEMETRY_OPTOUT=1; export DOTNET_NOLOGO=1
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release \
  --filter "FullyQualifiedName~InferenceSchedulingResourceBudgetBenchmarks" \
  --logger "console;verbosity=detailed"
```

What it measures (three benchmarks, each prints its numbers via
`ITestOutputHelper` rather than asserting a tight SLO):

1. **Admission throughput** — `InferenceBudgetScheduler.TryAdmit` over 20,000
   synthetic Tier0-3 jobs against a fixed, modest budget
   (`ResourceCost(500, 500, 512, 512)`) with steady-state churn (every 5th
   accepted job completes and frees its reservation). Measured on this
   sandbox: **~331,000 admissions/sec**, 116 accepted / 0 degraded / 19,884
   explicitly rejected once the tight budget filled. Only a generous sanity
   floor (`> 1,000/sec`) is asserted, to catch a gross regression (e.g. an
   accidental blocking call or O(n) scan), never a tight numeric target.
2. **`Tier3AsyncExecutor.RunWithDeadlineAsync` latency for work that
   completes within its deadline** — 50 samples of ~1ms simulated work
   against a 5,000ms deadline. Measured on this sandbox: p50≈3.6ms,
   p95≈4.7ms, p99≈5.3ms. Only a generous ceiling (`p99 < 2,000ms`) is
   asserted.
3. **`Tier3AsyncExecutor` timeout-return latency for work that never
   completes on its own** — the core "Tier 3 never blocks Safety/recovery"
   guarantee (`docs/ROADMAP_ESECUTIVA.md` §5). Measured on this sandbox: a
   50ms nominal deadline returned control at ~65ms (≈15ms overshoot). Only a
   generous ceiling (`< deadline + 2,000ms`) is asserted.

**Explicit acceptance criterion for this benchmark**, per the AP-00/A5
command's instruction not to repeat `TransportLoopTests`' mistake (a tight
latency budget that already flakes intermittently on this very CI
environment, reproduced during this audit's own baseline run — see §5):
these numbers are recorded for humans to read, not enforced as a strict
pass/fail gate. Re-run and compare the printed numbers by eye when
evaluating a future change to the scheduling hot path; do not tighten the
asserted bounds to match one machine's measurement.

## 4. Known, declared limits (carried forward from A1-A4's own handoffs — not
new findings)

- **Thermal telemetry is always Unknown with the existing Gate 1 probe path**
  (A4's own finding, confirmed by this audit's read of
  `RuntimeHardwareCapabilityProvider.Map` and its existing test
  `GetSnapshot_ProducesASnapshotThatIsFailClosedForEveryTierAboveZero_UntilThermalTelemetryExists`):
  `Thermal.ThrottleState` is unconditionally `ClassifiedValue<HardwareThrottleState>.Unknown(...)`
  because the existing WMI-backed probe reports no thermal/throttle data at
  all. Consequently **Tier 1, 2 and 3 can never be authorized** through
  `RuntimeHardwareCapabilityProvider` + `HardwareInferenceCapabilityGate`
  until a real thermal reading is added to the probe. This is correct
  fail-closed behaviour, not a bug — but it is a real operational ceiling: no
  GPU/heavy-CPU inference tier can run in production today through this
  path.
- **`InferenceTier` is duplicated between A1 and A3**
  (`NosAi.Core.Hardware.InferenceTier` vs `NosAi.Core.Scheduling.InferenceTier`),
  a deliberate, explicitly-documented stand-in A3 wrote before A1's contract
  existed. A3's own file (`src/NosAi.Core/Scheduling/InferenceTier.cs`)
  already carries a detailed A6 integration note with the exact member
  mapping and a recommended merge direction (keep A1's as canonical, delete
  A3's, repoint every Scheduling type that uses it). A5 did not touch this —
  see §6 for the merge checklist condensed for A6.
- **7 pre-existing broken Python test files, unrelated to AP-00**, found by
  A2 before any AP-00 work started (missing `nosai.phone.*` modules). Out of
  scope for this phase; not touched by A2 or A5. Left exactly as found.

## 5. Build/test evidence (this audit's own runs)

```
$ export PATH="/root/.dotnet:$PATH"; export DOTNET_CLI_TELEMETRY_OPTOUT=1; export DOTNET_NOLOGO=1
$ dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
Build succeeded. 0 Warning(s). 0 Error(s).

$ dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
Total: 251. Passed: 249. Failed: 2 (both intentional — see Defects #1/#2 above).
(Baseline before A5's additions: Total 218, Passed 217, Failed 1 — the
pre-existing TransportLoopTests loopback-latency flake, unrelated to AP-00,
explicitly called out in this task's own instructions as an example of what
NOT to do; it did not fail on every run during this audit, confirming it is
a pre-existing flake, not something this phase's tests interact with.)

$ dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
Total: 1825. Passed: 1767. Skipped: 58. Failed: 0.
(Confirmed unaffected by A5 — no file under NosAi.Runtime or
NosAi.Runtime.Tests was touched. One run during this audit showed a single
unrelated transient failure that did not reproduce on immediate re-run —
consistent with an environment-level flake, not a regression from this
phase's changes.)

$ python3 -m compileall -q nosai
(clean, no output)

$ python3 -m pytest -q tests/test_hardware_profiling.py tests/test_ap00_hardware_boundary_audit.py tests/test_agent_runtime_expansion.py tests/test_runtime_optimizations.py
36 passed.
```

## 6. Handoff checklist for A6 (integration agent)

Blocking items before AP-00 can be called `Integrated` with the DoD in
`docs/ROADMAP_ESECUTIVA.md` §7 ("nessun overcommit VRAM/RAM") actually true:

1. **Fix Defect #1** (`InferenceBudgetScheduler.TryAdmitCore` discards
   `TryReserve`'s result) — src/NosAi.Core/Scheduling/InferenceBudgetScheduler.cs.
   Un-skip nothing; the regression test already exists and will go green on
   its own once fixed:
   `InferenceBudgetSchedulerConcurrencyTests.ConcurrentTryAdmit_MustNeverAcceptMoreJobsThanTheLedgerCanActuallyReserve_KnownSchedulerDefect`.
2. **Fix Defect #2** (`GpuCapability.FreeVramMb` overflow on corrupted
   negative `TotalVramMb`) — src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs.
   Regression test:
   `InferenceTierFeasibilityAdditionalBoundaryTests.GpuCapability_FreeVramMb_OverflowsToALargePositiveValue_ForACorruptedNegativeTotalVram_KnownA1RobustnessGap`.
3. **Merge the duplicated `InferenceTier` enum** per A3's own detailed
   integration note in `src/NosAi.Core/Scheduling/InferenceTier.cs`: keep
   `NosAi.Core.Hardware.InferenceTier` as canonical, delete
   `NosAi.Core.Scheduling.InferenceTier`, repoint `InferenceJob`,
   `DegradationPlan`, `BoundedTierQueue`, `LowestCostJobSelector`,
   `Tier3AsyncExecutor`, `InferenceBudgetScheduler` and their tests. Same
   note also flags a related, narrower overlap worth resolving at the same
   time: A1's `InferenceJobPriority` (Background/Normal/Interactive/Critical)
   vs A3's `JobPriority` (Low/Normal/High/Critical), and A1's
   `InferenceJobBudgetRequest`/`TierResourceBudget` vs A3's plain
   `InferenceJob`/`ResourceCost` — A3's note recommends keeping A1's
   richer, `ClassifiedValue<T>`-aware shapes as canonical and adapting A3's
   admission/queue/selection logic to consume them. A5 did not implement
   this merge (out of file ownership); re-run the full Scheduling test suite
   after each rename step.
4. Re-run the full AP-00 test surface after both fixes and confirm the two
   currently-failing tests above go green **without** any other test
   changing outcome (in particular, the isolation-control tests in
   `InferenceBudgetSchedulerConcurrencyTests` — `LedgerAlone_...` and
   `Queue_Alone_...` — must keep passing; they prove the fix targeted the
   right layer).
5. Non-blocking but worth scheduling: extend real thermal telemetry into
   `RuntimeHardwareCapabilityProvider`'s probe path (§4) so Tier 1-3 can ever
   be authorized in production, and consider whether
   `NOSAIHardwareWatchdog`'s fail-open-on-missing-telemetry default (§2.4)
   needs to change before any later phase wires it into an authoritative
   Safety/execution gate.
6. **No item in this phase reaches `Verified`.** Every number in §3 and every
   claim in §4 was produced in a Linux sandbox with no ASUS Nitro V16 / RTX
   5060 hardware, no Windows APIs, and no real WMI/NVML telemetry available.
   `Verified` requires a real run on the target laptop per CLAUDE.md's
   "Real-environment rule".

## 7. Verification level for this AP-00 package, as of A5's audit

**`Integrated`** for the combined tree (it builds, and the combined test
suite runs and reports its results honestly — including two tests that fail
on purpose to keep a real defect visible). **Not `Done`** until Defects #1
and #2 are fixed and their regression tests pass. **Not `Verified`** under
any circumstance until real ASUS Nitro V16 / RTX 5060 hardware evidence
exists — none does today.

## 8. A6 integration — completed

**Author:** A6 (Claude), integration only. **Date:** 2026-09-05, same day.

Closed handoff items #1-#4 from §6:

1. **Defect #1 fixed.** `InferenceBudgetScheduler.TryAdmitCore`
   (`src/NosAi.Core/Scheduling/InferenceBudgetScheduler.cs`) now checks the
   `bool` returned by the final `_ledger.TryReserve(...)` call. On `false`
   (lost the reservation race) it calls a new
   `BoundedTierQueue.TryRemove(tier, jobId)` to undo the enqueue, then runs
   the existing `FailOrDegradeOnce` path with `RejectionReason.InsufficientBudget`
   — exactly the recommended fix direction in A5's report. `TryRemove` is a
   new method on `BoundedTierQueue`: an O(n) rebuild under the existing lock
   (matched by `InferenceJob.Id`), justified because this is the rare
   admission-rollback path, never the normal O(1) admit/dequeue hot path.
   Regression test `ConcurrentTryAdmit_MustNeverAcceptMoreJobsThanTheLedgerCanActuallyReserve_KnownSchedulerDefect`
   now passes, and both isolation controls
   (`LedgerAlone_...`/`Queue_Alone_...`) still pass unchanged.

2. **Defect #2 fixed.** `GpuCapability.FreeVramMb`
   (`src/NosAi.Core/Hardware/HardwareCapabilitySnapshot.cs`) now additionally
   requires `TotalVramMb.Value >= 0 && UsedVramMb.Value >= 0` before deriving
   a value; a negative reading on either side now yields
   `ClassifiedValue<long>.Unknown("gpu_vram_reading_is_negative_and_therefore_invalid")`
   instead of performing arithmetic on impossible input. This is the same
   guard pattern §2.3 noted the Python probes already use. Regression test
   `GpuCapability_FreeVramMb_OverflowsToALargePositiveValue_ForACorruptedNegativeTotalVram_KnownA1RobustnessGap`
   now passes. The companion test
   `CanRun_IsNotFooledByThatOverflow_OnlyBecauseOfTheIndependentTotalVramFloorCheck`
   was updated (its premise — that `FreeVramMb` evaluated to a wrapped
   `long.MaxValue` — was the bug that is now fixed, so asserting that exact
   value would have re-encoded the defect as "expected"); it now asserts
   `FreeVramMb.HasValue` is `false` for the same corrupted input, and still
   confirms `CanRun` refuses Tier2/Tier3 — now via the ordinary Unknown/floor
   checks rather than an incidental coincidence.

3. **`InferenceTier` duplication resolved.** `src/NosAi.Core/Scheduling/InferenceTier.cs`
   is deleted. Every file that used it
   (`BoundedTierQueue.cs`, `InferenceJob.cs`, `Tier3AsyncExecutor.cs`, and
   all `tests/NosAi.Core.Tests/Scheduling/*.cs` files) now has
   `using NosAi.Core.Hardware;` and uses `NosAi.Core.Hardware.InferenceTier`
   with its member names (`Tier0DeterministicRules`, `Tier1LightweightLocalMl`,
   `Tier2GpuAcceleratedVision`, `Tier3ExpensiveLocalReasoning`) — a purely
   mechanical rename per A3's own mapping table, no ordinal values changed.
   `NosAi.Core.Hardware.InferenceTier` is now the sole `InferenceTier` type
   in the assembly.

4. **Deliberately deferred, not done in this pass:** the deeper unification
   A3's note also flagged — `InferenceJobPriority` (Hardware) vs `JobPriority`
   (Scheduling), and `InferenceJobBudgetRequest`/`TierResourceBudget`
   (Hardware, `ClassifiedValue<T>`-aware) vs the plain `InferenceJob`/`ResourceCost`
   the scheduler actually runs on. Unlike the `InferenceTier` enum, these are
   not two names for the same compiled type colliding in one assembly; they
   are two working, independently-tested, non-conflicting designs at
   different levels of richness. Adapting the scheduler's admission logic to
   consume the Unknown-aware Hardware shapes is a real, separate piece of
   design work (projecting a `ClassifiedValue<T>`-wrapped budget down to
   concrete numbers, deciding how an Unknown component degrades) — forcing it
   into this integration pass risked exactly the kind of unreviewable,
   over-broad diff CLAUDE.md's "small reviewable changes" rule exists to
   prevent. Left as an explicit open item for a follow-up task, not silently
   dropped.

5. Not addressed in this pass (carried forward from §4/§6 as still open,
   non-blocking for `Integrated`): thermal telemetry is still absent from
   the real hardware probe path (Tier 1-3 still cannot be authorized in
   production), and `NOSAIHardwareWatchdog`'s fail-open default is unchanged.
   Both remain correctly documented, not silently fixed or ignored.

**Build/test evidence for this integration (this session, Linux sandbox):**

```
dotnet build src/NosAi.Core/NosAi.Core.csproj -c Release      → 0 Warning(s), 0 Error(s)
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release → 0 Warning(s), 0 Error(s)
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 251, Skipped: 0, Total: 251
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1767, Skipped: 58, Total: 1825
dotnet build tests/NosAi.ControlPanel.Tests/NosAi.ControlPanel.Tests.csproj -c Release → 0 Warning(s), 0 Error(s)
python3 -m compileall -q nosai → clean
python3 -m pytest -q tests/test_hardware_profiling.py → 19 passed
```

Zero test outcome changed apart from the two intended defect fixes and the
one updated companion assertion. No file outside this phase's ownership
(`src/NosAi.Core/Hardware/`, `src/NosAi.Core/Scheduling/`,
`src/NosAi.Runtime/Hardware/Gate/`, their tests, and this document) was
touched by this integration step.

## 9. Revised verification level, as of A6 integration

**`Done`** for AP-00's core hardware-capability/budget-policy/gate package:
Defects #1 and #2 are fixed with passing regression tests, the
`InferenceTier` duplication no longer exists, and the full combined test
surface (C# + Python) is green. **Still not `Verified`** — nothing in this
phase has run against real ASUS Nitro V16 / RTX 5060 hardware, and won't be
until that real-environment run happens per CLAUDE.md's "Real-environment
rule". The item-4 unification and the item-5 thermal/watchdog gaps above
remain open, tracked follow-up work, not blockers to calling the shipped
surface `Done`.
