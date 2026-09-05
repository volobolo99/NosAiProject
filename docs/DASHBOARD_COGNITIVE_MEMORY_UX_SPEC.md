# NosAi Dashboard — Cognitive & Memory UX Specification

**Version:** 1.0  
**Date:** 2026-09-05  
**Status:** Proposed implementation baseline

## 1. Obiettivo

La Dashboard deve diventare la console visiva del cervello operativo di NosAi, non un semplice pannello di stato.

Due superfici sono prioritarie:

1. **Memory Explorer** — esplorazione ordinata e ricercabile di memoria, conoscenza, esperienze, skill, outcome, world model e dati persistiti.
2. **Cognitive Flow** — visualizzazione live della pipeline cognitiva, con avanzamento sequenziale delle decisioni e stato di ogni nodo.

La UI deve mostrare esclusivamente dati realmente prodotti dal runtime. Se un dato non esiste o non è leggibile deve apparire `UNKNOWN`, mai un valore inventato.

## 2. Layout generale

Sidebar permanente:

- Panoramica
- Client NosTale
- Mappa
- Attorno
- Bersaglio
- **Cervello AI**
- **Memoria**
- Percezione
- Rete
- Decisione
- Sicurezza
- Certificazione
- Impostazioni
- Diario

Header persistente:

- stato runtime
- stato client
- stato Safety
- Guard
- latenza cognitiva p50/p95
- ultima decisione
- timestamp snapshot
- STOP/HALT sempre disponibili

## 3. Memory Explorer

### 3.1 Struttura a tre pannelli

**Sinistra — Albero logico**

```text
NOSAI MEMORY
├── Working Memory
│   ├── Belief State
│   ├── Active Goals
│   ├── Current Plan
│   └── Attention Queue
├── Episodic Memory
│   ├── Missions
│   ├── Combat Episodes
│   ├── Navigation Episodes
│   ├── Failures
│   └── Recoveries
├── Semantic Knowledge
│   ├── Game Mechanics
│   ├── Maps
│   ├── NPCs
│   ├── Items
│   ├── Quests
│   └── Strategies
├── Procedural Skills
│   ├── Navigation Skills
│   ├── Combat Skills
│   ├── Quest Skills
│   └── Recovery Skills
├── Outcome Ledger
│   ├── Successful Outcomes
│   ├── Failed Outcomes
│   └── Strategy Statistics
└── Runtime Data
    ├── Snapshots
    ├── Events
    ├── Telemetry
    └── Audit
```

**Centro — elenco**

Ogni elemento mostra:

- nome umano
- tipo
- origine/provenienza
- stato lifecycle
- confidence
- freshness/age
- ruleset version
- numero di osservazioni/campioni
- ultima modifica

Supportare ricerca full-text, filtro per tipo, lifecycle, confidence, data e ruleset.

**Destra — inspector**

Mostrare il record completo in forma leggibile, con sezioni:

- Identity
- Content
- Provenance
- Evidence
- Confidence
- Conditions
- Dependencies
- Outcomes
- History

Per i record strutturati, offrire anche una vista JSON read-only.

### 3.2 Regole di sicurezza

- Read-only per impostazione predefinita.
- Nessun comando della Memory Explorer può autorizzare un'azione di gioco.
- Cancellazione/modifica dei dati non deve essere un'operazione casuale della UI.
- Provenienza e lifecycle sono sempre visibili.
- `Candidate`, `Tested`, `Validated`, `Verified`, `RevalidationRequired`, `Deprecated`, `Forbidden` devono essere graficamente distinti.
- Un elemento `Forbidden` non deve mai essere trasformabile in candidato eseguibile dalla Dashboard.
- Dati simulati/cache/derivati devono essere esplicitamente etichettati.

## 4. Cognitive Flow

### 4.1 Principio

Non mostrare il chain-of-thought privato dell'LLM. La Dashboard mostra invece un **Decision Trace tecnico e osservabile**: input, stato cognitivo, alternative, scoring, vincoli, decisione selezionata, verifica e risultato.

Questo permette di vedere come il sistema arriva a una decisione senza esporre pensieri interni non necessari.

### 4.2 Schema visuale

```text
[SENSORS]
    ↓
[TEMPORAL FUSION]
    ↓
[BELIEF STATE]
    ↓
[WORLD MODEL]
    ↓
[ATTENTION]
    ↓
[PREDICTION]
    ↓
[GOALS]
    ↓
[UTILITY / RISK]
    ↓
[HTN / GOAP]
    ↓
[CANDIDATE PLAN]
    ↓
[GUARD]
    ↓
[TRUST]
    ↓
[SAFETY]
    ↓
[EXECUTE]
    ↓
[VERIFY]
    ↺
[RE-OBSERVE]
```

### 4.3 Animazione

Ogni nodo è una card con:

- nome
- stato: `Idle`, `Running`, `Completed`, `Rejected`, `Unknown`, `Blocked`, `Failed`
- durata
- confidence
- input/output summary
- timestamp

Quando un nodo completa:

1. la card passa a `Completed`;
2. si illumina brevemente;
3. compare il risultato sintetico;
4. il flusso evidenzia il collegamento successivo;
5. il nodo successivo passa a `Running`;
6. il ciclo continua fino a `Decision Committed`.

