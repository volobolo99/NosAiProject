# NosAiProject — Architettura del Sistema

**Versione:** 2.0  
**Data:** 2026-09-05  
**Stato:** CANONICO  
**Ambiente target:** Windows PC + client NosTale in ambiente privato educativo/test

> Questo documento descrive **come è costruito NosAi**, quali componenti esistono, come comunicano e dove sono i confini di autorità. Le descrizioni sono brevi, ma ogni area è rappresentata.

---

## 1. Obiettivo architetturale

NosAi è progettato come un **autonomous player osservazionale e verificabile**.

Il ciclo fondamentale è:

```text
OSSERVA
   ↓
COSTRUISCI WORLD STATE
   ↓
SIMULA
   ↓
VALUTA / RANKING
   ↓
PIANIFICA
   ↓
GUARD
   ↓
TRUST / AUTHORIZATION
   ↓
SAFETY
   ↓
ESEGUI
   ↓
VERIFICA
   ↓
MEMORIA / TRACE
   ↓
RI-OSSERVA
```

Nessun componente AI può saltare i controlli e passare direttamente all'esecuzione.

---

## 2. Vista generale del sistema

```text
┌─────────────────────────────────────────────────────────────┐
│                    NOSAI RUNTIME HOST                       │
│                                                             │
│  Scheduler / Session / Watchdog / Recovery / Resources      │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                    OBSERVATION LAYER                        │
│                                                             │
│ Network │ Client Memory │ Screen/CV/OCR │ OS Telemetry      │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                 PERCEPTION + SENSOR FUSION                  │
│                                                             │
│ Provenance │ Confidence │ Freshness │ Conflict resolution   │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                       WORLD MODEL                            │
│                                                             │
│ Player │ Map │ Entities │ Combat │ Quest │ Inventory │ Party │
└───────────────┬───────────────────────────┬─────────────────┘
                │                           │
                ▼                           ▼
        ┌───────────────┐          ┌─────────────────┐
        │ Simulation    │          │ Memory /        │
        │ + Prediction  │          │ Evidence        │
        └───────┬───────┘          └─────────────────┘
                │
                ▼
        ┌───────────────┐
        │ Tactical      │
        │ Ranking       │
        └───────┬───────┘
                │
                ▼
        ┌───────────────┐
        │ Orchestrator  │
        └───────┬───────┘
                │
                ▼
        ┌───────────────┐
        │ Planner        │
        │ HTN / GOAP /   │
        │ Reactive Rules │
        └───────┬───────┘
                │
                ▼
        ┌───────────────┐
        │ Guard          │
        ├───────────────┤
        │ Trust          │
        ├───────────────┤
        │ Safety Gate    │
        └───────┬───────┘
                │
                ▼
        ┌───────────────┐
        │ Executor       │
        │ / Game Adapter │
        └───────┬───────┘
                │
                ▼
        ┌───────────────┐
        │ Verifier       │
        └───────┬───────┘
                │
                └──────────────► nuova osservazione
```

---

## 3. Struttura del repository

```text
NosAiProject/
│
├── src/
│   ├── NosAi.Core/
│   ├── NosAi.Runtime/
│   └── NosAi.ControlPanel/
│
├── tests/
│   ├── NosAi.Core.Tests/
│   ├── NosAi.Runtime.Tests/
│   └── NosAi.ControlPanel.Tests/
│
├── docs/
│   ├── ARCHITETTURA.md
│   ├── NOSAI_ARCHITECTURE_BASELINE.md
│   ├── SOURCE_OF_TRUTH.md
│   ├── ROADMAP_ESECUTIVA.md
│   ├── adr/
│   ├── agents/
│   └── CERTIFICAZIONI/
│
├── scripts/
├── proto/
├── third_party/
└── NosAi.sln
```

### Regola della struttura

- `src/` contiene il prodotto.
- `tests/` contiene la verifica automatica.
- `docs/` contiene architettura, decisioni e procedure.
- `proto/` contiene contratti di comunicazione.
- `scripts/` contiene automazione riproducibile.
- `third_party/` contiene dipendenze di terze parti e **non deve essere cancellato o alterato senza una decisione esplicita di licensing/provenienza**.

---

## 4. Progetti software principali

### `NosAi.Core`

Nucleo di dominio e contratti condivisi.

Contiene concetti indipendenti dall'I/O: stato, modelli, contratti, policy e logica riutilizzabile.

