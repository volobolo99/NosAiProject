# NosAi — Architecture Baseline

## Purpose
This repository is the clean implementation source for NosAi. The previous repository `volobolo99/NosAi` is treated as a reference/archive source only.

## Rules
1. No blind copy of legacy code.
2. Every imported component must first pass a source audit and contract review.
3. Safety boundaries are first-class architecture, not a later patch.
4. Local LLM inference is isolated behind a decision-provider interface.
5. Game input/output adapters remain outside the decision engine.
6. Simulation must be executable without the game client.
7. CI must validate contracts, safety, deterministic simulation, and provider fallback.

## Target flow

Observation -> WorldState -> DecisionProvider -> Candidate/Decision -> Safety Gate -> Action Executor -> Telemetry

## Provider strategy
- Deterministic rule provider: baseline/fallback.
- Local LLM provider: optional accelerator for decision quality.
- Future providers must implement the same contract and cannot bypass Safety Gate.

## Runtime stages
1. Core contracts
2. Safety boundary
3. Deterministic simulator
4. Decision providers
5. Memory/telemetry
6. Read-only vision/observation
7. Game adapter
8. Local LLM optimization
9. Full runtime integration

## Migration policy
Useful legacy files may be reimplemented or imported selectively from `volobolo99/NosAi`, but only after verifying their actual contents. Historical reports are evidence, not source code truth.
