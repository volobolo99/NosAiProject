# NosAi — Unprivileged University Demonstration Specification

**Version:** 1.0
**Date:** 2026-09-04
**Status:** Canonical for university validation

## 1. Objective

NosAi must operate as a real autonomous software system against the private test server and client without privileged game-server access.

The goal is not to simulate success with moderator/admin data. The goal is to demonstrate that the same agent architecture can perceive, reason, act, verify and recover using only information available through the ordinary client-side boundary.

## 2. Demonstration rule

> If an ordinary player cannot observe it or perform it through the client, NosAi must not use it as gameplay truth or as a gameplay control path.

## 3. Permitted inputs

| Source | Permitted | Examples |
|---|---|---|
| Client-visible network | YES | packets/events reaching the client, timing, sequence, protocol observations |
| Local client memory | YES | client state readable by the local runtime under the test setup |
| Screen/pixels | YES | HUD, entities, text, visual state, window geometry |
| Local OS telemetry | YES | CPU/GPU/RAM/network/process/window state |
| NosAi local journal | YES | previous observations, decisions, outcomes |
| Human operator | LIMITED | configuration, start/stop, explicit intervention; never hidden gameplay truth |
| Server DB/admin panel | NO | authoritative hidden state |
| GM/moderator tools | NO | privileged gameplay information/actions |
| Server console | NO | hidden state or privileged commands |
| Admin-only API | NO | gameplay state or actions unavailable to ordinary client |

## 4. Canonical WorldState provenance

Every externally observed fact must carry provenance. Recommended values:

```text
Network
Memory
Screen
Local
Operator
Unknown
```

A fact with no permitted evidence is `Unknown`.

## 5. Autonomous-run rule

During the recorded autonomous run:

- no moderator/GM/admin commands are issued;
- no server database is queried by NosAi or its supporting tooling;
- no privileged server API is called;
- no hidden state is copied into the agent's memory;
- operator interventions are explicitly logged and terminate or pause autonomous evaluation for that segment;
- the agent cannot promote `Operator` knowledge into `Network`, `Memory` or `Screen` evidence.

## 6. Functional acceptance test

A release candidate must demonstrate, on the private test server:

1. discover and attach to the supported client;
2. establish at least one permitted observation source;
3. build a coherent WorldState from permitted observations;
4. expose uncertainty when information is unavailable;
5. select a goal and produce a plan;
6. pass the plan through Guard and Safety;
7. execute an allowed client-side action;
8. observe the result through permitted sources;
9. record the complete decision/evidence chain;
10. recover from a controlled disconnect or missing observation;
11. reproduce the run from the documented clean setup.

## 7. Anti-cheating acceptance tests

The demonstration is rejected if any of the following occurs:

- disabling a perception source causes the AI to retain privileged or preloaded truth;
- a server-admin credential is required for normal gameplay operation;
- a hidden test flag is used to reveal future/authoritative state;
- a moderator command is required to create an event that the AI claims to have observed;
- the evaluator cannot reproduce the run without privileged server access.

## 8. Required evidence package

Each final demonstration run should produce:

```text
artifacts/
  run-metadata.json
  observation-provenance.jsonl
  decision-trace.jsonl
  safety-events.jsonl
  action-events.jsonl
  recovery-events.jsonl
  journal-integrity.txt
  screenshots/
```

The package must identify the active data sources and show that no forbidden source was used.

## 9. Academic value

The project should be evaluated as an AI/software-engineering experiment, not only as a game automation demo. The central research questions are:

- How does an autonomous agent behave under partial observability?
- How does combining independent client-side sensors improve state confidence?
- How should an agent behave when observations conflict?
- How can safety and authorization constrain an autonomous planner?
- How can decisions be reproduced and audited from an immutable event history?
- How does memory improve decisions without becoming an unauthorized information channel?

## 10. Final acceptance target

The target is **100% reproducible functionality within the declared test scope**, not a claim that the software can know information unavailable to an ordinary client.

The commission should be able to start the private server, start the ordinary client, start NosAi, and observe the complete pipeline without privileged game-server intervention.
