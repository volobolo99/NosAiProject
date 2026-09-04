# NosAi AI Capability Research — 2026-09-04

## Obiettivo

Ricognizione di progetti open-source e risorse tecniche utili a rendere NosAi più capace come agente autonomo, mantenendo invariati i vincoli di `ADR-0021` e `docs/UNPRIVILEGED_DEMO_SPEC.md`.

La ricerca distingue tra:

- **Adottare**: tecnologia con forte valore architetturale e licenza verificata.
- **Studiare**: idea/algoritmo utile, ma integrazione da progettare e validare.
- **Reference-only**: materiale da usare come riferimento, senza entrare nel critical path.
- **REVIEW_REQUIRED**: licenza, maturità o compatibilità non sufficientemente verificate.

## Risultati prioritari

| Progetto | Capacità utile | Decisione | Licenza verificata |
|---|---|---|---|
| `luxkun/ReGoap` | GOAP planner C# | Studiare / possibile adozione isolata | Apache-2.0 |
| `caesuric/mountain-goap` | GOAP C#, planner e test | Studiare | MIT |
| `joslat/agent-memory-dotnet` | memoria persistente graph-native, episodica, reasoning, GraphRAG | Studiare / prototipo separato | MIT |
| `microsoft/Memora` | ciclo di vita della memoria: semantic/episodic/procedural, retrieval ibrido, consolidamento | Studiare fortemente | MIT |
| `microsoft/semantic-kernel` | agent orchestration, plugins, planning, multimodal, vector stores | Studiare come layer non-authoritative | MIT/open-source project; verificare versione prima di incorporare codice |
| `JPDoesDev/GamingVision` | Windows Graphics Capture + ONNX Runtime/DirectML + OCR | Studiare/adattare perception | MIT |
| `KingshotAuto/Kingshot-bot` | CV, OCR, task orchestration, retry/recovery, multi-instance | Reference-only / pattern mining | MIT |
| `ckazi/pilot` | CV game loop senza injection, detection + navigation + safety pauses | Reference-only | verificare upstream prima di redistribuire |
| `datamllab/awesome-game-ai` | indice RL multi-agent, self-play, imperfect information | Ricerca bibliografica | verificare per singolo progetto |

## Cosa aggiungere a NosAi

### 1. Memoria cognitiva a più livelli

Separare almeno:

- **Working Memory**: stato corrente e contesto della decisione.
- **Episodic Memory**: eventi realmente osservati, azioni, esiti e recovery.
- **Semantic Memory**: conoscenza consolidata del dominio/protocollo.
- **Procedural Memory**: strategie validate e sequenze di azioni riuscite.
- **Reasoning Trace**: perché una decisione è stata presa, con evidenze e confidence.

Regola NosAi: nessuna memoria può trasformare un'inferenza in un fatto osservato. Ogni record mantiene `Provenance`, `Confidence`, timestamp e relazione con la sessione/journal.

### 2. Retrieval ibrido

Per il futuro RAG usare una combinazione di:

- exact/keyword/BM25;
- vector similarity;
- metadata filtering;
- temporal relevance;
- provenance filtering;
- reranking deterministico dove possibile.

Il retrieval non deve mai bypassare Safety Gate o creare authority di esecuzione.

### 3. GOAP/HTN sopra il planner deterministico

GOAP è utile per trasformare un obiettivo in una sequenza di azioni in presenza di molte alternative. HTN/behavior trees sono più prevedibili per routine note.

Architettura proposta:

`Goal -> Candidate Plans -> deterministic ranking -> Guard -> Safety -> Execute -> Verify`

Il planner produce **candidate plans**, non autorizzazioni. L'unica authority rimane il percorso ADR-0020 / Safety Gate.

### 4. Perception multimodale locale

Combinare:

- network observation;
- process-memory observation consentita;
- screen capture;
- object detection ONNX;
- OCR;
- tracking temporale;
- confidence fusion.

Quando le sorgenti sono in conflitto, WorldState deve rappresentare l'incertezza invece di scegliere arbitrariamente un valore.

### 5. Recovery-first orchestration

I progetti di game automation analizzati mostrano un pattern utile: ogni task deve avere startup/recovery/timeout/retry/known-state transitions.

Per NosAi questo diventa una macchina a stati esplicita:

`Disconnected -> Attaching -> Observing -> Ready -> Acting -> Verifying -> Recovering -> SafeStop`

