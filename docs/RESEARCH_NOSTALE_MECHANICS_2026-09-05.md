# NosTale Mechanics Research — NosAiProject

**Date:** 2026-09-05  
**Purpose:** canonical research input for autonomous character progression in the private educational/test environment.

## 1. Product objective

NosAi must be able to start from a newly created ordinary player character, observe the client-visible state, and autonomously progress it using only permitted ordinary-client capabilities: screen/pixels, client-visible network traffic, legitimately readable local client memory, local telemetry, and mouse/keyboard input. It must not depend on server databases, GM/mod/admin tools, server consoles, privileged APIs, hidden state, or secret credentials.

The university demonstration target is an end-to-end character journey: create a character from the normal creation flow (nickname + available appearance/sex choices), then let NosAi play the character and demonstrate real progress on the character itself. Progression must remain human-plausible: no cheats/hacks, no impossible actions, no hidden server truth. NosAi may use all permitted observations to reduce wasted time/resources and make better decisions than a typical human player.

## 2. Confirmed game-mechanics families

### Character creation and class progression
- Official Gameforge game guide documents the normal Adventurer start and later class change. The classic progression changes from Adventurer to Swordsman, Archer or Mage after combat level 15 and job level 20; the NPCs involved are Mimi Mentor in NosVille or Lucia at Port Alveus market.
- Gameforge's 2026 NosFire rules also confirm four classes exist in current game variants: Swordsman, Archer, Mage and Martial Artist. NosFire is a special server configuration and must not be blindly treated as the normal progression baseline.
- A 2024 official Gameforge guide describes character creation as including sex, hairstyle and hair-color choices.

### Interface and observable state
The official guide exposes ordinary client surfaces relevant to perception:
- character panel: name, combat level, HP/MP, equipment and NosMate information;
- target panel: target name, race, element, combat level, HP/MP;
- minimap: current position, exits, NPC markers and selected destination;
- mission window/help: current main/secondary missions and story progress;
- dialog window: previously completed chapter dialogs;
- inventory: equipment, commonly used items/quest items, materials/food and specialist/costume storage areas;
- quick slots for skills/items;
- chat and interaction UI.

These are high-value ordinary-client observation sources for the World Model.

### Inventory management
Inventory is not just a bag. NosAi should model at least:
- equipment slots;
- consumables;
- quest items;
- crafting/material items;
- specialist cards;
- costumes;
- raid/additional inventories where available;
- stack quantities;
- item identity, level, rarity, options/effects, binding/ownership and usability constraints;
- reserved quantities for active quests, planned upgrades and emergency consumables.

The March/May 2026 official updates added/expanded raid inventory behavior, including a 63-slot raid inventory, direct dropping, Bazaar registration from raid inventory, scrolling and stack splitting. The inventory model must therefore be extensible rather than assuming a single fixed bag.

### Equipment and upgrade decision making
The official guide confirms several independent equipment-improvement systems:
- rarity/betting;
- reinforcement/upgrade;
- jewellery refinement with Cellon options;
- combining gloves/shoes and resistance progression;
- stones and stone options;
- shell/effect systems;
- specialist-card upgrades.

NosAi must never treat "stronger item" as a single scalar. Equipment decisions need a context-sensitive utility model including character class, current/target level, SP, elemental matchup, resistance requirements, survivability, damage, movement/clear speed, resource cost, failure probability, replacement horizon, and opportunity cost.

The official guide documents rarity from -2 through +7 on the general system page; official forum material also confirms rarity/upgrade systems and that current systems can extend to champion gear. Current values must be versioned because the live game evolves.

### Upgrade economics
The official guide states, for example, that rarity betting can consume gold and Cella Powder and that reinforcement improves attack/defence. Combination and refinement can consume materials and have failure consequences. Therefore the planner needs:
- expected value;
- probability of success/failure;
- material consumption;
- replacement cost;
- current liquidity;
- future quest/upgrade requirements;
- opportunity cost of spending now versus farming first.

