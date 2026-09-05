# NosAi — Fastest Mission / TS / SP Strategy Research

**Date:** 2026-09-05  
**Status:** Candidate strategy research  
**Scope:** public guides, community knowledge, current official changes; no privileged information.

## Objective

NosAi must explicitly search for the fastest *valid* way to achieve a goal, especially for:

- Time-Spaces and hidden Time-Spaces (TS/TSO);
- missions containing one or more TS;
- Specialist acquisition missions;
- SP/job-level progression;
- complete quest chains.

The optimization target is **Expected Time To Goal (ETTG)**, not raw DPS:

`ETTG = travel + preparation + execution + recovery + expected retries + resource acquisition`

A strategy can therefore win by avoiding unnecessary fights, choosing a shorter route, preparing resources before entering a TS, or reducing the probability of a failed run.

## Time-Space optimization model

For each observed TS, collect:

`requiredRooms`  
`optionalRooms`  
`requiredObjectives`  
`killOrder`  
`interactionOrder`  
`timeLimit`  
`scoreThreshold`  
`failureConditions`  
`observedDuration`  
`successRate`  
`retryCost`

Community guidance on score-based missions reports that speed, completing internal missions and maintaining kill chains can affect the score. Therefore NosAi must not blindly skip every optional encounter: it must learn whether skipping an action still produces a valid completion and sufficient score.

Source: official NosTale forum guide on score missions: https://forum.nostale.gameforge.com/forum/thread/479-misiones-de-puntuaciones-m%C3%A1ximas/

## Hidden TS / TSO

For hidden TS, the useful strategy is not a fixed coordinate macro. NosAi should learn an observation-driven search:

`detect signal → estimate direction/distance → move → re-observe → narrow area → verify field → interact`

The official forum guide for Spider Raid seal TS describes searching in Eastern Krem / Mountain Cave 5 with dowsing sticks, then creating the TS when the field is found. This is suitable as a candidate observation strategy, not as privileged knowledge.

Source: https://forum.nostale.gameforge.com/forum/thread/363-spider-raid-seal-time-spaces/

## Specialist / SP optimization

The planner must distinguish:

`first acquisition` vs `repeat/farm` vs `SP leveling`.

For first acquisition, optimize the entire prerequisite chain. For repeat/farm, optimize expected reward per unit time. For SP leveling, optimize job XP/goal progress while considering survivability, cooldowns, consumables and travel.

The Italian game guide confirms that specialist missions are obtained through the Mysterious Soul Stone and can be repeated; therefore repeatable SP content belongs in the strategy memory rather than being represented as a one-off macro.

Source: https://gameguide.nostale.it/main/jobs_specialist

## Quest-chain optimization

A quest should be represented as a graph rather than an isolated task:

`Goal → prerequisites → travel → resources → TS/combat → reward → unlocked goal`

The planner should compare complete paths. A TS that takes 20 seconds less but requires a 10-minute resource farm is not necessarily the fastest solution.

## Current-ruleset awareness

Strategy memory must carry a ruleset/version field. Official changes can alter quest requirements, level gates and TS availability. The March 2025 update, for example, reduced requirements for certain quests, allowed party Time-Spaces up to level 99 and reduced the minimum level for SP10 Time-Spaces/Celestial Spire to 84.

Source: https://forum.nostale.gameforge.com/forum/thread/2602-start-adventures-at-level-56-specialist-improvements-more/

Community discussions also show that current progression advice changes with the ruleset; therefore old guides are evidence for hypotheses, not universal truth.

## Fastest-path learning loop

For every candidate strategy:

`CommunitySource`
`→ Candidate`
`→ Preconditions check`
`→ Real private-server execution`
`→ Observe duration/result`
`→ Record success/failure`
`→ Update confidence`
`→ Compare ETTG`
`→ Validated/Verified`

The planner must prefer the fastest strategy only when it remains valid and sufficiently reliable for the current character, environment and ruleset.

## Safety boundary

Only public strategy knowledge is ingested. Gameplay truth remains limited to ordinary-client-observable information: client-visible network traffic, legitimately readable client memory, screen/pixels/OCR/CV, local telemetry and ordinary input. Exploits, packet injection, server manipulation, admin/GM information and hidden server state are never optimization inputs.