Ogni transizione deve essere journaled e testabile.

### 6. RL solo fuori dal critical path iniziale

RL/self-play è interessante per ottimizzare policy e ranking, soprattutto in ambienti parzialmente osservabili e multi-agent. Tuttavia non deve decidere direttamente l'esecuzione nella prima versione certificata.

Pattern consigliato:

`Replay/Simulation -> train/evaluate policy -> produce candidate ranking/model -> deterministic validator -> Safety Gate`

Questo consente di sperimentare RL senza introdurre authority non deterministica nell'action path.

## Progetti da monitorare

### ReGoap

C# GOAP con world facts, actions, goals e piano generato dinamicamente. Licenza Apache-2.0 verificata. Ottimo candidato per un adapter sperimentale separato da `NosAi.Core`.

### Mountain GOAP

GOAP generico C# con suite di test e implementazione della priority queue inclusa. Licenza MIT verificata. Utile soprattutto per confrontare performance, API e testabilità con un eventuale planner NosAi custom.

### Agent Memory for .NET

Implementazione .NET di memoria persistente graph-native con memoria short-term, long-term e reasoning, oltre a retrieval vector/full-text/hybrid e GraphRAG. Licenza MIT verificata. Utile come laboratorio per progettare il `NosAi.Memory` senza vincolare il critical path a Neo4j.

### Microsoft Memora

Progetto MIT che esplora lifecycle della memoria, estrazione di fatti/episodi/procedure, deduplicazione, consolidamento e retrieval semantico/prompted/hybrid. È particolarmente interessante per progettare la futura memoria procedurale e il forgetting controllato.

### Semantic Kernel

Framework Microsoft per agenti, plugin, planning, memoria, multimodalità e vector stores. Va usato come orchestrazione AI non-authoritative, mai come bypass del deterministic action path.

### GamingVision

Stack Windows/.NET moderno per screen capture, OCR e ONNX Runtime/DirectML. Può fornire pattern concreti per il layer `NosAi.Perception` mantenendo l'osservazione entro la boundary non privilegiata.

### KingshotAuto

Mostra un'architettura task-based con CV, OCR, throttling, scheduling, retry e recovery. Anche se il dominio è differente, i pattern di orchestrazione e recovery sono direttamente trasferibili come concetti.

### L2 Spoiler Autopilot / pilot

Esempio interessante perché basa il controllo su screen capture e input emulation senza client injection. Da usare esclusivamente come riferimento architetturale e dopo verifica della licenza upstream.

## Non adottare ora

- accesso a server DB o console;
- API/admin/GM/moderator controls;
- hidden state o developer flags;
- codice di bypass anti-cheat;
- RL che può invocare direttamente `Execute`;
- LLM con authority di esecuzione;
- memoria che non conserva provenance;
- planner che considera UNKNOWN come TRUE.

## Roadmap tecnica proposta

1. `NosAi.Memory` con working + episodic + semantic + reasoning records.
2. Provenance-aware hybrid retrieval.
3. Procedural memory con promotion solo dopo verifica di esiti reali.
4. GOAP adapter sperimentale dietro `IPlanner`.
5. HTN/behavior-tree adapter per routine deterministiche.
6. Perception ONNX/OCR adapter dietro `IPerceptionSource`.
7. Recovery state machine integrata con watchdog.
8. Replay-based offline learning/evaluation.
9. RL/self-play in simulazione soltanto.
10. Model/policy promotion tramite test, benchmark e Safety Gate.

## Fonti

- ReGoap: https://github.com/luxkun/ReGoap
- Mountain GOAP: https://github.com/caesuric/mountain-goap
- Agent Memory for .NET: https://github.com/joslat/agent-memory-dotnet
- Memora: https://github.com/microsoft/Memora
- Semantic Kernel: https://github.com/microsoft/semantic-kernel
- GamingVision: https://github.com/JPDoesDev/GamingVision
- KingshotAuto: https://github.com/KingshotAuto/Kingshot-bot
- L2 Spoiler Autopilot: https://github.com/ckazi/pilot
- Awesome Game AI: https://github.com/datamllab/awesome-game-ai

## Nota di licenza

Questo documento non copia codice di terze parti. Per ogni futura importazione in `third_party/sources/` verificare il file LICENSE dell'esatto repository/commit, conservare copyright/license notices e registrare SHA, percorso e provenienza in `third_party/provenance/REUSE_INDEX.md`.