La timeline deve essere controllabile: pausa, step-by-step, velocità 0.5×/1×/2×/4× e ritorno al live.

### 4.4 Decision card

Quando la decisione viene confermata:

```text
DECISION COMMITTED
Move → NPC_Supplier
confidence 0.94
reason codes: QUEST_PROGRESS, SAFE_PATH, RESOURCE_AVAILABLE
risk: LOW
plan revision: #1842
```

La card resta nella timeline e il sistema apre automaticamente i passi successivi.

## 5. Multi-timescale

Il Cognitive Flow deve poter mostrare quattro livelli:

- **Reflex** — safety/recovery a bassa latenza.
- **Tactical** — target, combattimento, movimento locale.
- **Strategic** — quest, progressione, esplorazione, risorse.
- **Reflective** — memoria, outcome learning, valutazione offline.

L'utente può cambiare livello senza interrompere il runtime.

## 6. Temporal Belief Inspector

Una sezione deve mostrare trend, non solo snapshot:

- posizione + velocità
- HP/MP + trend
- target + movimento
- cooldown + trend
- action progress
- sensor agreement/disagreement
- confidence trend
- state age

Visualizzare inoltre quale sensore ha prodotto ogni valore.

## 7. Attention Scheduler

Mostrare:

- cosa sta osservando ora
- ROI/sensore attivo
- priorità
- motivo dell'attivazione
- budget CPU/GPU/RAM
- cosa è stato rimandato

Questo rende visibile perché NosAi non elabora inutilmente tutto al massimo costo.

## 8. Candidate/Decision inspector

Per ogni decisione mostrare le alternative candidate in una tabella ordinata:

| Candidate | Score | Success estimate | Cost | Risk | Confidence | Selected |
|---|---:|---:|---:|---:|---:|---|
| MoveToNPC | 0.94 | 0.97 | 8s | Low | 0.94 | YES |
| FarmNearby | 0.61 | 0.84 | 42s | Medium | 0.82 | NO |
| BuyItem | 0.57 | 0.90 | 5s | Low | 0.71 | NO |

La UI deve rendere evidente che il ranking è advisory e che Guard/Safety mantengono l'autorità di esecuzione.

## 9. Performance UX

Il pannello non deve rallentare il giocatore.

- Event-driven UI, non polling ad alta frequenza per ogni componente.
- Buffer bounded per il trace live.
- Virtualizzazione per grandi alberi/listati di memoria.
- Sampling grafico indipendente dal runtime.
- Aggiornamento visuale target 10–30 FPS, mentre il runtime mantiene la propria frequenza.
- Backpressure: se la UI non regge, perde solo eventi visuali non critici, mai dati di sicurezza.
- Persistenza del trace tramite Event Journal; la UI può ricostruire una decisione passata.

## 10. Contratti dati da introdurre

Creare contratti separati dal rendering:

- `CognitiveTraceEvent`
- `CognitiveNodeState`
- `DecisionCandidateView`
- `MemoryEntryView`
- `MemoryTreeNode`
- `MemoryInspectorDocument`
- `TemporalBeliefView`
- `AttentionTaskView`

Ogni evento deve avere:

- `TraceId`
- `Sequence`
- `TimestampUtc`
- `Stage`
- `Status`
- `CorrelationId`
- `Source`
- `Confidence`
- `DataClassification`
- `Freshness`

## 11. Architettura runtime → Dashboard

```text
Cognitive Runtime
      │
      ├── CognitiveTracePublisher ──► bounded event stream
      │                                │
      │                                ▼
      │                         ControlPanel ViewModel
      │                                │
      │                                ▼
      │                         Cognitive Flow UI
      │
      ├── MemoryQueryService ───────► Memory Explorer
      │
      ├── OutcomeLedger ─────────────► Outcome Explorer
      │
      └── EventJournal ──────────────► Historical Trace
```

La Dashboard non deve leggere direttamente file interni sparsi per il repository. Deve interrogare servizi/adapter read-only con contratti versionati.

## 12. Roadmap implementativa

### D1 — Contratti

Definire contratti tipizzati per trace e memory explorer.

### D2 — Event stream

Implementare publisher bounded, sequence ordering, correlation e drop policy.

### D3 — Memory query facade

Adapter read-only sopra working/episodic/semantic/procedural/outcome/runtime storage.

### D4 — Cognitive Flow UI

Canvas/ItemsControl virtualizzato con nodi, archi, timeline e inspector.

### D5 — Memory Explorer UI

Tree + list + inspector + search/filter + JSON read-only.

### D6 — Historical replay

Selezione di un trace passato e replay visuale senza esecuzione.

### D7 — Performance hardening

Stress test su trace ad alta frequenza e memoria con dataset grandi.

### D8 — E2E

Test con runtime reale del private server: osservazione → decisione → execute → verify, con evidence persistente.

## 13. Criteri di completamento

La funzione è `Integrated` quando il pannello riceve dati dal runtime reale.

È `Done` quando UI, contratti, persistence e test sono presenti.

È `Verified` solo quando il comportamento è dimostrato con evidenza nel target privato reale.

La presenza di una grafica o di dati demo non costituisce prova di integrazione.
