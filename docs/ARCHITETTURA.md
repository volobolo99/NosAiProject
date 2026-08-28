# NosAi — Architettura completa e modello di comunicazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## Scopo

Questo è l'unico documento ufficiale dell'architettura di NosAi. Consolida architettura, responsabilità, comunicazioni, autorità e ciclo dei dati.

## 1. Principio architetturale

NosAi è un runtime deterministico basato su contratti. I modelli linguistici sono fornitori di decisioni: producono dati, ma non eseguono direttamente strumenti, I/O o decisioni di sicurezza. `WorldState` è la fonte canonica dello stato corrente; EventBus e trace registrano come il sistema è arrivato allo stato osservato.

## 2. Mappa generale

```text
SESSIONE / SCHEDULER
        │
RISORSE ── POLICY ── PROVIDER ROUTER ── MEMORIA ── STRUMENTI
        │
        ▼
CONTROLLO RUNTIME
        │
        ├──────── WATCHDOG
        ├──────── RECOVERY CONTROLLER
        └──────── VALUTAZIONE
        │
        ▼
EVENTBUS / TRACE
        │
        ▼
PERCEZIONE
        │
PerceptionWorldAdapter
        │
        ▼
WORLDSTATE STORE → WorldState(vN) + provenienza
        │
        ├── Party
        ├── Pet
        └── Partner
        │
        ▼
SIMULAZIONE → PredictedOutcome
        │
        ▼
TACTICAL RANKING → punteggio / confidenza / rischio / evidenza
        │
        ▼
ORCHESTRATOR
        │
        ▼
AGENT PLANNER / LOOP
        │
        ▼
GUARD / TRUST / SAFETY
        │
        ▼
EXECUTOR / GAME ADAPTER
        │
        ▼
RISULTATO
        │
        ▼
VERIFIER
        │
        ▼
NUOVA OSSERVAZIONE
        │
        ▼
WORLDSTATE vN+1
        │
        ├── successo → checkpoint → ciclo successivo
        └── fallimento → Recovery → nuova strategia → nuova valutazione
```

## 3. Percorso critico

Il percorso principale è:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`.

Il recupero fa parte del ciclo e può produrre retry, replan, modalità degradata o Cooling.

## 4. Autorità dei componenti

| Componente | Decide | Esegue | Può cambiare strategia |
|---|---|---|---|
| Percezione | fatti osservati | no | no |
| WorldState | rappresentazione dello stato | no | no |
| Simulazione | risultati previsti | no | no |
| Tactical Ranking | ordine dei candidati | no | no |
| Decision Provider | dati decisionali | no | sì, come proposta |
| Planner | piano | no | sì |
| Guard | valutazione | no | sì, nella valutazione |
| Trust | livello autorizzativo | no | secondo policy |
| Safety | autorizzazione finale | no | secondo policy |
| Executor | nessuna decisione strategica | sì | no |
| Verifier | verifica | no | no |
| Recovery | recupero e nuova strategia | no | **sì** |
| Watchdog | gestione dello stato runtime | no | **sì** |
| EventBus | registrazione | no | no |

Recovery e Watchdog sono quindi controller attivi del runtime. La loro capacità di cambiare strategia o modalità non sostituisce i contratti di autorizzazione delle azioni protette.

## 5. WorldState

`WorldStateStore` mantiene la sequenza delle osservazioni, versione dello stato, versione precedente, identificativo dell'osservazione, sorgente e confidenza. Ogni osservazione accettata crea una nuova versione.

Esempio:

`WorldState v41 → azione prevista → risultato osservato → WorldState v42`.

## 6. Simulazione e Tactical Ranking

Simulazione e Tactical Ranking non eseguono azioni. Producono risultati previsti, candidati, punteggi, confidenza, rischio, ricompensa attesa ed evidenza. L'Orchestrator utilizza questi risultati per costruire piani runtime.

## 7. Orchestrator e Planner

L'Orchestrator coordina i moduli e il ciclo operativo. Il Planner trasforma obiettivi e stato in un piano. Un piano è un dato finché non attraversa il percorso di autorizzazione configurato.

## 8. Guard, Trust e Safety

Guard valuta il contesto operativo. Trust determina il livello autorizzativo secondo policy. Safety costituisce il confine finale per le azioni protette.

Livelli Trust:

`OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

## 9. Executor e Verifier

Executor/Game Adapter costituisce il confine tecnico di esecuzione. Riceve un'azione già autorizzata e non output grezzo di un modello. Verifier confronta risultato e nuova osservazione. Un risultato non verificato non viene considerato automaticamente riuscito.

## 10. RecoveryController adattivo

Recovery può:

- analizzare il contesto del fallimento;
- comprimere lo storico con `VRAMContextSlimmer`;
- eseguire retry;
- creare o richiedere un nuovo piano;
- cambiare strategia;
- selezionare una modalità degradata;
- modificare il budget operativo secondo policy;
- entrare in Cooling;
- cambiare modalità runtime;
- riprendere l'esecuzione quando le condizioni lo consentono.

