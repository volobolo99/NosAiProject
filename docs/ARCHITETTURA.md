# NosAi — Architettura completa e modello di comunicazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## Scopo

Questo è l'unico documento ufficiale dell'architettura di NosAi. Consolida architettura, responsabilità, comunicazioni, autorità, prestazioni e ciclo dei dati.

## 1. Mappa generale

```text
SESSIONE / SCHEDULER
        │
RISORSE ── POLICY ── PROVIDER ROUTER ── MEMORIA ── STRUMENTI
        │
CONTROLLO RUNTIME ── WATCHDOG ── RECOVERY ── VALUTAZIONE
        │
EVENTBUS / TRACE
        │
PERCEZIONE → WORLDSTATE(vN) → SIMULAZIONE → RANKING
                                      │
                                 ORCHESTRATOR
                                      │
                                  PLANNER
                                      │
                              GUARD / TRUST / SAFETY
                                      │
                             EXECUTOR / ADAPTER
                                      │
                                  VERIFIER
                                      │
                              WORLDSTATE(vN+1)
                                      │
                         PASS ───────┴──── FAIL
                                                │
                              CONTEXT SLIMMING / RECOVERY
                                                │
                                  retry / replan / degraded / cooling
                                                │
                                         nuovo ciclo
```

## 2. Percorso critico e timeout

Il percorso principale è:

`Observe → WorldState → Simulation → Ranking → Orchestrator → Plan → Guard → Trust → Safety → Execute → Verify → Re-observe`.

I blocchi sincroni di valutazione devono utilizzare un timeout fail-fast configurabile, con valore predefinito di 200 ms per blocco escluso il Planner. Un timeout deve produrre un evento runtime e attivare il percorso di Recovery/Watchdog previsto dalla policy, evitando attese indefinite.

Il modulo `nosai.runtime.timeouts` fornisce `run_with_timeout()` e `RuntimeTimeout` per rendere questo comportamento testabile.

## 3. Autorità dei componenti

| Componente | Responsabilità | Esecuzione diretta | Cambio strategia |
|---|---|---:|---:|
| Percezione | fatti osservati | No | No |
| WorldState | stato canonico | No | No |
| Simulazione | risultati previsti | No | No |
| Tactical Ranking | ordine candidati | No | No |
| Decision Provider | dati decisionali | No | Proposta |
| Planner | piano | No | Sì |
| Guard | valutazione | No | Sì |
| Trust | autorizzazione secondo policy | No | Secondo policy |
| Safety | autorizzazione finale | No | Secondo policy |
| Executor | esecuzione | Sì | No |
| Verifier | verifica | No | No |
| Recovery | recupero e nuova strategia | No | **Sì** |
| Watchdog | gestione runtime | No | **Sì** |
| EventBus | osservazione/audit | No | No |

Recovery e Watchdog sono controller attivi. Possono cambiare strategia, modalità e budget runtime; non costituiscono però un canale di esecuzione diretto alternativo all'Executor.

## 4. Recovery e Circuit Breaker

Recovery utilizza `VRAMContextSlimmer` per comprimere lo storico diagnostico e supporta retry, replan, modalità degradata e Cooling.

Il controller implementa un circuit breaker configurabile con predefinito di 3 fallimenti consecutivi per macro-azione. Il backoff è esponenziale:

`backoff(n) = base × 2^(n-1)`.

Dopo il superamento della soglia viene sollevato `CriticalDeadlock` e il ciclo automatico viene interrotto. Un successo azzera il contatore della macro-azione.

## 5. Watchdog e gestione hardware

Il Watchdog supporta le modalità:

`NORMAL → DEGRADED → RECOVERY → COOLING → STOPPED`.

Il watchdog hardware può osservare temperatura CPU/GPU e I/O quando disponibili. La soglia termica predefinita è 80 °C. La gestione hardware deve rimanere osservabile e non deve assumere come garantite prestazioni non misurate.

Le tecniche di ottimizzazione che dipendono da privilegi del sistema operativo, pinning della memoria fisica o impostazioni specifiche del kernel devono essere implementate come adapter opzionali e validate per piattaforma; non vengono dichiarate automaticamente disponibili su tutti i sistemi.

## 6. Context Slimming e VRAM

`VRAMContextSlimmer` conserva un numero limitato di errori recenti, normalizza elementi variabili come indirizzi e numeri di riga e produce firme deterministiche. L'obiettivo è ridurre il contesto diagnostico senza perdere la causa sintetica del fallimento.

## 7. EventBus bounded

EventBus utilizza una coda a capacità finita. I log non critici possono essere scartati quando la coda raggiunge il limite; gli eventi critici vengono preservati tramite sostituzione controllata dell'evento meno recente. Il contatore `dropped_noncritical` rende misurabile la perdita di telemetria.

Gli iscritti restano osservatori e non acquisiscono autorità di esecuzione.

## 8. Contratto binario Protobuf

Per i flussi ad alta frequenza verso Control Center/Eye AI View è stato aggiunto `proto/nosai_network_v1.proto` con sintassi Protobuf v3.

Il contratto definisce:

- `EntityType`;
- `Vector2D`;
- `NetworkPacket`;
- `EntityState`;
- `UIFrameUpdate`.

Il contratto è versionabile e adatto alla generazione di binding C++ e TypeScript/JavaScript. La generazione dei binding viene mantenuta separata dal codice sorgente finché la toolchain Protobuf non è formalizzata nel progetto.

## 9. Comunicazione LAN e nonce

Il protocollo di bring-up ora usa messaggi con nonce crittograficamente casuale e validazione della versione. Il nonce protegge l'identificazione del messaggio, ma **da solo non equivale a mTLS**: l'autenticazione reciproca del trasporto deve essere aggiunta mediante una implementazione TLS/Noise validata prima dell'uso produttivo.

