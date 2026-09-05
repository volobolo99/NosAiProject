# Optimization / Resource & Build Planning — Upstream Research

## google/or-tools
- License: Apache-2.0 (verified)
- Role: constraint programming, routing, integer optimization.
- NosAi use candidates:
  - equipment/build selection under constraints
  - inventory/resource allocation
  - route/visit optimization where graph search alone is insufficient
  - progression scheduling and resource planning
- Target:
  - src/NosAi.Core/Optimization/
  - Character Build Optimizer
  - Inventory & Resource Planner
- Priority: VERY HIGH.
- Strategy: consume official package/bindings; do not vendor full source.
