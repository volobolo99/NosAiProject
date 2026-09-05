# NosAi Dashboard — Live Data & Practical Test Center

Version: 2.0  
Date: 2026-09-05  
Status: CANONICAL PRODUCT REQUIREMENT

## Objective

The Windows `.exe` Dashboard/Control Panel is the operator's real-time observability and practical-test surface.

It must show the latest data actually observed by the runtime. It must never manufacture gameplay state, replace unavailable values with zero, or treat a cached/predicted value as live.

## Live data contract

Every gameplay-relevant value shown by the Dashboard must have source/provenance, observation timestamp, freshness/age, confidence where applicable, state classification (`Observed`, `Derived`, `Predicted`, `Cached`, `Unknown`), and an explicit reason when `Unknown`.

The Dashboard reads the canonical runtime snapshot. It does not independently invent a second WorldState.

Target operator refresh cadence: 250 ms for live status surfaces, subject to bounded runtime observation/update budget. Heavy perception, OCR and inference remain off the UI thread.

## Practical Test Center — T1 to T20

| ID | Test | Operator action | Evidence required |
|---|---|---|---|
| T1 | Attach & Live Observation | Start private test client when requested | Attached process + fresh observation |
| T2 | Screen / Vision | Keep client visible and perform requested visual scenario | Real frame + ROI/detection evidence |
| T3 | Network Observation | Generate normal client traffic | Client-visible traffic + timestamps |
| T4 | World Model | Change a visible game state | Updated WorldState + provenance |
| T5 | Navigation | Move to controlled destination | Position/path/replan before/after |
| T6 | Combat | Enter controlled combat scenario | Target/decision/Guard/Execute/Verify chain |
| T7 | Quest / Interaction | Perform requested observable interaction | Before/after state delta |
| T8 | Character / Inventory | Open/use requested observable state | Character/inventory delta + provenance |
| T9 | Autonomous Loop | Let runtime operate in declared scenario | Observe → plan → guard → safety → execute → verify → re-observe |
| T10 | Resilience / Safety | Perform controlled perturbation | Fail-closed + watchdog/recovery |
| T11 | Hardware / Runtime | None | CPU/RAM/GPU/VRAM/runtime observations |
| T12 | Safety Gate | None | Safety policy and fail-closed state |
| T13 | Guard / Trust | None | Guard connectivity/auth/trust evidence |
| T14 | Runtime Health | None | Runtime status + correlation |
| T15 | Snapshot Freshness | None | Timestamp/correlation within threshold |
| T16 | Provenance Integrity | None | Classified values and explicit UNKNOWN |
| T17 | Evidence Journal | None | Persistent journal integrity/gap report |
| T18 | Recovery / Reconnect | Disconnect/reconnect only when requested | Safe degradation + recovery before/after |
| T19 | Operator Control | Execute only requested operator control | Authenticated command through Guard/Trust/Safety |
| T20 | End-to-End Certification | Execute final private-server procedure | Complete reproducible evidence package |

## State machine

Every practical test follows:

`READY → PRECONDITIONS → OPERATOR_ACTION (if needed) → OBSERVE → VERIFY → PASS/FAIL/UNKNOWN/BLOCKED`

`PASS` requires concrete evidence. Missing evidence is never PASS.

`UNKNOWN` means the permitted observation boundary did not establish the fact. It is never converted into a privileged inference.

`BLOCKED` means the required capability or evidence contract is not yet implemented/available.

## Safety boundary

The Test Center never bypasses:

`Guard → Trust → Safety → Execute → Verify`

The Dashboard can instruct or request operator actions, but runtime remains authoritative.

No test uses server DB, GM/mod/admin controls, server console, hidden server state, secret credentials, or privileged APIs.

## Implementation status

- Practical-test contract catalog: **T1–T20 implemented**.
- WPF Test Center: **implemented** and dynamically renders T1–T20.
- Live canonical snapshot monitor: **implemented**, target refresh 250 ms.
- T1–T4: **evaluable** against current canonical Gate 1 data.
- T5–T10: **evidence-gated**; current snapshot does not yet expose enough information to claim full live capability verification.
- T11–T16, T19: **system-level evidence gates implemented**.
- T17–T18: require persistent journal/reconnect execution evidence; they remain blocked until that evidence is actually exercised.
- T20: remains blocked until the physical private-server E2E procedure produces the complete evidence package.

## Explicit limits

T2 currently verifies client window visibility, not yet a captured Windows Graphics Capture frame.

T4 currently verifies decoded packet availability, not a complete WorldState.

T5–T9 cannot be marked PASS from the current Gate 1 snapshot merely because supporting subsystems exist elsewhere in the repository.

T20 is intentionally impossible to PASS from UI metadata alone: it requires the real private test client, physical operator actions where specified, runtime evidence, safety evidence, and reproducible build/package evidence.
