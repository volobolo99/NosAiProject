# NosAi — Advanced Tactical Simulation

## Purpose

The tactical simulator is the offline laboratory for Guard AI and the Progression Engine. It must answer:

> Given the observed state, objective, available resources and known strategy evidence, which action sequence maximizes expected progression while minimizing time, resource waste and failure risk?

The simulator is intentionally independent of the live game client. It can therefore be used for CI, regression tests, strategy discovery and benchmarking before any live adapter exists.

## Research basis

The official NosTale Game Guide documents the core systems that the model must eventually represent: controls, Time-Space Stones, trade, skills, specialists, fairies, PvP, raids, arenas, groups, items, upgrades, NosMates and companions.

Important mechanics already identified for the model include:

- Four elemental types and an elemental relationship cycle; elemental attacks interact with resistance.
- Specialists change the available combat skill set and use their own transformation/job progression.
- Time-Space content has mission-specific state such as HP/lives, score, objectives and time limits.
- Raids have party/level constraints and reward conditions.
- The interface exposes equipment, consumables, combat skills and other actions that affect the decision space.

These facts are source requirements, not assumptions about undocumented formulas. Exact damage, cooldown, AI, map and reward formulas must be calibrated from verified observations or authoritative data before being treated as ground truth.

## GitHub research

Public repositories were reviewed for reusable ideas and data sources. Useful examples include:

- `Vanosilla/Vanosilla`: a NosTale emulator containing server code and client data such as items, effects, monsters and maps. It is valuable as a reference for data schemas and game-state concepts, not as an automatic source of truth for the live game.
- `wojtas99/Nostale_Bot`: demonstrates waypoint/targeting/loot concepts, but is not adopted as the decision architecture.
- `Bappsack/OwO-Maker`: demonstrates memory-oriented game-state extraction for minigames, useful as an architectural reference for a future read-only perception adapter.
- `BlowaXD/SaltyEmu`: demonstrates an event-driven emulator architecture and modular separation that can inform simulator boundaries.

No external repository is copied blindly. The NosAi clean repository remains the implementation source.

## Simulator architecture

```text
                    ┌────────────────────────┐
                    │   Scenario / Objective │
                    └───────────┬────────────┘
                                ↓
                    ┌────────────────────────┐
                    │      TacticalState     │
                    │ HP/MP • enemies • time │
                    │ resources • progress   │
                    └───────────┬────────────┘
                                ↓
                    ┌────────────────────────┐
                    │    Action Generator    │
                    │ attack/heal/advance... │
                    └───────────┬────────────┘
                                ↓
              ┌─────────────────┴─────────────────┐
              ↓                                   ↓
       deterministic                    stochastic rollouts
       transition model                 confidence estimation
              └─────────────────┬─────────────────┘
                                ↓
                    ┌────────────────────────┐
                    │  Risk-adjusted planner │
                    │ beam search / rollout  │
                    └───────────┬────────────┘
                                ↓
                         Best Plan +
                    success probability
```

## Current engine

`app/simulation/tactical.py` currently provides:

1. Explicit `Combatant`, `TacticalState` and `TacticalAction` contracts.
2. Elemental advantage/resistance handling.
3. Resource and time accounting.
4. Bounded enemy counter-actions.
5. Seeded stochastic transitions for reproducible experiments.
6. Monte-Carlo action evaluation.
7. Risk-adjusted beam-search planning.
8. Expected success probability and expected resource/time cost.

This is the first simulation kernel, not yet a complete NosTale simulator.

## Target advanced model

The next layers should add:

### 1. World model

- map graph and movement cost
- NPCs, portals, objects and interaction requirements
- monsters, spawn rules and aggro state
- party/family state
- mission/quest state
- Time-Space rooms, timers, score and failure conditions
- raid mechanics and phase state

### 2. Character model

- base class and specialist
- level/job level
- HP/MP/energy/resources
- equipment and upgrades
- fairy/element
- resistances
- buffs/debuffs
- skill cooldowns and cast times
- pet/partner state

### 3. Decision model

For every candidate action compute:

`expected_progress - time_cost - resource_cost - risk_penalty - failure_cost`

with confidence intervals where enough samples exist.

### 4. Search algorithms

The architecture should support multiple planners rather than one hard-coded algorithm:

- greedy baseline
- beam search
- Monte-Carlo rollouts
- Monte-Carlo Tree Search
- A* for deterministic map objectives
- dynamic programming for finite mission subproblems
- Pareto optimization for time vs resources vs risk

Guard AI can choose the planner according to scenario complexity and available compute budget.

### 5. Strategy memory

Every validated strategy should be keyed by context:

`class × level-range × specialist × build × objective × environment × constraints`

Store:

- action sequence/policy
- expected completion time
- success rate
- resource consumption
- observed variance
- sample count
- confidence
- version and evidence

A strategy is promoted only after repeated validation. New results should compete against the stored baseline rather than overwrite it blindly.

### 6. Mastery / perfection score

The score should measure distance from the best validated baseline, not claim absolute mathematical perfection:

`mastery = weighted performance / validated reference performance`

Separate dimensions should include time efficiency, success probability, resource efficiency, damage/HP efficiency and objective completion quality.

## Calibration rule

Simulation is only as good as its model. Every uncertain mechanic is tagged with one of:

- `VERIFIED_OFFICIAL`
- `VERIFIED_OBSERVATION`
- `INFERRED`
- `UNKNOWN`

Planner confidence must decrease when decisions depend on `INFERRED` or `UNKNOWN` parameters.

## Safety boundary

The simulator never sends input to a game client, changes process memory, injects code, manipulates packets or attempts anti-cheat bypass. Those capabilities remain separate external integration points in the project architecture. The simulator's job is to make the decision layer testable and quantitatively strong before a live adapter is connected.