NosAi should have a hard safety rule: do not spend a scarce resource on a low-confidence upgrade merely because the upgrade is technically available.

### Specialist Cards / transformations
Specialists are a core autonomous decision surface. The official guide confirms:
- transformation uses a specialist card and the normal transformation control/key;
- the correct fairy is required;
- transformation has cooldown/conditions;
- specialist job level affects abilities/progression;
- reputation and other conditions can gate transformation;
- SP cards can be upgraded using resources such as angel wings, souls, full-moon crystals and gold;
- status points are allocated to improve the specialist.

The July 2026 official patch notes added SP12, one new specialist per class; the Swordsman SP12 is Achilles and uses the crossbow. This proves the SP system is still evolving and the implementation must use data-driven specialist definitions rather than hard-code an old list.

The August 2026 official patch notes also confirm continuing SP balance/QoL changes. Therefore the combat system must learn/ingest current ability metadata and not assume static historical rankings.

### Element and resistance reasoning
The official guide documents four elements (fire, darkness, water, light), elemental interactions, fairies providing elemental power, and resistance reducing elemental damage.

NosAi therefore needs a combat/equipment evaluator that can answer:
1. What element is relevant to the current enemy/activity?
2. Which SP and fairy combination is available?
3. What resistances are required for survival/efficiency?
4. Is changing equipment/SP worth the transition cost?
5. Should the character farm the required resistance/equipment first?

### NPC communication and quests
The normal client exposes NPC markers, mission UI and dialog. Time-Space and story systems require following objectives, reaching locations, interacting with NPCs, killing targets, collecting items and completing sequential objectives.

NosAi must therefore implement an **NPC/Quest Interaction subsystem**, not merely navigation. It needs:
- NPC detection and identity;
- approach/range verification;
- dialog/menu state recognition;
- selectable option representation;
- quest acceptance/turn-in detection;
- objective extraction;
- quest dependency graph;
- reward observation;
- recovery when dialog state changes or an expected option is absent.

For uncertain OCR/dialog recognition, the system must remain UNKNOWN and re-observe rather than invent an option.

### Production/crafting and resource acquisition
The official tutorial confirms production/crafting through NPCs and required materials. Materials can come from different sources, including NPC purchase and monster drops; other sources include quest/time-space/raid rewards depending on the item.

NosAi therefore needs a **Resource Acquisition Planner** capable of comparing:
- NPC purchase price and availability;
- NosBazaar observed market price and quantity;
- expected monster drop yield;
- travel time;
- combat time;
- death/failure risk;
- consumable cost;
- inventory space;
- opportunity cost of not progressing another objective;
- expected future demand.

The decision should be expressed as an expected cost/time model, not a hard-coded "always farm" or "always buy" rule.

### NosBazaar
The official Gameforge guide confirms buying through the Bazaar search UI and that purchased items enter the inventory. It also documents listing/selling and related limits/taxes that can change with game configuration.

NosAi must treat Bazaar data as observed market data with timestamp, price, quantity and provenance. It must not assume a listing remains available after observation; before purchase it must revalidate price, quantity and target item.

### Time-Space / mission execution
Official documentation confirms Time-Space missions can be repeated and expose map/objective/time/life information. Some mission scoring also depends on kills, remaining time, NPC survival and exploration.

This supports a mission planner with explicit objective types:
- travel;
- kill target(s);
- collect item(s);
- interact with NPC/object;
- survive/escort;
- finish before deadline;
- preserve required NPC/life state.

### Raids and group content
Official documentation confirms raids have entry requirements, seals, team structure, timed objectives, boss phases and rewards. NosAi should not assume solo play is sufficient for every progression path. Group/raid participation needs explicit capability and safety gating.

## 3. Autonomous progression architecture required

The existing roadmap must expand AP-07/AP-08 into a coherent **Character Economy & Progression loop**:

