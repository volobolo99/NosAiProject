# NosAiProject

Clean-source implementation of NosAi, an AI runtime for NosTale.

## Status
**Gate 0 — Clean foundation in progress.**

This repository is the new development source. The legacy repository [`volobolo99/NosAi`](https://github.com/volobolo99/NosAi) is used only as a reference/source library. Legacy code is never copied blindly: each component is audited, reimplemented or migrated selectively, then covered by tests.

## Architecture
See `docs/ARCHITECTURE.md` for runtime boundaries and migration rules.

## Roadmap
See `docs/ROADMAP.md` for implementation gates.

## Priorities
1. Safety and fail-closed execution.
2. Deterministic simulation and testability.
3. Stable contracts between perception, decision, memory and execution.
4. Local LLM as an isolated decision provider, never a privileged executor.
5. Hardware-specific optimization only after functional correctness is proven.
