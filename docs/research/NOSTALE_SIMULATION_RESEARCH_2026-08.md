# NosAi — NosTale Tactical Simulation Research (2026-08)

## Executive conclusion

The simulator should be a **hybrid evidence-driven model**, not a single damage formula. NosTale exposes several coupled systems: character progression, jobs/specialists, elemental combat, equipment, pets/mates, quests, Time-Spaces, raids, maps and timed objectives.

The official game guide documents the gameplay surfaces and the official forum documents current patch changes. Public GitHub projects are useful for extracting structured client/data concepts, but are not authoritative for the live service and must be version-tagged and independently verified.

## Evidence map

| Area | Evidence | Simulation implication | Authority |
|---|---|---|---|
| UI / controls | Official game guide documents movement, minimap, selection, attack, inventory, skills, quest UI and event UI | Input/observation schema must represent both world state and UI-derived state | Official guide |
| Classes | Official guide lists Swordsman, Archer and Mage plus specialist gameplay | Character model needs base class + specialist + job state | Official guide |
| Specialist | Official guide describes transformation, element-specific skills, SP levels, points and requirements | Specialist state must be explicit and versioned | Official guide |
| Elements | Official guide describes fire/water/light/dark/none and elemental advantage/resistance | Damage model must separate physical and elemental components | Official guide |
| Damage | Community technical formula describes physical, elemental, morale, equipment, SP, rune, pet, buff/debuff and resistance contributions | Formula engine must be modular and confidence-scored rather than hard-coded as one opaque equation | Community research; verify experimentally |
| Time-Spaces | Official guide describes maps, exits, monsters, levers, crystal choices, timers, lives, scoring and rewards | Time-Space planner must be a graph/goal planner, not only combat DPS optimization | Official guide |
| Raids | Official guide describes 5–15 players, timed mechanics, tasks, lives and boss phase | Raid simulator needs multi-agent roles, synchronization and failure states | Official guide |
| Client data | taletool recognizes Item.dat, BCard.dat, Skill.dat, monster.dat, quest.dat, MapIDData.dat, MapPointData.dat and related files | Build a versioned importer/data-normalization layer | Open-source research tool |
| Runtime state | NosSmooth describes high-level character/map/entity/mate/skill/family/group state, packets and A* pathfinding | Observation adapter should target a normalized state independent of the eventual client connector | Open-source reference |
| Current content | Official 2026 patch notes add SP12 and new content | Knowledge base needs patch/version metadata and invalidation | Official patch notes |

## Critical design changes from a simple simulator

### 1. Separate prediction from decision

The simulator predicts distributions of outcomes. Guard AI then selects an action using a risk-aware objective. This prevents the model from confusing “high expected damage” with “best progression action”.

### 2. Use a multi-objective score

Default optimization order:

1. satisfy the objective;
2. minimize probability of irreversible failure/death;
3. minimize expected completion time;
4. minimize resource consumption and item loss;
5. maximize progression/reward value;
6. maximize secondary score/efficiency.

The weights must be configurable per objective type.

### 3. Model uncertainty explicitly

Every parameter needs:

- value
- source
- patch/version
- timestamp
- confidence
- verification status
- optional empirical sample count

A strategy based on uncertain data must never be ranked as equivalent to an experimentally confirmed strategy.

### 4. Add a belief/state-estimation layer

The eventual live observation will be incomplete. The planner should therefore operate on a **belief state** containing known values, estimates and confidence intervals. When new observations arrive, the belief state is updated before replanning.

### 5. Plan hierarchically

Use three levels:

- **Strategic:** which quest/Time-Space/raid/farming route should be attempted next?
- **Tactical:** which route, target priority, SP, buff sequence and resource policy?
- **Reactive:** which immediate movement/skill/target action is safest and fastest?

### 6. Replan continuously

The plan is not a script. After meaningful state changes (unexpected mob, cooldown, HP threshold, path blockage, failed action, new objective information), Guard AI should re-evaluate the remaining plan.

## Data acquisition priority

### P0 — mandatory

- character stats and progression
- current class/SP/job state
- equipment and relevant effects
- skills and cooldowns
- HP/MP/status effects
- target HP/level/element/resistance
- map position and nearby entities
- objective/timer/lives

### P1 — high value

- full map graph and movement costs
- quest prerequisites and rewards
- Time-Space room graph, exits, levers, crystals, spawn rules and scoring
- raid mechanics, phases and role requirements
- item costs and consumables

### P2 — optimization

- drop distributions
- market/economy value
- alternative equipment build space
- party composition statistics
- historical successful runs
- empirical latency/action-duration distributions

## Simulator architecture

```text
Verified Game Data
      ↓
Versioned Data Normalizer
      ↓
World / Character / Objective Model
      ↓
State Estimator (belief + confidence)
      ↓
Predictive Simulation Engine
      ↓
Candidate Plan Generator
      ↓
Risk-Aware Optimizer
      ↓
Strategy Memory / Benchmark DB
      ↓
Guard AI recommendation
      ↓
Play AI execution adapter
```

## Important boundary

The research and simulator are designed as an offline planning/data layer. No client bypass, anti-cheat evasion or packet manipulation is treated as a prerequisite for the simulation engine. Any future client integration should feed normalized observations into this architecture without coupling the planner to one extraction mechanism.

## Sources used for this research

- Official NosTale Game Guide: gameplay, controls, classes, specialists, elements, Time-Spaces, raids and upgrades.
- Official NosTale forum: 2026 patch notes, including Act 10 Part 2 / SP12.
- Community damage-formula research on the official forum.
- GitHub `imxeno/taletool`: structured NosTale client data formats.
- GitHub `Rutherther/NosSmooth`: normalized game-state, packet, combat and pathfinding abstractions.

These sources are references with different authority levels. The simulator must retain provenance instead of merging them into an unqualified “truth” dataset.