Ciclo:

`fallimento → contesto compatto → strategia Recovery → nuova simulazione/ranking → nuovo ciclo`.

## 11. Watchdog adattivo

Il Watchdog gestisce le modalità:

`NORMAL → DEGRADED → RECOVERY → COOLING → STOPPED`.

Il watchdog hardware può utilizzare telemetria CPU/GPU e I/O opzionale. La soglia termica predefinita è 80 °C. Il Watchdog può modificare modalità runtime e gestione delle risorse in funzione delle condizioni osservate.

## 12. Context Slimming e VRAM

`VRAMContextSlimmer` riduce il costo del contesto diagnostico conservando firme deterministiche degli errori e un numero limitato di errori recenti. La normalizzazione evita che indirizzi di memoria e numeri di riga variabili producano firme inutilmente diverse.

## 13. EventBus e Trace

Gli eventi devono conservare identificativi di correlazione, timestamp, sorgente, tipo, versione dello schema e payload strutturato. EventBus serve audit, telemetria, memoria, evidenza, valutazione e replay. Gli iscritti non acquisiscono autorità di esecuzione.

## 14. Memoria ed evidenza

La memoria distingue esperienza grezza, osservazioni, episodi, ipotesi e conoscenza verificata. Provenienza, confidenza ed eventi di supporto devono essere conservati per le evidenze.

## 15. Provider e hardware

Provider Router utilizza telemetria hardware e policy per scegliere il provider. Le variabili possono includere VRAM/RAM, utilizzo GPU, temperatura, latenza, energia, complessità e prestazioni recenti. L'escalation cloud deve rispettare la policy locale.

## 16. Comunicazione PC/telefono

Il bring-up locale/LAN utilizza messaggi tipizzati e protezione da sequenze/replay. Il ciclo previsto è:

`HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

Contratti principali:

- `SessionMessage`
- `ResourceSnapshot`
- `RuntimeEvent`
- `PerceptionWorldUpdate`
- `WorldState`
- `SimulationResult`
- contratto di ranking
- `AgentPlan`
- `GuardDecisionContext`
- contratto Safety
- risultato Executor
- risultato Verifier
- comando Recovery

## 17. Pipeline di percezione

Pipeline produttiva prevista:

```text
DXGI Direct Capture
      ↓
Triple Buffer lock-free
      ↓
HSV multi-ROI
      ↓
YOLO
      ↓
OCR glyph-hash
      ↓
AI-OCR fallback/cache
      ↓
Kalman 2D temporale
      ↓
Game State Evaluator
      ↓
WorldState semantico immutabile
```

I backend produttivi devono essere validati indipendentemente prima dell'uso live.

## 18. Ciclo dati

### Osservazione

`percezione grezza → semantica → validazione → WorldStateStore → versione`

### Decisione

`WorldState + obiettivo → simulazione → ranking → orchestrazione → piano`

### Esecuzione

`piano → autorizzazione → executor → risultato → nuova osservazione`

### Recupero

`fallimento → contesto compatto → strategia Recovery → nuova simulazione/ranking → nuovo ciclo`

## 19. Matrice di comunicazione

| Da | A | Contratto/canale | Risultato |
|---|---|---|---|
| Percezione | World Model | `PerceptionWorldAdapter` | osservazione versionata |
| World Model | Simulazione | WorldState immutabile | risultati previsti |
| Simulazione | Tactical Ranking | `SimulationResult` | candidati valutati |
| Tactical Ranking | Orchestrator | azioni ordinate | decisione di dominio |
| Orchestrator | Planner/Runtime | contratto piano | `AgentPlan` |
| Decision Provider | Runtime | dati decisionali | candidato/piano |
| Planner | Guard | `GuardDecisionContext` | valutazione |
| Guard | Trust/Safety | contratto policy | stato autorizzativo |
| Safety | Executor | autorizzazione esplicita | azione o blocco |
| Executor | Percezione | confine osservazione | nuova osservazione |
| Executor | Verifier | risultato azione | evidenza |
| Verifier | Recovery | fallimento strutturato | retry/replan |
| Recovery | Runtime | comando di controllo | nuova modalità/strategia |
| Runtime | EventBus | `RuntimeEvent` | audit/trace |
| Runtime | Memoria | contratto eventi | esperienza/evidenza |
| Hardware | Provider Router | `ResourceSnapshot` | scelta provider |
| PC | Telefono | `SessionMessage` autenticato | coordinamento |

## 20. Stato della produzione

Le fondamenta deterministiche sono disponibili. I backend produttivi di percezione, persistenza, rete autenticata, provider LLM, adapter di gioco e integrazione hardware reale devono essere considerati produttivi solo dopo validazione indipendente.

## 21. Regola linguistica

La documentazione del progetto è italiana. Codice, identificatori, API, protocolli e nomi tecnici che richiedono la forma originale possono rimanere in inglese.

## 22. Governance

NosAi rimane **1.0 Beta** finché il creatore non richiede esplicitamente una modifica.
