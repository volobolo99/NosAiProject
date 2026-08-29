# NosAi — Architettura, comunicazione e flusso dati

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Stato:** documento architetturale canonico consolidato

## 1. Scopo

Questo è l'unico documento canonico dell'architettura di NosAi. Descrive responsabilità, dati, comunicazioni, autorità, percorso operativo, persistenza, sicurezza e componenti ancora pianificati.

## 2. Architettura generale

```text
SESSIONE / SCHEDULER
        │
RISORSE ─ POLICY ─ PROVIDER ROUTER
        │
CONTROLLO RUNTIME
        │
EVENTBUS / TRACE / AUDIT
        │
        ├──────── PERCEZIONE ────────┐
        │                            ↓
        └──────── MEMORIA       WORLDSTATE vN
                                      │
                           PARTY / PET / PARTNER
                                      │
                                  SIMULAZIONE
                                      │
                               TACTICAL RANKING
                                      │
                                 ORCHESTRATOR
                                      │
                                AGENT PLANNER
                                      │
                            GUARD / TRUST / SAFETY
                                      │
                                  EXECUTOR
                                      │
                               GAME ADAPTER
                                      │
                                  RISULTATO
                                      │
                         VERIFIER + NUOVA OSSERVAZIONE
                                      │
                                  WORLDSTATE vN+1
                                      │
                         ┌────────────┴────────────┐
                        PASS                       FAIL
                         │                         │
                    CHECKPOINT              RECOVERY
                                                   │
                                  retry / replan / degraded / cooling
                                                   │
                                             nuovo ciclo
```

## 3. Percorso critico

Il percorso operativo autorevole è:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Planner → Guard → Trust → Safety → Execute → Verify → Re-observe`.

Nessun subscriber dell'EventBus può inserire effetti di esecuzione in questo percorso.

## 4. Modello delle autorità

| Componente | Decide | Esegue | Concede autorizzazioni |
|---|---|---|---|
| Perception | fatti osservati | No | No |
| World Model | rappresentazione dello stato | No | No |
| Simulation | esiti previsti | No | No |
| Tactical Ranking | ordinamento candidati | No | No |
| Provider/LLM | dati decisionali | No | No |
| Planner | piano limitato | No | No |
| Guard | valutazione sicurezza/policy | No | No |
| Trust Boundary | limite deterministico | No | No |
| Safety Gate | autorizzazione finale | No | Sì, per il proprio confine |
| Executor/Game Adapter | azione autorizzata | **Sì** | No |
| Verifier | verifica risultato | No | No |
| Recovery | strategia di recupero | No direttamente | No |
| Watchdog | controllo modalità/runtime | No direttamente | No |
| EventBus | registrazione/notifica | No | No |

Recovery e Watchdog sono controller runtime attivi: possono modificare strategia, modalità e budget secondo policy e condizioni osservate. Non acquisiscono automaticamente autorità di esecuzione o di concessione Trust.

## 5. EventBus e trace

EventBus è trasversale e osservazionale. Gli eventi devono mantenere almeno identificativo evento, sessione, esecuzione, attività, genitore, timestamp, sorgente, tipo, versione schema e payload strutturato.

Il bus è bounded: la capacità è configurabile e il dropping controllato può interessare i log non critici sotto saturazione. Gli eventi critici non devono essere persi silenziosamente.

Famiglie principali: percezione, WorldState, simulazione, ranking, decisioni, pianificazione, Guard/Safety, azioni, verifica, recovery, replan, memoria, provider, hardware e ciclo di sessione.

## 6. WorldState e provenienza

`WorldStateStore` è la fonte canonica dello stato operativo. Ogni osservazione accettata produce una nuova versione con versione precedente, identificativo osservazione, sorgente e confidenza.

Ciclo concettuale:

`WorldState v41 → Simulation → Action → Observation → WorldState v42`.

SQLite e altri sistemi di persistenza non sostituiscono il WorldState canonico.

## 7. Decisione, simulazione e ranking

Simulation produce `PredictedOutcome`; Tactical Ranking valuta candidati usando score, confidenza, rischio, ricompensa attesa ed evidenza. Nessuno di questi componenti esegue direttamente.

L'Orchestrator coordina il flusso e l'Agent Planner produce un piano limitato. Il piano attraversa Guard, Trust e Safety prima dell'esecuzione.

## 8. Recovery e Watchdog

### RecoveryController

Recovery usa il contesto del fallimento e lo storico compresso per scegliere tra retry, replan, modalità degradata e Cooling. Include circuit breaker con massimo predefinito di tre fallimenti consecutivi, backoff esponenziale e stato `CriticalDeadlock` per fallimenti persistenti.

### Watchdog

Il RuntimeWatchdog gestisce `NORMAL`, `DEGRADED`, `RECOVERY`, `COOLING` e `STOPPED`. Il watchdog hardware può monitorare temperatura CPU/GPU e I/O quando i backend sono disponibili. La soglia termica predefinita è 80 °C.

Timeout sincroni devono poter fallire rapidamente tramite il meccanismo runtime dedicato.

## 9. Riduzione del contesto

`VRAMContextSlimmer` riduce lo storico diagnostico ripetitivo, usa firme deterministiche delle eccezioni e mantiene uno storico limitato. È parte del percorso di recupero, non della fonte canonica dello stato.

## 10. Sicurezza e sessioni

Il repository contiene il nucleo per sessioni effimere con X25519, HKDF-SHA256 e ChaCha20-Poly1305, oltre a test del nucleo su 1000 operazioni. Questo nucleo non deve essere descritto come implementazione completa del protocollo Noise IK/KK finché il trasporto completo non è stato integrato e verificato.

Il bring-up PC/telefono previsto è:

`HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

