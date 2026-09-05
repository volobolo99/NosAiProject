# AP-00 / A3 — Claude — AI Budget Policy

## GOAL
Define and implement bounded inference/capture/memory/I/O budgets for the target laptop.

## READ
`docs/ROADMAP_ESECUTIVA.md`; `docs/adr/ADR-0022-hybrid-cognitive-control-loop.md`; `nosai/runtime/hardware.py`.

## OWNED SCOPE
Only budget-policy contracts/implementation and direct tests. No Gate3, Dashboard or perception pipeline files.

## REQUIREMENTS
Tier 0 deterministic → Tier 1 lightweight ML → Tier 2 GPU vision/embeddings → Tier 3 expensive reasoning. Jobs declare resource/deadline budgets. Tier 3 cannot block Safety/recovery. Use bounded queues and explicit degradation.

## DELIVERY
Complete compilable/tested files; no placeholders; handoff evidence to A6.
