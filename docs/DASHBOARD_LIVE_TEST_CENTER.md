# NosAi Dashboard — Live Data & Practical Test Center

Version: 1.1  
Date: 2026-09-05  
Status: CANONICAL PRODUCT REQUIREMENT

## Objective

The Windows `.exe` Dashboard/Control Panel must be the operator's real-time observability and practical-test surface.

It must show the latest data actually observed by the runtime. It must never manufacture gameplay state, replace unavailable values with zero, or treat a cached/predicted value as live.

## Live data contract

Every gameplay-relevant value shown by the Dashboard must have:

- source/provenance;
- observation timestamp;
- freshness/age;
- confidence where applicable;
- state classification (`Observed`, `Derived`, `Predicted`, `Cached`, `Unknown`);
- an explicit reason when `Unknown`.

The Dashboard reads the canonical runtime snapshot. It does not independently invent a second WorldState.

Target operator refresh cadence: 250 ms for live status surfaces, subject to the runtime's bounded observation/update budget. Heavy perception, OCR and inference work must remain off the UI thread.

## Practical Test Center

The Dashboard must expose a dedicated Test Center with the following ten pillars:

| ID | Test | Operator action | Evidence required |
|---|---|---|---|
| T1 | Attach & Live Observation | Start the private test client when requested | Attached process + fresh client observation |
| T2 | Screen / Vision | Keep the client window visible and perform the requested visual scenario | Screen/window observation + ROI/detection result |
| T3 | Network Observation | Generate normal client traffic when requested | Client-visible traffic counters/timestamps |
| T4 | World Model | Change a visible game state when requested | Updated WorldState with provenance |
| T5 | Navigation | Move to the requested controlled test area | Position/path/replan evidence |
| T6 | Combat | Enter the controlled combat scenario | Target/decision/Guard/execute/verify chain |
| T7 | Quest / Interaction | Perform the requested observable interaction | Before/after observation and verification |
| T8 | Character / Inventory | Open/use the requested observable character or inventory state | State delta and provenance |
| T9 | Autonomous Loop | Allow the runtime to operate in the declared test scenario | Observe → plan → guard → safety → execute → verify → re-observe |
| T10 | Resilience / Safety | Perform the explicitly requested controlled perturbation | Fail-closed + watchdog/recovery evidence |

## Operator workflow

A practical test must be executable as a state machine:

`READY → PRECONDITIONS → OPERATOR_ACTION (if needed) → OBSERVE → VERIFY → PASS/FAIL/UNKNOWN/BLOCKED`

The operator must always see what action is required. If no human action is required, the Dashboard must say so.

A test is **PASS** only when the runtime has concrete evidence. Missing evidence is not PASS.

## Safety

The Test Center never bypasses:

`Guard → Trust → Safety → Execute → Verify`

A practical test may request an action, but the runtime remains the authority to allow or refuse it.

No test may use server DB, GM/mod/admin controls, hidden server state, secret credentials, or privileged APIs.

## Current implementation status

- Core practical-test contracts: implemented.
- T1–T4 live snapshot evaluator: implemented in `LivePracticalTestService`.
- Existing certification suites: already exposed by the Control Panel.
- WPF Live Test Center window: implemented and reachable with `Ctrl+F9`.
- Live monitor polling target: 250 ms in the Test Center.
- T1–T4 are evaluable from canonical `/api/gate1` data; T5–T10 remain `BLOCKED` until their concrete live verification paths are implemented.

## Explicit limits

T2 currently verifies that the client window is detected and visible; this is **not yet proof of an acquired Windows Graphics Capture frame**.

T4 currently verifies decoded packet availability; this is **not yet proof of a complete WorldState**.

The repository must not claim live-game verification until the corresponding capabilities are physically exercised against the private test client.
