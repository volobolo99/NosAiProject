# NosAiProject — NosTale Hidden Knowledge & Discovery Research

**Date:** 2026-09-05  
**Status:** ACTIVE RESEARCH  
**Purpose:** define how NosAi can discover and retain useful game knowledge that is not already present in its knowledge base, without using privileged server state or cheats.

## 1. Research conclusion

NosTale contains mechanics that can be discovered through ordinary client interaction and observation. Official guides document hidden Time-Space discovery: dowsing sticks indicate distance/direction and an active location enables creation of the hidden Time-Space. The official Italian guide also documents boxes, levers and crystal spheres inside Time-Spaces. This is a concrete example of knowledge that an autonomous player can discover, test and remember. cite-source-turn0search7

Community documentation also describes raid Time-Space fragments and hidden Time-Spaces, but community sources are treated as hypotheses until verified against the current test ruleset. cite-source-turn0search11

Current official material confirms that the game continues to evolve: 2026 raid QoL introduced a 63-slot raid inventory, while later updates changed specialist and inventory behaviour. Therefore discovered knowledge must be versioned by ruleset and revalidated after changes. cite-source-turn0search1

## 2. What NosAi should actively discover

The discovery engine may record, classify and test:

- previously unseen item/drop identities;
- unknown item categories and subcategories;
- unknown map landmarks, portals and interaction points;
- hidden Time-Space indicators and prerequisites;
- NPC dialogue branches and observable quest prerequisites;
- previously unseen quest objectives and reward patterns;
- mob-to-drop relationships and empirical drop frequencies;
- item-to-recipe/upgrade relationships discovered through normal client UI;
- skill effects, cooldowns and observable combat outcomes;
- specialist/SP mechanics and context-dependent tactics;
- element/resistance relationships inferred from repeated observable outcomes;
- inventory placement/stacking/usage behaviour;
- Bazaar/NPC acquisition alternatives and measured costs;
- failure conditions and recovery procedures.

The engine must distinguish **observed fact**, **derived hypothesis**, **candidate strategy**, **validated knowledge** and **verified knowledge**.

## 3. Hidden does not mean privileged

A mechanic can be "hidden" from the player guide or not yet catalogued by NosAi and still be legitimate to discover through the ordinary client. Examples include visual cues, client-visible messages, interaction outcomes, drop observations and hidden Time-Space discovery mechanics documented by the official guide.

NosAi must NOT obtain hidden server databases, admin/GM/moderator state, server console output, debug flags, secret credentials or privileged APIs. It must never modify the server to expose hidden state. If a fact cannot be established through the allowed observation boundary, it remains `UNKNOWN`.

## 4. Self-expanding knowledge filesystem

The runtime knowledge store is intentionally hierarchical and extensible. It can create new directories and JSON records when a topic is not yet represented.

Canonical logical hierarchy:

```text
Knowledge/
  Universal/
  Progression/
    Level-01-20/
  Class/
    Swordsman/
    Archer/
    Mage/
    MartialArtist/
  Specialist/
  Context/
  Character/
  Environment/
```

This is a storage mechanism, not self-modifying executable code. NosAi may create knowledge records, indexes and evidence files; it must not rewrite its own safety, authorization or execution code at runtime.

## 5. Discovery lifecycle

```text
Observe
  -> Normalize
  -> Detect unknown
  -> Create candidate topic/path
  -> Record raw evidence
  -> Form hypothesis
  -> Test with ordinary client actions
  -> Measure outcome
  -> Validate/reject
  -> Persist
  -> Reuse on future characters
```

Failed experiments are retained so the same unsafe or inefficient hypothesis is not repeatedly tested.

## 6. Research sources

Primary source: official Gameforge NosTale game guide, including the hidden Time-Space section. cite-source-turn0search7

Primary source: official Gameforge raid QoL patch notes (March 2026), showing continuing inventory/raid mechanics changes. cite-source-turn0search1

Community source: NosTale Wiki, useful for candidate knowledge and historical context, but not authoritative. cite-source-turn0search0

Community source: raid/hidden Time-Space documentation, useful for hypotheses that require live verification. cite-source-turn0search11

## 7. Architectural implication

AP-09 must become the persistent learning substrate for all AP phases. A new character should inherit verified universal/class/specialist knowledge, while character-specific state remains separate. New observations should expand the knowledge tree rather than overwrite unrelated knowledge.

The resulting principle is:

> **Discover once, validate empirically, version by ruleset, remember, reuse, and revalidate when evidence changes.**
