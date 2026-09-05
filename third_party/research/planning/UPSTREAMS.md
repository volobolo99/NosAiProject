# Planning / Decision — Upstream Research

## luxkun/ReGoap
- License: Apache-2.0 (verified)
- Upstream commit: 69eeea4a5489506b2e0d3f2db4a02c288d8d38fa
- Local code: third_party/sources/luxkun/ReGoap/reference/
- Added locally: planner, node, planner settings, action/agent/goal/memory/sensor interfaces, conditions.
- NosAi use: deterministic GOAP candidate generation and action planning.
- Target: src/NosAi.Core/Planning/Goap/
- Priority: VERY HIGH.
- Adaptation rule: preserve deterministic planning for identical state/configuration.

## ptrefall/fluid-hierarchical-task-network
- License: MIT (verified)
- Upstream commit: e67af264cfdf240053f392d4e0e6c620c454eb97
- Local code: third_party/sources/ptrefall/FluidHTN/reference/
- Added locally: Planner, Domain, DomainBuilder, BaseDomainBuilder, LICENSE.
- NosAi use: HTN decomposition for progression, Time-Space, quest chains and long-horizon skills.
- Target: src/NosAi.Core/Planning/Htn/
- Priority: VERY HIGH.

Recommended hybrid:
Strategic Orchestrator -> HTN decomposition -> GOAP executable sequence -> Reactive interruption rules -> Guard/Trust/Safety.
