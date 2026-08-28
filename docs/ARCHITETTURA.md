# NosAi — Architettura e modello di comunicazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## 1. Principio architetturale

NosAi è un runtime deterministico basato su contratti. I modelli linguistici sono fornitori di decisioni: producono dati, ma non eseguono direttamente strumenti, I/O, autorizzazioni o decisioni di sicurezza. `WorldState` è la fonte canonica dello stato corrente; EventBus e trace registrano come il sistema è arrivato allo stato osservato.

## 2. Sistema completo

```text
SESSIONE / SCHEDULER
        │
PIANO DI CONTROLLO RUNTIME
Policy • Trust • Risorse • Provider Router • Memoria • Strumenti • Watchdog • Valutazione
        │
BUS EVENTI / TRACE  ← osservazionale
        │
PERCEZIONE
        │
PerceptionWorldAdapter
        │
WORLDSTATE STORE → WorldState(vN) + provenienza
        │
Party / Pet / Partner
        │
Simulazione → PredictedOutcome
        │
Tactical Ranking → punteggio/confidenza/rischio/evidenza
        │
Orchestrator
        │
Agent Planner / Loop
        │
GuardDecisionContext
        │
Guard AI / Policy
        │
Trust Boundary
        │
Safety Gate
        │
Play AI / Executor / Game Adapter
        │
Risultato azione
        │
Verifier + nuova osservazione
        │
WorldState(vN+1)
   ├─ PASS → checkpoint → ciclo successivo
   └─ FAIL → recupero adattivo → ripianificazione → nuovo ranking
```

## 3. Percorso critico

Il percorso critico deve essere deterministico e ordinato:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`.

Il recupero è parte del ciclo: un errore può produrre retry, nuovo piano, modalità degradata o cooling in base a policy, contesto e stato runtime.

## 4. Bus eventi e trace

EventBus è trasversale e osservazionale. Gli eventi identificano evento, sessione, esecuzione, attività, genitore, timestamp, sorgente, tipo e versione dello schema. La persistenza durevole e il replay deterministico sono traguardi successivi.

## 5. WorldState

`WorldStateStore` mantiene la sequenza delle osservazioni, versione dello stato, versione precedente, identificativo dell'osservazione, sorgente e confidenza. Ogni osservazione accettata crea una nuova versione.

Esempio:

`WorldState v41 → azione prevista → risultato osservato → WorldState v42`.

## 6. Decisione e pianificazione

Simulazione e Tactical Ranking non eseguono azioni. Producono candidati, punteggi, confidenza, rischio, ricompensa attesa ed evidenza. L'Orchestrator trasforma i risultati in piani runtime. Il piano rimane un dato finché non attraversa il percorso di autorizzazione configurato.

## 7. Guard, Trust e Safety

Guard valuta il contesto completo. Trust fornisce il livello di autorizzazione applicabile. Safety costituisce il confine finale di autorizzazione per le azioni protette.

Livelli Trust: `OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

## 8. Esecuzione e verifica

Executor/Game Adapter è il confine di esecuzione. Riceve un'azione già autorizzata e non output grezzo di un modello. Verifier riceve risultato e nuova osservazione. Un risultato non verificato non viene considerato automaticamente riuscito.

## 9. RecoveryController e Watchdog

### RecoveryController

Recovery è un controller runtime attivo. Può:

- analizzare il contesto del fallimento;
- comprimere lo storico tramite `VRAMContextSlimmer`;
- riprovare un'azione;
- richiedere o produrre un nuovo piano;
- selezionare una strategia degradata;
- entrare in Cooling;
- cambiare modalità runtime;
- riprendere l'esecuzione quando le condizioni lo consentono;
- adattare budget e strategia secondo policy e condizioni osservate.

### Watchdog

Il Watchdog controlla condizioni runtime e hardware e supporta le modalità:

`NORMAL → DEGRADED → RECOVERY → COOLING → STOPPED`.

Il watchdog hardware può monitorare temperatura CPU/GPU e, quando disponibile, frequenza di I/O. La soglia termica predefinita è 80 °C.

Recovery e Watchdog **non sono vincolati alla sola riduzione o al blocco dell'esecuzione**. Possono adattare strategia e modalità runtime. Le azioni che richiedono autorizzazione continuano comunque a passare dai confini Guard/Trust/Safety configurati.

## 10. Memoria ed evidenza

La memoria distingue esperienza grezza, osservazioni, episodi, ipotesi e conoscenza verificata. Provenienza, confidenza ed eventi di supporto devono essere conservati per le evidenze.

## 11. Provider e hardware

Provider Router utilizza telemetria hardware e policy per scegliere il provider. Le variabili possono includere VRAM/RAM, utilizzo GPU, temperatura, latenza, energia, complessità e prestazioni recenti. L'escalation cloud deve rispettare la policy locale.

## 12. Comunicazione PC/telefono

Il bring-up locale/LAN utilizza messaggi tipizzati e protezione da sequenze/replay. Il ciclo previsto è:

`HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

## 13. Pipeline di percezione

Pipeline produttiva prevista:

`DXGI Direct Capture → Triple Buffer lock-free → HSV multi-ROI → YOLO → OCR glyph-hash con fallback/cache AI-OCR → filtro Kalman 2D temporale → Game State Evaluator → WorldState semantico immutabile`.

I backend produttivi devono essere validati indipendentemente prima dell'uso live.

## 14. Matrice di comunicazione

| Da | A | Contratto/canale | Risultato |
|---|---|---|---|
| Percezione | World Model | `PerceptionWorldAdapter` | osservazione versionata |
| World Model | Simulazione | WorldState immutabile | risultati previsti |
| Simulazione | Tactical Ranking | SimulationResult | candidati valutati |
| Tactical Ranking | Orchestrator | azioni ordinate | decisione di dominio |
| Orchestrator | Planner/Runtime | contratto piano | AgentPlan limitato |
| Decision Provider | Runtime | dati decisionali | candidato/piano |
| Planner | Guard | GuardDecisionContext | valutazione |
| Guard | Trust/Safety | contratto policy | stato autorizzativo |
| Safety | Executor | autorizzazione esplicita | azione o blocco |
| Executor | Percezione | confine osservazione | nuova osservazione |
| Executor | Verifier | risultato azione | evidenza |
| Verifier | Recovery | fallimento strutturato | retry/replan |
| Recovery | Runtime | comando di controllo | nuova modalità/strategia |
| Runtime | EventBus | `RuntimeEvent` | audit/trace |
| Runtime | Memoria | contratto eventi | esperienza/evidenza |
| Hardware | Provider Router | `ResourceSnapshot` | scelta provider |
| PC | Guard telefono | `SessionMessage` autenticato | coordinamento |

## 15. Confini

- Nessun modello linguistico esegue direttamente.
- Percezione non esegue direttamente.
- Tactical Ranking non esegue direttamente.
- Recovery può modificare strategia e modalità runtime.
- Watchdog può modificare modalità runtime e gestione delle risorse.
- Le autorizzazioni protette restano soggette al percorso Guard/Trust/Safety.
- Nessun risultato non verificato viene trattato come successo.
- Le integrazioni live restano dietro traguardi espliciti.
- Gli iscritti a EventBus non acquisiscono autorità di esecuzione.

**Governance versione:** NosAi rimane **1.0 Beta** finché il creatore non richiede esplicitamente una modifica.