`Observe Inventory/Equipment/Quest/NPC/Market`
→ `Normalize + Provenance`
→ `Character World Model`
→ `Goal/Requirement Graph`
→ `Resource Ledger`
→ `Equipment Utility Evaluator`
→ `SP/Transformation Evaluator`
→ `Acquire-vs-Farm Optimizer`
→ `Quest/NPC Planner`
→ `Navigation + Interaction`
→ `Guard/Trust/Safety`
→ `Execute`
→ `Verify`
→ `Re-observe`
→ `Outcome Ledger`

### Required domain modules
1. `CharacterState`
2. `InventoryState`
3. `EquipmentState`
4. `EquipmentEvaluator`
5. `UpgradePlanner`
6. `SpecialistState`
7. `SpecialistEvaluator`
8. `QuestState`
9. `QuestDependencyGraph`
10. `NpcInteractionState`
11. `CraftingRecipeGraph`
12. `ResourceLedger`
13. `AcquisitionPlanner`
14. `MarketObservation`
15. `ProgressionGoalPlanner`
16. `OutcomeLedger`
17. `ProgressionPolicy`

## 4. Human-plausible progression requirement

The university demonstration must deliberately avoid speedrun/cheat behavior. NosAi should behave like a highly competent player:
- no impossible movement or actions;
- no server-side hidden information;
- no artificial item creation;
- no bypass of normal level/job/reputation/quest requirements;
- normal client interaction paths;
- resource/time budgets;
- cooldown and travel-time respect;
- uncertainty handling;
- conservative spending when information is incomplete.

The key academic demonstration is not "the bot is fast". It is "the agent observes more consistently, remembers state, evaluates alternatives quantitatively, avoids waste, and produces a reproducible decision/evidence chain while remaining inside ordinary-player capabilities."

## 5. University demonstration scenario

The canonical demo should be designed as a fresh-character journey:

1. Launch NosTale private test client.
2. Create a new character using the ordinary creation UI.
3. Enter nickname and choose available sex/appearance options.
4. Attach NosAi and establish observation.
5. Verify the initial World Model.
6. Let NosAi select a progression goal.
7. Complete early quests/objectives.
8. Acquire and organize inventory.
9. Equip appropriate gear.
10. Obtain/use an appropriate SP when legitimately available.
11. Compare farming vs NPC/Bazaar acquisition for a required resource.
12. Perform an upgrade only when the expected value is positive and safety constraints permit it.
13. Continue quests and NPC interactions.
14. Show live Dashboard evidence of decisions, inventory, equipment, resources, map/position, quest state, SP and progression.
15. Show before/after results on the actual character.

The demo should have a reproducible checkpoint plan so a failure does not require restarting the entire presentation.

## 6. Research caveats

- NosTale mechanics change through patches, events and server-specific configurations. GameForge official guide/forum material is preferred for current facts.
- Community wikis/guides are useful for discovery and historical details but must not become authoritative without corroboration.
- Exact drop rates, hidden formulas, item databases and current upgrade probabilities should be treated as UNKNOWN until verified for the exact private test-server version using permitted client-observable evidence or explicitly documented public data.
- NosFire is a special server configuration; its starting conditions must not be confused with the normal fresh-character path unless the university test server deliberately adopts them.
- The implementation must be versioned by game/server ruleset so mechanics can evolve without corrupting the World Model.

## 7. Immediate implementation consequence

Before AP-05 Combat is considered complete, NosAi needs the data structures that allow combat decisions to consume:
- current equipment;
- current SP;
- current fairy/resistances;
- current inventory/resources;
- current target and activity;
- quest constraints;
- upgrade/acquisition costs.

AP-07 Character/Inventory/Equipment and AP-06 Quest Intelligence are therefore not optional secondary features. They are prerequisites for genuine autonomous progression.

**Primary research sources reviewed:** official Gameforge NosTale game guide pages and 2026 Gameforge patch notes/forum announcements, including interface, tutorial, class change, specialist cards, item upgrades, trade/NosBazaar, Time-Space, NosFire 2026, raid QoL, SP12 and August 2026 SP balance updates.