**Dipendenze:** deve rimanere il più indipendente possibile da client, UI e infrastruttura.

### `NosAi.Runtime`

Processo principale dell'autonomous player.

Coordina osservazione, world model, decisione, sicurezza, esecuzione, verifica, memoria e recovery.

È il **cuore operativo** del sistema.

### `NosAi.ControlPanel`

Dashboard Windows per osservabilità e controllo operativo consentito.

Visualizza stato runtime, telemetria, eventi, trace, test e diagnostica.

**Non deve diventare un percorso alternativo di esecuzione.**

---

## 5. Runtime Host

Il Runtime Host coordina il ciclo principale.

Responsabilità principali:

- bootstrap;
- configurazione;
- lifecycle;
- scheduling;
- health state;
- orchestrazione;
- watchdog;
- recovery;
- resource management;
- collegamento dei moduli.

Il Runtime Host non deve contenere logica di dominio eccessiva: deve coordinare, non diventare un monolite.

---

## 6. Observation Layer

### Fonti ammesse

```text
Client-visible network
        │
Legitimately readable client memory
        │
Screen / pixels / OCR / CV
        │
Local OS / hardware telemetry
        │
NosAi-owned state
```

### Principio

L'osservazione produce **fatti con provenienza**, non decisioni.

Ogni dato importante deve poter indicare:

- sorgente;
- timestamp;
- confidenza;
- freschezza;
- stato `LIVE / DERIVED / CACHED / SIMULATED / UNKNOWN`.

### Live Observation Gateway

`LiveObservationGateway` unifica client baseline e gameplay observation in uno snapshot immutabile.

È read-only e non possiede autorità di esecuzione.

---

## 7. Perception Layer

Trasforma dati grezzi in osservazioni semantiche.

Pipeline prevista:

```text
Capture
 → ROI
 → Vision
 → OCR
 → Tracking
 → Sensor Fusion
 → Semantic Observation
```

### Componenti

- **Capture:** acquisisce il frame del client.
- **ROI:** limita l'area analizzata.
- **CV/YOLO:** rileva elementi visivi.
- **OCR:** legge testo e UI.
- **Tracking:** stabilizza le osservazioni nel tempo.
- **Fusion:** combina fonti diverse.
- **Provenance:** conserva origine e confidenza.

OCR e CV sono sensori: non sono verità assoluta.

---

## 8. World Model

È la rappresentazione canonica dello stato operativo.

```text
WorldState
├── Player
├── Map
├── Entities
├── NPCs
├── Mobs
├── Drops
├── Interactables
├── Combat
├── Quest
├── Inventory
├── Equipment
├── Party
├── Pet
└── Partner
```

### WorldState Store

Mantiene versioni successive dello stato.

Ogni aggiornamento deve conservare almeno:

- versione;
- versione precedente;
- observation id;
- sorgente;
- confidence;
- timestamp.

---

## 9. Spatial / Map Model

Rappresenta il mondo navigabile.

```text
Map Observation
      ↓
Geometry / Walkability
      ↓
Spatial Representation
      ↓
Global Route
      ↓
Local Corridor
      ↓
Obstacle Avoidance
      ↓
Movement
      ↓
Verification
```

Supporta progressivamente:

- coordinate;
- celle;
- ostacoli;
- aree osservate;
- landmark;
- portali;
- transizioni;
- versioni della mappa;
- pathfinding globale e locale.

---

## 10. Domain Models

### Combat Model

Rappresenta HP, MP, target, skill, cooldown, distanza, minacce e stato combattimento.

### Quest Model

Rappresenta missioni, obiettivi, prerequisiti, progressione e stato corrente.

### Character Model

Rappresenta livello, statistiche, equipaggiamento, inventario, risorse e progressione.

### Party Model

Rappresenta party, pet e partner osservati e rilevanti per la decisione.

---

## 11. Simulation Layer

Prevede gli effetti delle possibili azioni.

Esempi:

- percorso più breve;
- rischio combattimento;
- consumo risorse;
- tempo stimato;
- risultato atteso.

Output principale:

`PredictedOutcome`

La simulazione è **advisory**: non può dichiarare realtà di gioco.

---

## 12. Tactical Ranking

Ordina le alternative candidate.

Valuta tipicamente:

```text
Score
+ Confidence
+ Expected Reward
- Risk
- Cost
- Resource Pressure
```

