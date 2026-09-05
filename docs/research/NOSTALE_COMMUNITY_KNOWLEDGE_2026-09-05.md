# NosTale Community Knowledge Research — 2026-09-05

**Status:** research corpus / candidate knowledge, NOT automatic gameplay truth.

## Purpose

Collect player-discovered tactics, shortcuts, hidden mechanics, farming heuristics, route knowledge, raid execution advice and practical observations from public NosTale communities. These records are inputs to NosAi's Knowledge/Strategy Memory. They must be normalized, versioned and experimentally validated before becoming `Verified` live strategy.

## Evidence policy

- Official Gameforge material is authoritative for documented mechanics.
- Community guides are valuable empirical evidence but may be outdated, server-specific or opinionated.
- Reddit/forum claims are stored as hypotheses unless corroborated.
- Bugs/exploits/cheats are not promoted to gameplay strategies. NosTale rules explicitly prohibit exploiting bugs and manipulating the client. See official rules.
- No server DB, GM/admin state, hidden flags or privileged data may be used by NosAi.

## Findings worth feeding to the Knowledge/Strategy Memory

### 1. Hidden Time-Space discovery
Official game guidance documents hidden Time-Spaces: a dowsing stick is used together with a Time-Space fragment; a question mark means too far away, an arrow indicates direction, and an exclamation mark/activated energy square indicates the correct location. This is a canonical example of a discoverable signal chain and should be modeled as `HiddenLocationSignal` rather than a hardcoded coordinate.

Community guides add operational details such as specific hidden TS routes, required materials and entry conditions. These are useful candidate route knowledge and must be tagged with map/ruleset/version.

Sources: Gameforge Game Guide (hidden Time-Space); official community forum guide on individual hidden TS; Spider Raid/Seal guide.

### 2. Hidden TS interaction edge cases
Community discussion reports that a hidden TS may require interaction with the created stone rather than simply walking into it, and that ownership/group context can affect who can enter. Treat this as a context-sensitive interaction hypothesis and verify on the target private server.

### 3. Efficient early progression
A community guide recommends prioritizing job level early because profession XP efficiency changes with combat level, then using Time-Spaces for combat progression. It also recommends doing board missions early because they provide money, potions and reputation, and notes class/element-specific fairy choices. These are strategy candidates, not universal truths.

### 4. Raid button/path knowledge
The official community forum contains detailed Spider Raid button sequencing. Some buttons are safe, some teleport, and some spawn monsters; several can be skipped or timed so the party avoids unnecessary combat. This is an excellent example of procedural strategy that can be represented as a conditional action graph.

### 5. Raid role composition and debuff sequencing
Community raid guides describe role-specialized teams and sequencing of debuffs. For Desert Robber, the guide recommends tank/DPS/debuffer/support roles and gives examples of when to apply Elemental Leech, Poison Gas and Armor Break. NosAi should store these as contextual team-composition hypotheses and learn measured success/time/risk rather than treating forum advice as absolute.

### 6. Boss-specific positioning and selective engagement
The Desert Robber guide recommends positioning the tank near the entrance and focusing on the boss rather than unnecessarily fighting every monster in the room. This illustrates a general tactical pattern: `objective_priority > incidental_enemy_clear` when incidental combat has negative utility.

### 7. Class/build optimization from community experience
A 2024 Archer discussion compares investment in dagger/bow upgrades and specialist choices under a limited budget. The advice favors concentrating scarce resources rather than spreading upgrades across multiple items. This supports an `OpportunityCost` feature in the equipment optimizer.

### 8. Resistance/build stacking observations
A community Swordsman guide presents measured examples where long-range damage reductions from multiple equipment effects combine. Such claims should be stored as formulas/hypotheses with the original observed setup and re-tested, because game balance and item systems evolve.

### 9. SP-specific resource preparation
The SP8 community guide lists preparation items, resistance requirements and low-drop-rate materials. This is useful for `ResourceLedger`, `QuestDependencyGraph` and acquisition planning: the system can predict material bottlenecks before starting the quest.

### 10. Fafnir raid preparation
The community guide recommends specific preparation such as Viking costume, selective HP equipment, pets/companions, guardians and consumables, based on the boss's mechanics. This should become a context-dependent raid preparation checklist only after validation against the active ruleset.

