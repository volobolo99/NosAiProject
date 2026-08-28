# NosTale tactical simulation data model

## Purpose

The simulator must not pretend that a small combat formula is a complete model of NosTale. Planning quality depends on the quality and freshness of the world data.

## Evidence currently identified

- Official 2026 patch notes show that specialist cards, buffs/debuffs, raids and multi-phase raid mechanics materially affect gameplay.
- Official updates also show level/quest/Time-Space requirements changing over time.
- Public emulator/data projects expose useful structured categories including items, effects, monsters, maps, quests and skills.
- `taletool` identifies client-data formats such as `Item.dat`, `monster.dat`, `Skill.dat`, `quest.dat`, `MapIDData.dat` and `MapPointData.dat`.
- `NosSmooth` exposes structured skills/items/monsters and packet abstractions, useful as a research reference rather than as a dependency of the simulator.

## Required model layers

1. **Character state** — class, level, job level, champion level, HP/MP, equipment, specialist, fairy, resistances, buffs/debuffs.
2. **Action state** — skills, cooldowns, cast/recovery time, range, target rules, resource cost, hit probability and effects.
3. **Combat state** — monsters, HP, attack/defence, element, resistances, AI/aggro, skills, spawn and death state.
4. **World state** — map nodes, coordinates, movement cost, doors, hazards, NPCs, portals and encounter placement.
5. **Objective state** — quests, Time-Spaces, raids, collection, survival and reach conditions; time limits and failure penalties.
6. **Economy/resource state** — consumables, ammunition/resources, durability/cost, expected reward and opportunity cost.
7. **Party state** — members, roles, buffs, target coordination, aggro and synchronized mechanics.
8. **Knowledge state** — strategy candidates, evidence, confidence, sample count, patch/version and validation status.

## Simulation metrics

Every candidate plan should expose at least:

- success probability
- expected completion time
- worst/percentile completion time
- expected resource consumption
- expected damage/HP loss
- death/failure probability
- reward/value estimate
- confidence/evidence quality

The optimization target is therefore not simply maximum DPS. The default objective is minimum expected progression time subject to an acceptable failure/risk budget and resource budget.

## Data provenance

Each imported fact should carry source, game version/patch, timestamp and verification status. Public emulator repositories are reference material and may be stale; they must not be treated as authoritative for the live game without verification.

## Deliberate boundary

This document defines data needed by the offline simulator. It does not implement packet manipulation, client injection, anti-cheat evasion or bypass mechanisms.