Il ranking non esegue azioni.

---

## 13. Strategic Orchestrator

Decide **quale obiettivo perseguire** in base allo stato corrente e alle priorità.

Esempi:

```text
Sopravvivere
↓
Recuperare risorse
↓
Completare quest
↓
Combattere
↓
Esplorare
↓
Progredire
```

Coordina i planner senza bypassare Guard e Safety.

---

## 14. Planning Layer

Architettura ibrida:

### HTN
Decompone obiettivi complessi.

### GOAP deterministico
Sceglie sequenze di azioni in base a stato, costo e prerequisiti.

### Reactive Rules
Gestiscono eventi immediati e interruzioni.

### LLM / AI Reasoning
Può proporre interpretazioni, strategie o candidati.

**Non può eseguire direttamente.**

---

## 15. Guard Layer

Controllo preliminare dell'azione.

Verifica:

- validità del piano;
- prerequisiti;
- coerenza dello stato;
- policy;
- limiti;
- rischio evidente.

Output: decisione strutturata di ammissione o rifiuto.

---

## 16. Trust / Authorization

Definisce se il piano ha sufficiente autorità per procedere.

Principio fondamentale:

```text
Decisione AI ≠ Autorizzazione
```

Il Trust non viene aumentato automaticamente da:

- LLM;
- Recovery;
- Watchdog;
- UI;
- EventBus;
- risultati non verificati.

---

## 17. Safety Gate

È l'ultima barriera prima dell'esecuzione.

```text
Planner
   ↓
Guard
   ↓
Trust
   ↓
Safety
   ↓
Executor
```

Un `DENY` blocca l'azione corrente.

Safety non delega la propria autorità a modelli probabilistici.

---

## 18. Execution Layer

L'Executor esegue esclusivamente azioni autorizzate.

### Adapter

Il Game Adapter separa il dominio dall'I/O concreto.

Canali ammessi:

- API Windows ordinarie;
- mouse;
- tastiera;
- meccanismi client-side consentiti;
- altri canali osservabili e non privilegiati previsti dall'architettura.

L'Executor non deve ottenere hidden state o privilegi server.

---

## 19. Verification Layer

Ogni azione importante deve produrre un risultato verificabile.

```text
Expected Outcome
       ↓
Actual Observation
       ↓
Compare
       ↓
PASS / FAIL / UNKNOWN
```

Un'azione non verificata non diventa automaticamente successo.

Il Verifier alimenta WorldState, Memory e Recovery.

---

## 20. Recovery Controller

Gestisce gli errori senza bypassare la sicurezza.

Strategie:

```text
Retry
Replan
Degraded Mode
Cooling
Critical Deadlock
```

Include circuit breaker e backoff.

Recovery può cambiare strategia o modalità, ma non concede nuova autorità di Trust.

---

## 21. Watchdog

Controlla salute e lifecycle del runtime.

Stati principali:

```text
NORMAL
DEGRADED
RECOVERY
COOLING
STOPPED
```

Può fermare o degradare il runtime secondo policy.

Non esegue direttamente azioni di gioco.

---

## 22. Adaptive Throttling

Adatta il carico alle risorse disponibili.

Input principali:

- CPU;
- GPU;
- VRAM;
- RAM;
- temperatura;
- I/O;
- rete;
- latenza;
- errori critici.

Output:

`ResourcePlan`

Il throttler decide il carico, non l'autorizzazione alle azioni.

---

## 23. Memory & Learning

Ciclo cognitivo operativo:

```text
Observation
   ↓
Episode
   ↓
Decision
   ↓
Outcome
   ↓
Evidence
   ↓
Reusable Strategy
```

La memoria deve conservare:

- provenienza;
- confidenza;
- timestamp;
- contesto;
- outcome;
- evidenze di supporto.

Un fallimento non diventa automaticamente conoscenza valida.

---

## 24. Cognitive Runtime Trace

Il trace rappresenta il **flusso tecnico verificabile della decisione**, non il pensiero privato di un modello.

Esempio:

```text
OBSERVE        ✓
WORLD STATE    ✓
SIMULATION     ✓
RANKING        ✓
PLAN           ✓
GUARD          ✓
TRUST          ✓
SAFETY         ✓
EXECUTE        ✓
VERIFY         ✓
```

La Dashboard può visualizzare questi nodi in sola lettura.

---

## 25. EventBus / Trace / Audit

