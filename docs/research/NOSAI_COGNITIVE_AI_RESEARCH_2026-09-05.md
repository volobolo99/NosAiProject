# NosAiProject — Cognitive AI & Game-Agent Research

**Date:** 2026-09-05  
**Status:** Research baseline for implementation  
**Scope:** reasoning, memory, planning, world models, game agents, multimodal perception, learning, latency and reliability.

## Executive conclusion

The current NosAi architecture is directionally correct, but it can become substantially more fluid by separating **fast reflex cognition** from **slow deliberative cognition** and by making memory, prediction and uncertainty first-class inputs to planning.

The recommended architecture is not "one giant neural brain". It is a **hybrid cognitive control system**:

```text
Sensors
  ↓
Temporal Fusion / Belief State
  ↓
World Model
  ├── Working Memory
  ├── Episodic Memory
  ├── Semantic Knowledge
  └── Procedural Skill Library
  ↓
Fast Reactive Policy ───────────────┐
  ↓                                │
Utility / Risk Ranking              │
  ↓                                │
Hierarchical Planner ← Prediction ←┘
  ↓
Candidate Plan
  ↓
Guard → Trust → Safety
  ↓
Execute
  ↓
Verify
  ↓
Outcome + Reflection + Memory Update
```

The key improvement is **not more model size**. It is better information flow, temporal state, retrieval, prediction, verification and scheduling.

## 1. What current research says

### 1.1 Reasoning must be coupled to action

ReAct demonstrates that reasoning and acting work better when interleaved with environment feedback rather than treated as two isolated phases. For NosAi this supports a closed loop in which every consequential action immediately generates new observations and updates the active plan.

Source: Yao et al., *ReAct: Synergizing Reasoning and Acting in Language Models*, ICLR 2023.  
https://arxiv.org/abs/2210.03629

### 1.2 Reflection is useful, but should not control execution

Reflexion shows that agents can improve through verbal/episodic feedback without changing model weights. NosAi can use the same principle for post-action analysis: record what happened, why the plan failed or succeeded, and what should change next time. The reflection result must modify memory/ranking, never bypass Safety.

Source: Shinn et al., *Reflexion: Language Agents with Verbal Reinforcement Learning*, 2023.  
https://arxiv.org/abs/2303.11366

### 1.3 Long-term agents need multiple memory forms

Recent agent-memory research distinguishes factual, experiential and working memory and separates token-level, parametric and latent representations. This maps naturally to NosAi's existing direction but suggests a stricter separation:

- **Working memory:** current combat/quest/navigation context;
- **Episodic memory:** concrete previous runs and outcomes;
- **Semantic memory:** stable game knowledge;
- **Procedural memory:** reusable successful action skills;
- **Spatial memory:** map/topology/landmark experience;
- **Reasoning memory:** compact lessons and failure explanations.

Memory should be versioned, scoped, confidence-aware and invalidatable.

Source: Hu et al., *Memory in the Age of AI Agents*, 2025.  
https://arxiv.org/abs/2512.13564

### 1.4 Episodic memory is particularly important for adaptation

A long-lived game agent repeatedly encounters similar but not identical situations. Episodic memory allows single experiences to influence later decisions without pretending that one experience is universal truth. This is particularly valuable for NosAi's mission, combat and recovery learning.

Source: Pink et al., *Episodic Memory is the Missing Piece for Long-Term LLM Agents*, 2025.  
https://arxiv.org/abs/2502.06975

### 1.5 Search improves game planning

Research on language-model game playing shows that external search/MCTS can improve decision quality and that domain knowledge improves state prediction and legal-action accuracy. NosAi should therefore use short-horizon deterministic search for consequential choices instead of asking a language model to directly select a final action.

Source: Schultz et al., *Mastering Board Games by External and Internal Planning with Language Models*, 2024.  
https://arxiv.org/abs/2412.12119

Real-time MCTS research also shows that tree reuse, knowledge-based evaluation, loss avoidance and other enhancements can materially improve game-playing performance.

Source: Soemers et al., *Enhancements for Real-Time Monte-Carlo Tree Search in General Video Game Playing*, 2024.  
https://arxiv.org/abs/2407.03049

### 1.6 Hierarchical game AI remains highly relevant

Game AI practice continues to use combinations of FSMs, hierarchical FSMs, Behavior Trees, Utility Systems, GOAP and HTN. Utility reasoning is especially useful when multiple valid actions exist and the decision depends on continuous trade-offs rather than binary conditions.

Source: Game AI Pro, Behavior Selection Algorithms; Utility Decisions; reactive AI architecture.  
https://www.gameaipro.com/

### 1.7 MMORPG-like environments require persistent learning and specialization

Neural MMO demonstrates why persistent multi-agent environments are valuable for testing exploration, combat, navigation and specialization. The important lesson for NosAi is architectural: persistent worlds expose long-horizon interactions that simple reactive policies cannot solve reliably.

Source: Suarez et al., *Neural MMO*, 2019/2020.  
https://arxiv.org/abs/1903.00784

### 1.8 Open-ended embodied agents benefit from skill libraries