### 11. Current meta changes must be treated as volatile
Recent community discussions show that efficient leveling and gold-making recommendations change with new acts, level gates and patches. Examples include discussions about LoL access and modern solo/duo map farming. Therefore `LastValidated`, `RulesetVersion`, `SourceDate` and `EvidenceCount` are mandatory for strategy records.

### 12. Patch changes can invalidate community knowledge
Official 2025–2026 changelogs have changed drop tables, raid mechanics, item behavior, map mechanics and other systems. Example: the February 2026 changelog removed general item drops from Acts 8/9, restored group Time-Space pieces to Act 10 maps, and fixed/changed several raid/map behaviors. A strategy memory entry must therefore be automatically marked `RevalidationRequired` when its dependent ruleset changes.

### 13. Raid QoL changes affect automation strategy
The March 2026 official raid QoL patch added a 63-slot raid inventory. This matters for inventory planning and loot organization and demonstrates why NosAi must not assume the old inventory model is permanent.

### 14. Community knowledge about economic optimization
Community discussions repeatedly compare farming, questing, LoL/maps, raid drops, equipment investments and Bazaar purchases. NosAi should convert these into measurable alternatives: expected gold/hour, expected item/hour, variance, travel cost, consumable cost, failure probability and opportunity cost.

## Knowledge classification

Every imported claim should receive one of:

- `DocumentedOfficial`
- `CommunityGuide`
- `CommunityObservation`
- `CommunityOpinion`
- `CandidateHypothesis`
- `Disputed`
- `Deprecated`
- `Rejected`

Only validated evidence can promote a claim toward `Verified`.

## Discovery categories for future crawling/research

1. hidden Time-Spaces and dowsing signals;
2. hidden/conditional NPC interactions;
3. quest prerequisites and shortcuts that remain legitimate;
4. map routes, safe corridors and portal ordering;
5. raid button/order/positioning mechanics;
6. boss attack telegraphs and safe zones;
7. class/SP matchup knowledge;
8. skill sequencing and cooldown windows;
9. resistance/element interactions;
10. equipment breakpoints and opportunity cost;
11. drop locations and empirical drop-rate observations;
12. crafting/production bottlenecks;
13. Bazaar price-vs-farm decisions;
14. inventory organization patterns;
15. pet/NosMate tactical usage;
16. Time-Space scoring and route optimization;
17. event-specific temporary mechanics;
18. map-specific farming patterns;
19. recovery patterns after failed mechanics;
20. historical strategies that may have become obsolete.

## Important distinction: secret knowledge vs exploit

NosAi may exploit **knowledge asymmetry** (remembering community discoveries, correlating signals, predicting outcomes from observed data, choosing better routes). It must not exploit software vulnerabilities, manipulate the client, bypass cooldowns, inject packets, reveal server-only state or use bugs as gameplay advantages. The official game rules explicitly prohibit bug abuse and client manipulation.

## Research interpretation for NosAi

The corpus should feed this pipeline:

`Public Research → Source Classification → Knowledge Normalization → Candidate Strategy → Simulation/Ranking → Real Client Validation → Outcome Ledger → Confidence Update → Strategy Memory → Cross-Character Reuse`

A community claim is therefore a **prior**, not an instruction.

## Primary/community sources consulted

- Official Gameforge Game Guide: hidden Time-Spaces, item systems and core mechanics.
- Official NosTale forum: Guide & Tutorials and completed/in-progress community guides.
- Official NosTale forum: Spider Raid & Seal Time-Spaces.
- Official NosTale forum: Desert Robber Band Raid Guide.
- Official NosTale forum: early leveling guide.
- Official NosTale forum: SP8 raid/quest preparation guide.
- Official NosTale forum: Archer equipment/SP optimization discussion.
- Reddit r/nostale: current returning-player/meta/progression discussions, used only as community evidence.

## Current research examples

- Hidden TS signals and dowsing workflow: Gameforge guide.
- Individual hidden TS use and low-level accessibility: official community forum guide.
- Spider Raid button ordering and skip/timing tactics: official community forum guide.
- Desert Robber team roles, button traps and boss targeting: official community forum guide.
- Early progression and board/TS strategy: official community forum guide.
- Current meta uncertainty: recent Reddit discussions.

## Implementation requirement

Do not hardcode these claims into the live controller. Import them into the adaptive knowledge store with source URL, source date, ruleset scope, confidence, evidence count and validation status. The planner may retrieve them as candidate strategies; Guard/Safety remains authoritative.