L'EventBus è trasversale e osservazionale.

Eventi principali:

- runtime;
- observation;
- world state;
- decision;
- safety;
- execution;
- verification;
- recovery;
- resource;
- audit.

Ogni evento importante dovrebbe avere:

`EventId + SessionId + ExecutionId + Timestamp + Source + Type + SchemaVersion + Payload`

Il bus non è un canale di esecuzione.

---

## 26. Persistence

### SQLite

Usato per persistenza locale di dati runtime selezionati, sessioni e traiettorie.

### WAL / Batch

Favoriscono scritture efficienti e riducono contention.

### Replay

Deve permettere analisi e simulazione senza eseguire I/O live.

### Knowledge Base

Destinata a conoscenza verificata con provenienza e storico delle evidenze.

---

## 27. PC ↔ Smartphone

Il telefono è un **client di osservabilità/controllo autorizzato**, non il cervello del sistema.

Flusso previsto:

```text
PC Runtime
   ↕
Authenticated Session
   ↕
Mobile App
```

Bring-up:

```text
HELLO
 → CAPABILITIES
 → AUTH
 → HEARTBEAT / STATUS
 → COMMAND / EVENT
 → ACK / ERROR
 → DISCONNECT
```

Il trasporto deve verificare autenticazione, sequenza, replay e validità della sessione.

---

## 28. Control Panel

La Dashboard Windows deve fornire:

- stato client;
- stato runtime;
- HP/MP;
- posizione;
- mappa;
- target;
- attività corrente;
- decision trace;
- memoria osservabile;
- hardware telemetry;
- eventi;
- test center;
- errori e recovery;
- stato Safety.

### Regola architetturale

```text
Dashboard → osserva / comanda tramite contratti autorizzati
Dashboard ≠ Executor
```

---

## 29. Gate architetturali

### Gate 1 — Physical Spine

Verifica la connessione operativa:

```text
PC ↔ NosTale ↔ Runtime ↔ Dashboard
```

### Gate 3 — Decision / Action Safety

Verifica il percorso:

```text
Observe
 → Plan
 → Simulation
 → Ranking
 → Guard
 → Safety
 → Execute
 → Verify
```

### Gate finali

Ogni capacità live deve passare da:

```text
Present
  ↓
Integrated
  ↓
Done
  ↓
Verified
```

La sola presenza del codice non è evidenza di funzionamento.

---

## 30. Sicurezza e confini non negoziabili

1. Nessun LLM esegue direttamente.
2. Perception non esegue.
3. Simulation non esegue.
4. Ranking non esegue.
5. Recovery non aumenta Trust.
6. Watchdog non aumenta Trust.
7. AdaptiveThrottler non autorizza azioni.
8. Safety può bloccare qualsiasi azione.
9. Verifier decide se l'esito è osservabilmente riuscito.
10. EventBus non diventa execution path.
11. UI non bypassa Safety.
12. Nessun server DB o admin tool è richiesto.
13. Nessun hidden/debug state può diventare sorgente decisionale.
14. Nessuna automazione hardware esterna è necessaria oltre ai dispositivi di input consentiti.
15. `third_party/` deve mantenere provenienza e licenze.

---

## 31. Principi di performance

```text
Bounded Queues
Bounded Memory
Cancellation
Timeout
Async I/O
Lazy Loading
ArrayPool / Memory / Span
ROI Processing
Adaptive Inference
```

Obiettivi:

- bassa latenza sul percorso critico;
- p50/p95/p99 misurabili;
- niente crescita memoria incontrollata;
- nessuna inferenza pesante dentro Safety;
- degradazione controllata sotto pressione hardware.

---

## 32. Hardware target

Baseline di progetto:

- Windows laptop ASUS Nitro V16;
- AMD Ryzen;
- NVIDIA RTX 5060 Laptop GPU, classe 8 GB GDDR7;
- 16 GB DDR5;
- SSD esterno dedicato al progetto/runtime.

Il runtime deve rilevare le caratteristiche reali invece di assumere un SKU fisso.

La temperatura, VRAM, RAM e potenza disponibile sono **capacità dinamiche**.

---

## 33. AI Provider Router

Seleziona il provider più adatto in base a:

- latenza;
- privacy;
- costo computazionale;
- RAM/VRAM;
- GPU;
- temperatura;
- carico;
- disponibilità;
- qualità recente.

Tier indicativi:

