# NosAi — Architecture Baseline

## Purpose
This repository is the clean implementation source for NosAi. The previous repository `volobolo99/NosAi` is treated as a reference/archive source only.

## Project roles
- **Play AI** — execution-oriented player agent. It carries out approved plans/actions through game-interface adapters and reports observations/results.
- **Guard AI** — strategic protection/evaluation layer. It evaluates risk, uncertainty, constraints and proposed plans/actions; it may reject, constrain, downgrade or request reconsideration.
- **Progression Engine** — strategic planner. It determines the best progression path toward the current goal using state, predictions, resources, time, risk and validated strategy knowledge.
- **Knowledge Base** — persistent reusable strategy knowledge, evidence, statistics and mastery information shared across compatible character profiles.

These roles are separate and communicate through explicit contracts. No role silently bypasses the Safety Gate.

## Rules
1. No blind copy of legacy code.
2. Every imported component must first pass a source audit and contract review.
3. Safety boundaries are first-class architecture, not a later patch.
4. Local LLM inference is isolated behind a decision-provider interface.
5. Game input/output adapters remain outside the decision engine.
6. Simulation must be executable without the game client.
7. CI must validate contracts, safety, deterministic simulation, and provider fallback.
8. The first functional milestone is Play AI + Play Guard + Guard AI minimal bring-up and authenticated communication.
9. Rich game perception and advanced automation follow only after the minimal runtime path is reliable.
10. External specialist integrations are represented explicitly as interfaces/placeholders and marked `EXTERNAL_IMPLEMENTATION_REQUIRED` until reviewed and supplied.

## Target flow

```text
Game/Simulation Observation
          ↓
     WorldState
          ↓
 Progression Engine ←→ Knowledge Base
          ↓
     Plan / Decision Proposal
          ↓
       Guard AI
          ↓
     Safety Gate
          ↓
       Play AI
          ↓
 Game Interface Adapter
          ↓
     Execution Result
          ↓
 Observation / Telemetry → Knowledge Base
```

## Provider strategy
- Deterministic rule provider: baseline/fallback.
- Local LLM provider: optional accelerator for decision quality.
- Future providers must implement the same contract and cannot bypass Safety Gate.

## Runtime stages
1. Core contracts
2. Safety boundary
3. Deterministic simulator
4. Play/Guard minimal communication bring-up
5. Progression Engine contracts and deterministic planner
6. Knowledge Base and evidence-backed strategy lifecycle
7. Decision providers
8. Memory/telemetry
9. Read-only vision/observation
10. Game adapter
11. Local LLM optimization
12. Full runtime integration

## Migration policy
Useful legacy files may be reimplemented or imported selectively from `volobolo99/NosAi`, but only after verifying their actual contents. Historical reports are evidence, not source code truth.