Voyager shows a strong pattern for long-lived game agents: automatic curriculum, reusable executable skills, environment feedback and self-verification. NosAi should adopt the **concept** of a procedural skill library, but its skills must remain inside the project's ordinary-client action boundary and pass deterministic Guard/Safety validation.

Source: Wang et al., *Voyager: An Open-Ended Embodied Agent with Large Language Models*, 2023.  
https://arxiv.org/abs/2305.16291

### 1.9 Multimodal computer agents still struggle with precise grounding

OSWorld shows that screenshot-based agents can suffer from coordinate errors, repetitive actions and difficulty handling unexpected UI state. This reinforces the decision to combine screen perception with independent permitted network/memory observations and explicit verification instead of relying on vision alone.

Source: Xie et al., *OSWorld: Benchmarking Multimodal Agents for Real-World Computer Use*, 2024.  
https://arxiv.org/abs/2404.07972

## 2. Recommended NosAi cognitive architecture

### Layer A — Reflex loop

Target latency: tens of milliseconds where possible.

Responsibilities:
- stop/avoid unsafe action;
- detect death/critical HP;
- detect target loss;
- detect stuck movement;
- cancel stale action;
- react to immediate combat hazards;
- maintain heartbeat/watchdog.

This layer must be deterministic and must never depend on an LLM.

### Layer B — Tactical loop

Target latency: roughly 100–500 ms depending on hardware and workload.

Responsibilities:
- target selection;
- skill/basic attack choice;
- movement correction;
- local repositioning;
- pickup/interact decisions;
- short-horizon prediction;
- immediate replanning.

This is where utility scoring, deterministic search and compact ML models can cooperate.

### Layer C — Strategic loop

Target latency: seconds rather than milliseconds.

Responsibilities:
- quest selection;
- progression objective;
- farming versus questing;
- equipment/build decisions;
- route selection;
- resource acquisition;
- long-horizon recovery;
- exploration priorities.

HTN/GOAP and knowledge retrieval belong here.

### Layer D — Reflective/learning loop

Runs asynchronously when resources permit.

Responsibilities:
- summarize episodes;
- identify repeated failures;
- update strategy statistics;
- discover new procedural skills;
- invalidate stale knowledge;
- evaluate model drift;
- generate candidate improvements.

It must never block the reflex loop and must never receive execution authority.

## 3. The most important "neural connections" to add

### 3.1 Temporal belief state

Do not reason only from the latest snapshot. Maintain a short temporal window of observations and derive:

- velocity;
- acceleration where meaningful;
- target movement trend;
- HP/MP trend;
- cooldown trend;
- map-transition likelihood;
- action progress;
- confidence trend;
- sensor disagreement trend.

This reduces jitter and lets the agent predict what is happening rather than only react to what already happened.

### 3.2 Prediction before action

For important actions calculate a small set of possible outcomes:

```text
current state
   ↓
possible action
   ↓
short-horizon transition model
   ↓
expected reward / time / resource / risk
   ↓
choose candidate
```

Use deterministic simulation where the transition is known. If uncertain, propagate uncertainty rather than inventing certainty.

### 3.3 Memory → retrieval → plan feedback

Memory should not be a passive archive. For every goal:

```text
Goal
 ↓
retrieve relevant episodes + knowledge + skills
 ↓
filter by ruleset / class / character / environment
 ↓
rank by evidence + recency + success + cost
 ↓
planner generates candidates
 ↓
outcome updates memory
```

### 3.4 Skill library

Represent reusable procedural knowledge as parameterized skills, e.g.:

- approach-target;
- maintain-combat-distance;
- recover-after-death;
- navigate-to-landmark;
- interact-with-npc;
- complete-observed-ts-room;
- loot-nearby;
- return-to-safe-area.

Each skill must declare preconditions, expected observations, action sequence, timeout, abort conditions, confidence and evidence history.

### 3.5 Uncertainty as a computational signal

Confidence should influence planning continuously, not only at the Safety Gate.

Example:

```text
high confidence + low risk    → act normally
medium confidence             → gather more evidence / cheaper action
low confidence                → re-observe / alternative sensor
conflicting evidence         → reconcile / UNKNOWN
critical uncertainty          → safe-stop
```

### 3.6 Attention scheduler

The agent should explicitly choose what to perceive next.

Examples:
- if target identity is certain but HP is stale → refresh vitals;
- if map identity is uncertain → prioritize map transition evidence;
- if quest text is known but objective progress is stale → inspect HUD/packet evidence;
- if movement is stuck → prioritize local spatial perception.

This avoids wasting GPU/CPU on unnecessary full-frame inference.

## 4. Make the AI faster without making it dumber

### Cascade inference

```text
cheap change detector
 → ROI tracker
 → lightweight classifier/detector
 → OCR only if required
 → expensive reasoning only if ambiguity remains
```

### Event-driven thinking

Do not recompute the entire world every frame. Trigger domain updates from meaningful changes:

- HP changed;
- target changed;
- map changed;
- quest changed;
- inventory changed;
- cooldown ready;
- movement deviation;
- connection state changed.