Il protocollo previsto è:

`HELLO → CAPABILITIES → AUTH → HEARTBEAT/STATUS → COMMAND/EVENT → ACK/ERROR → DISCONNECT`.

La sessione deve inoltre validare sequenza, identità del dispositivo, autorizzazione e TTL.

## 10. WorldState

`WorldStateStore` mantiene osservazioni versionate, identificativo dell'osservazione, sorgente, confidenza e collegamento allo stato precedente.

## 11. Simulazione e Tactical Ranking

Questi moduli producono risultati previsti, candidati, punteggi, rischio, confidenza, ricompensa attesa ed evidenza. Non eseguono direttamente azioni.

## 12. Guard, Trust e Safety

Il percorso di autorizzazione rimane:

`AgentPlan → GuardDecisionContext → Guard → Trust → Safety → Executor`.

I livelli Trust sono:

`OBSERVE (0) → SIMULATE (1) → REVERSIBLE (2) → SENSITIVE (3) → CRITICAL (4)`.

## 13. Executor, Verifier e Adapter

Executor è l'unico confine tecnico di esecuzione delle primitive autorizzate. Un eventuale Live Game Adapter deve limitarsi a tradurre primitive standardizzate, senza introdurre logica decisionale.

Verifier confronta risultato e nuova osservazione. Un risultato non verificato non diventa automaticamente successo o conoscenza verificata.

## 14. Memoria ed evidenza

Le evidenze verificate devono essere persistite in modalità append-only. Un'esperienza già verificata non deve essere sovrascritta da un'esecuzione successiva fallita.

## 15. Provider e instradamento

Provider Router deve favorire il local-first quando il profilo hardware e la policy lo consentono. Il caching locale dello stato può ridurre la latenza del percorso sincrono. Le decisioni di instradamento devono tenere conto di risorse, latenza, temperatura, carico e complessità.

## 16. Pipeline di percezione

Pipeline prevista:

```text
DXGI Direct Capture
 → Triple Buffer lock-free
 → HSV multi-ROI
 → YOLO
 → OCR glyph-hash
 → AI-OCR fallback/cache
 → Kalman 2D temporale
 → Game State Evaluator
 → WorldState
```

I componenti live devono essere considerati produttivi solo dopo validazione indipendente.

## 17. Stress test e saturazione controllata

Il test di carico deve misurare entità simultanee, pacchetti/sec, latenza IPC, packet drop rate e race conditions. La metodologia di riferimento considera scenari da 50 a 350 entità.

Sopra la soglia operativa configurata, il sistema può entrare in **Saturazione Controllata**, dando priorità ai dati essenziali per lo stato operativo e degradando i flussi non critici. La soglia deve essere configurabile e validata con benchmark reali; i valori del documento allegato sono obiettivi di test, non prestazioni garantite.

## 18. Relogging e riconnessione

La riconnessione deve essere gestita come macchina a stati con timeout, numero massimo di tentativi, riallineamento del WorldState e arresto sicuro in caso di fallimento persistente.

Non viene implementato un meccanismo finalizzato a eludere sistemi anti-cheat o a simulare deliberatamente il comportamento umano per aggirare controlli. I ritardi di riconnessione devono essere definiti da affidabilità, rate limiting e policy di servizio.

## 19. Control Center / Eye AI View

Il Control Center locale è il piano di osservazione e controllo del sistema. Deve poter mostrare stato runtime, WorldState, simulazioni, ranking, piani, autorizzazioni, eventi, Recovery, Watchdog, risorse hardware, provider, rete e metriche.

Il formato Protobuf può essere utilizzato per i flussi ad alta frequenza, mentre contratti più leggibili possono restare disponibili per configurazione, debug e API amministrative.

## 20. Matrice di rischio

| Rischio | Mitigazione |
|---|---|
| Allucinazione LLM | validazione schema + Guard/Policy |
| Latenza provider | timeout + local-first + cache |
| Saturazione EventBus | code bounded + dropping non critico |
| Compromissione LAN | autenticazione reciproca + nonce + TTL + sequenze |
| Loop di Recovery | circuit breaker + `CriticalDeadlock` |
| Sovraccarico hardware | Watchdog + modalità degradata/Cooling |
| Evidenza corrotta | persistenza append-only |
| Adapter con logica nascosta | separazione Executor/Adapter |

## 21. Ciclo dati

### Osservazione
`percezione → semantica → validazione → WorldStateStore → versione`

### Decisione
`WorldState + obiettivo → simulazione → ranking → orchestrazione → piano`

### Esecuzione
`piano → Guard → Trust → Safety → Executor → risultato → verifica`

### Recupero
`fallimento → Context Slimming → Recovery → backoff/replan → nuova valutazione`

## 22. Stato della produzione

Implementato nel core: EventBus bounded, Context Slimming, Recovery adattivo con circuit breaker, Watchdog runtime/hardware, timeout fail-fast e contratto Protobuf v3.

Parzialmente implementato/fondazione: protocollo LAN con nonce, Control Center, provider routing e hardware telemetry.

Da validare prima della produzione: TLS/Noise completo, binding Protobuf generati, persistenza append-only definitiva, adapter di gioco live, pipeline percezione produttiva, benchmark IPC/latency e integrazione hardware specifica.

## 23. Lingua e governance

La documentazione ufficiale è italiana. Codice, identificatori, API, protocolli e nomi tecnici obbligatori possono rimanere in inglese.

NosAi rimane **1.0 Beta** finché il creatore non richiede esplicitamente una modifica.