```text
Tier 0 → regole deterministiche
Tier 1 → ML locale leggero
Tier 2 → GPU inference
Tier 3 → reasoning costoso
```

Il runtime sceglie il livello minimo sufficiente.

---

## 34. Miniland

Il dominio Miniland è separato dall'I/O.

```text
Miniland Domain
      ↓
Miniland Adapter
      ↓
Client Integration
```

Questo permette test deterministici con adapter simulati e successiva integrazione reale attraverso i normali confini di sicurezza.

---

## 35. Progressione del personaggio

Il progression engine dovrà modellare:

- quest;
- prerequisiti;
- missioni concatenate;
- TS;
- SP;
- equipaggiamento;
- risorse;
- obiettivi di livello.

La progressione diventa operativa solo quando collegata a WorldState, Simulation, Planning, Execution e Verification.

---

## 36. Matrice delle responsabilità

| Layer | Osserva | Decide | Esegue | Autorizza |
|---|---:|---:|---:|---:|
| Observation | ✓ | | | |
| Perception | ✓ | | | |
| World Model | | rappresenta | | |
| Simulation | | ✓ previsione | | |
| Ranking | | ✓ ordine | | |
| Orchestrator | | ✓ obiettivo | | |
| Planner | | ✓ piano | | |
| Guard | | ✓ policy | | |
| Trust | | | | ✓ confine |
| Safety | | ✓ decisione finale | | ✓ |
| Executor | | | ✓ | |
| Verifier | ✓ risultato | ✓ esito | | |
| Recovery | | ✓ strategia | | |
| Watchdog | ✓ salute | ✓ modalità | | |
| EventBus | ✓ eventi | | | |
| Dashboard | ✓ telemetria | UI only | | No |

---

## 37. Comunicazioni principali

| Da | A | Contratto / contenuto |
|---|---|---|
| Observation | Perception | Raw Observation |
| Perception | WorldState | Semantic Observation |
| WorldState | Simulation | Immutable Snapshot |
| Simulation | Ranking | PredictedOutcome |
| Ranking | Orchestrator | Ranked Candidates |
| Orchestrator | Planner | Goal / Context |
| Planner | Guard | Action Plan |
| Guard | Trust | Guard Decision |
| Trust | Safety | Authorization Context |
| Safety | Executor | Explicit Authorization |
| Executor | Verifier | Execution Receipt |
| Verifier | WorldState | Verified Observation |
| Verifier | Recovery | Failure Evidence |
| Runtime | EventBus | RuntimeEvent |
| Runtime | Dashboard | Telemetry / Trace |
| WorldState | Mobile | Versioned / Delta State |
| Mobile | Runtime | Authenticated Command |

---

## 38. Stato architetturale

### Operativo / presente nel repository

- Runtime .NET 8;
- Gate 1 e relativi componenti;
- Gate 3 e pipeline decisionale;
- WorldState e provider;
- live client connector;
- gameplay providers;
- `LiveObservationGateway`;
- Control Panel;
- navigation/pathfinding;
- EventBus e trace;
- Recovery / Watchdog;
- telemetry hardware;
- framing e componenti di sessione;
- SQLite iniziale;
- Miniland adapter architecture.

### Da integrare o verificare

- full live perception production pipeline;
- end-to-end PC ↔ smartphone;
- provider AI produttivi;
- binding Protobuf completo nella toolchain;
- replay durevole completo;
- Knowledge Base append-only;
- valutazione predittiva completa;
- adapter live Miniland;
- verifica end-to-end dei gate;
- benchmark reali hardware/performance.

> **Importante:** “presente” non significa automaticamente “Verified”. La verifica richiede build, test ed evidenza nell'ambiente applicabile.

---

## 39. Regola finale del ciclo autonomo

Il sistema deve sempre poter rispondere tecnicamente a questa sequenza:

```text
Cosa vedo?
   ↓
Qual è lo stato del mondo?
   ↓
Cosa potrebbe succedere?
   ↓
Quale opzione conviene?
   ↓
Qual è il piano?
   ↓
È consentito?
   ↓
È sicuro?
   ↓
Eseguo.
   ↓
Cosa è realmente successo?
   ↓
La previsione era corretta?
   ↓
Cosa salvo come evidenza?
   ↓
Qual è il prossimo ciclo?
```

Questa sequenza è il **contratto architetturale centrale di NosAiProject**.