### Incremental planning

Reuse a valid plan prefix when only one world-state fact changed. Replan only the invalid suffix where possible.

### Search budget allocation

Give more compute to high-impact decisions and less to trivial ones. A safe movement correction should not invoke the expensive strategic reasoning stack.

### Model residency

On 16 GB RAM / 8 GB VRAM class hardware, avoid simultaneously resident heavyweight models. Prefer one active GPU model plus small CPU components and bounded caches.

## 5. Better learning without uncontrolled reinforcement learning

The first production learning mechanism should be **outcome-based bandit-style ranking**, not online RL.

For every strategy maintain:

- attempts;
- successes;
- duration distribution;
- resource distribution;
- failure classes;
- confidence;
- ruleset version;
- character/build context;
- environment context;
- last successful observation.

Use conservative estimates such as lower-confidence success rates rather than raw empirical success. A strategy with 1/1 successes should not automatically outrank a strategy with 80/100 successes.

Online RL can later operate offline on replay data or simulation, then produce a candidate ranking/policy artifact. Promotion requires regression tests and Safety compatibility.

## 6. New metrics that will make the AI objectively better

Add these to the evaluation system:

### Decision quality
- goal success rate;
- expected utility versus realized utility;
- regret against best observed strategy;
- unnecessary action rate;
- replan rate;
- plan invalidation rate.

### Perception
- confidence calibration error;
- false positive/negative rate;
- temporal stability;
- sensor disagreement rate;
- stale-state duration.

### Runtime
- decision p50/p95/p99;
- perception p50/p95/p99;
- queue depth;
- dropped-frame rate;
- allocation rate;
- GPU VRAM high-water mark;
- RAM high-water mark;
- thermal throttling events.

### Learning
- improvement after N trials;
- knowledge reuse rate;
- repeated-failure rate;
- stale-knowledge rate;
- skill reuse success;
- exploration-to-exploitation ratio.

### Safety
- forbidden-source violations: **0**;
- execution requests rejected by Safety;
- stale-observation actions prevented;
- unknown-state actions prevented;
- recovery-to-safe-state success rate.

## 7. New architecture rule: cognition has no execution authority

All learned/neural/LLM components should terminate at one of these outputs:

- observation interpretation;
- belief estimate;
- candidate goal;
- candidate plan;
- ranking score;
- memory update;
- skill proposal.

Only the deterministic runtime chain may produce executable authority:

`Guard → Trust → Safety → Execute → Verify`.

This preserves the existing safety model while allowing much more sophisticated cognition.

## 8. Prioritized implementation plan

### P0 — highest value
1. Temporal belief state.
2. Event-driven cognition scheduler.
3. Outcome-aware strategy ranking with conservative statistics.
4. Episodic + procedural memory separation.
5. Explicit attention/observation scheduler.
6. Deterministic short-horizon prediction interface.

### P1
7. Skill library with preconditions/evidence/abort semantics.
8. Incremental replanning and plan-prefix reuse.
9. Hybrid retrieval: exact + semantic + recency + provenance + success.
10. Confidence calibration and drift metrics.
11. Structured cognitive trace for every decision cycle.

### P2
12. Offline world-model learning.
13. Offline RL/self-play.
14. Learned reranking/shadow policies.
15. Controlled policy promotion and rollback.

## 9. What should NOT be done

- Do not replace the architecture with a single LLM loop.
- Do not let an LLM issue raw gameplay input.
- Do not use online RL directly in the certified live path initially.
- Do not treat screenshots as ground truth when independent sensors exist.
- Do not store unverified community tactics as truth.
- Do not let memory override fresh contradictory observations.
- Do not use larger models merely because they are available.
- Do not add privileged game/server state to improve AI quality.

## 10. Final target

The strongest practical version of NosAi is a **hybrid cognitive game agent**:

`Temporal Perception + Belief State + World Model + Episodic/Semantic/Procedural Memory + Attention + Prediction + Utility + HTN/GOAP + Reactive Control + Reflection + Outcome Learning + Safety`

This should produce a system that appears more "fluid" because it remembers, predicts, prioritizes attention, reuses successful skills and replans incrementally—not because it blindly calls a larger model more often.

## Primary sources

1. Yao et al., ReAct — https://arxiv.org/abs/2210.03629
2. Shinn et al., Reflexion — https://arxiv.org/abs/2303.11366
3. Hu et al., Memory in the Age of AI Agents — https://arxiv.org/abs/2512.13564
4. Pink et al., Episodic Memory — https://arxiv.org/abs/2502.06975
5. Schultz et al., External/Internal Planning — https://arxiv.org/abs/2412.12119
6. Soemers et al., Real-Time MCTS — https://arxiv.org/abs/2407.03049
7. Wang et al., Voyager — https://arxiv.org/abs/2305.16291
8. Suarez et al., Neural MMO — https://arxiv.org/abs/1903.00784
9. OSWorld — https://arxiv.org/abs/2404.07972
10. Game AI Pro — https://www.gameaipro.com/
