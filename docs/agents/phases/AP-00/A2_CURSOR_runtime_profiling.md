# AP-00 / A2 — Cursor — Runtime Profiling

## GOAL
Harden the existing hardware profiler and runtime capability detection without changing public boundaries.

## READ
`docs/ROADMAP_ESECUTIVA.md`; `nosai/runtime/hardware.py`; `nosai/runtime/__init__.py`; hardware documentation.

## OWNED SCOPE
Python runtime profiling/tooling under `nosai/runtime/` and its direct tests only. Do not edit C# runtime or agent orchestration docs.

## REQUIREMENTS
Detect actual CPU/GPU/VRAM/RAM/driver/thermal/storage capabilities at runtime. Produce deterministic, serializable snapshots. Gracefully represent unavailable telemetry as UNKNOWN. Keep inference tiers resource-aware for 16 GB RAM/8 GB-class VRAM.

## DELIVERY
Complete files only; run Python tests/type checks available in repository; report exact results and assumptions.