Nonce, validità della sessione, sequenza e replay devono essere verificati nel trasporto.

## 11. Protobuf e comunicazioni ad alta frequenza

Il contratto Protobuf v3 definisce i messaggi condivisi per stato entità, pacchetti di rete, aggiornamenti UI e tipi correlati. I binding C++/TypeScript generati restano un'attività di integrazione finché non sono presenti nella toolchain.

## 12. Persistenza

`NosAiSqliteLogger` fornisce persistenza locale per sessioni di caccia e traiettorie, con SQLite, WAL, transazioni e inserimento batch.

La persistenza analitica non equivale ancora a una persistenza completa di EventBus, audit, replay e Knowledge Base append-only.

## 13. Miniland

Il modulo `nosai/miniland` contiene il controller Miniland e `FishingAutomation`, separati dall'I/O tramite `MinilandAdapter`. Questa separazione permette di testare il dominio con adapter simulati e di aggiungere successivamente l'adapter specifico del client.

L'integrazione reale del client rimane un traguardo separato e deve passare dai normali confini di autorizzazione, verifica e sicurezza.

## 14. Percezione

Pipeline produttiva prevista:

`DXGI Direct Capture → Triple Buffer lock-free → HSV multi-ROI → YOLO → OCR glyph-hash con fallback/cache AI-OCR → Kalman 2D temporale → Game State Evaluator → WorldState semantico immutabile`.

I backend produttivi devono essere validati prima dell'uso live.

## 15. Provider e hardware

Provider Router è local-first e policy-controlled. Può considerare privacy, complessità, latenza, VRAM/RAM, utilizzo GPU, coda, temperatura, energia e prestazioni recenti.

Discovery hardware, benchmark reali e provider produttivi restano traguardi finché non sono integrati e testati.

## 16. Ciclo memoria ed evidenza

`esperienza → osservazione → episodio → ipotesi → evidenza verificata → strategia riutilizzabile`.

La conoscenza verificata deve conservare provenienza, confidenza ed eventi di supporto. Un fallimento non può diventare automaticamente conoscenza verificata.

## 17. Replay e valutazione

Il replay deve essere orientato alla simulazione e non deve eseguire I/O live. La valutazione deve poter confrontare predizione e realtà, qualità del ranking, confidenza, blocchi Safety, successo dell'esecuzione, recovery, latenza provider e uso delle risorse.

## 18. Matrice di comunicazione

| Produttore | Consumatore | Contratto |
|---|---|---|
| Perception | WorldStateStore | PerceptionWorldUpdate |
| WorldState | Simulation | snapshot immutabile |
| Simulation | Tactical Ranking | SimulationResult |
| Tactical Ranking | Orchestrator | contratto azioni ordinate |
| Orchestrator | Planner | contratto di pianificazione |
| Planner | Guard | GuardDecisionContext |
| Guard | Trust/Safety | contratto autorizzativo |
| Safety | Executor | autorizzazione esplicita |
| Executor | Verifier | risultato/receipt |
| Executor | Perception | confine osservazione |
| Verifier | Recovery | evidenza del fallimento |
| Runtime | EventBus | RuntimeEvent |
| Runtime | Memory/Evaluation | eventi e trace |
| Risorse | Provider Router | ResourceSnapshot |
| Sessione | PC/Telefono | SessionMessage autenticato |
| Miniland | Adapter | MinilandCommand/FishingResult |
| SQLite | Analisi | sessioni/traiettorie |

## 19. Stato implementativo

### Presente

EventBus bounded, WorldState versionato, RecoveryController, circuit breaker, Watchdog runtime/hardware, Context Slimming, sessioni effimere, Protobuf, SQLite iniziale e controller Miniland tramite adapter.

### Fondazioni

Persistenza EventBus, replay durevole, PredictionEvaluator, Knowledge Base append-only, trasporto Noise/mTLS completo, binding Protobuf, Shared Memory/N-API, discovery/benchmark hardware, provider produttivi e backend di percezione.

### Traguardi live

Adapter del client reale, pipeline di percezione produttiva, integrazione PC/telefono, provider locali/cloud, adapter Miniland reale e gate finale di integrazione.

## 20. Confini non negoziabili

1. Un LLM non esegue direttamente.
2. Percezione non esegue.
3. Simulazione non esegue.
4. Tactical Ranking non esegue.
5. Recovery non aumenta il Trust.
6. Watchdog non aumenta il Trust.
7. Un diniego Safety blocca l'azione corrente.
8. Un risultato non verificato non è successo.
9. Un subscriber EventBus non diventa un percorso di esecuzione.
10. Le integrazioni live richiedono gate e test espliciti.

## 21. Risultato architetturale

**NosAi osserva il mondo, costruisce uno stato canonico, prevede gli esiti, ordina le opzioni, pianifica un'azione limitata, la sottopone ai confini di autorizzazione, la esegue attraverso l'Executor, verifica il risultato reale, registra il trace, aggiorna lo stato e ripianifica quando la realtà differisce dalla previsione.**

Questo documento sostituisce qualsiasi precedente documento architetturale duplicato.
