# NosAi — Progression Engine Specification

## Status
Design baseline — Step 3B.2.

## Purpose
The Progression Engine is the strategic planner responsible for maximizing character progression toward an explicit goal while minimizing time, resource waste, failure probability, and unnecessary risk.

It does not directly control the game. It produces plans and scored candidate objectives/actions for Play AI, while Guard AI evaluates strategy, risk, constraints, and protection requirements.

## Design principle
NosAi should behave as a highly optimized player: it observes the current game state, predicts likely outcomes, evaluates alternatives, selects the best expected progression path, executes through Play AI, measures the result, and stores validated knowledge for reuse.

## Optimization objective
The planner evaluates candidate paths using a configurable utility function built from:

- progress toward the current objective;
- expected time to completion;
- expected resource consumption and item loss;
- probability of success/failure;
- risk of death or unrecoverable state;
- future value such as unlocks, equipment, materials, quests, and progression dependencies;
- confidence in the underlying strategy knowledge.

No single metric such as XP/hour is sufficient.

## Runtime flow

```text
Game Observation
      ↓
Normalized WorldState
      ↓
Goal + Character Profile + Known Strategies
      ↓
Candidate generation
      ↓
Outcome prediction / scoring
      ↓
Guard AI risk & constraint evaluation
      ↓
Best plan / next action proposal
      ↓
Play AI execution
      ↓
Observed result
      ↓
Evaluation + telemetry
      ↓
Knowledge Base update
```

## Core concepts

### Goal
A goal contains the desired outcome, priority, constraints, deadline/time preference, and acceptable risk/resource limits.

Examples include level progression, completing a quest chain, clearing a dungeon, obtaining an item, improving equipment, or reaching a target build state.

### Candidate path
A candidate path is a sequence or short horizon of possible activities. Each candidate carries predicted duration, success probability, resource cost, risk, expected progression value, and confidence.

### Strategy
A strategy is reusable knowledge describing how a character profile should approach a specific context. Strategy keys should include, where available:

- character class/category;
- level range;
- build/equipment profile;
- content/activity;
- objective;
- relevant party/context conditions.

Strategies are versioned and must retain evidence and validation statistics.

## Knowledge lifecycle

```text
Observed / proposed
        ↓
Experimental strategy
        ↓ sufficient evidence
Validated strategy
        ↓ repeated superior results
Preferred strategy
        ↓ regression detected
Demoted / re-evaluated
```

The system must never overwrite validated knowledge merely because one new run performed better or worse. Statistical evidence and reproducibility are required.

## Transfer to new characters
Knowledge is shared at the strategy level rather than tied permanently to a single character. A new character can inherit applicable validated strategies and then personalize them using its own build, equipment, resources, and observed performance.

## Mastery Score
The system exposes a 0–100 Mastery Score representing how closely current behavior approaches the best validated strategy/reference for the evaluated context.

Mastery is contextual, not merely global. At minimum the data model should support:

- global mastery;
- class/category mastery;
- level-range mastery;
- activity/content mastery;
- objective-specific mastery.

The score must be evidence-based and include enough metadata to explain why it changed. It is not a claim of absolute game perfection.

## Guard AI boundary
Guard AI is not a second Play AI. It acts as the strategic protection and evaluation layer. It can reject, constrain, downgrade, or request reconsideration of a proposed plan/action when risk, uncertainty, resource limits, or safety constraints are violated.

## Play AI boundary
Play AI is the execution-oriented agent. It receives approved goals/plans/actions and is responsible for carrying them out through the available game interface adapters. It reports observations and execution outcomes back to the planning stack.

## Required contracts
The implementation should expose explicit contracts for:

- `Goal`
- `CharacterProfile`
- `ProgressionState`
- `CandidatePath`
- `StrategyRecord`
- `Prediction`
- `RiskAssessment`
- `MasterySnapshot`
- `ExecutionResult`

These contracts should remain transport-agnostic and testable without a game client.

## Deterministic-first requirement
The first implementation must work with synthetic/simulated WorldState data. A game-client adapter is an input source, not a prerequisite for testing the planner.

## Non-goals for this step
- direct game input implementation;
- packet manipulation;
- anti-cheat bypass;
- client bypass;
- production vision pipeline;
- final LLM tuning.

Those remain explicit integration boundaries where required and must not be silently removed from the architecture.
