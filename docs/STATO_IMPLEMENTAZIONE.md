# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-08-29

Questo documento registra esclusivamente ciò che è effettivamente presente nel repository `volobolo99/NosAiProject`.

## 🟢 Implementato

- Contratti fondamentali e base decisionale deterministica.
- Confine Safety Gate e integrazione Orchestrator.
- World Model, Party, Pet e Partner.
- Coordinated Action Manager.
- Tactical Action Ranking e fondazioni Simulation/Lookahead.
- Contratti Perception, pipeline iniettabile, visione ROI e fondazione tracking.
- Fondazione Game State Evaluator e adapter Perception → WorldState.
- Fondazione Agent Runtime: sessioni, memoria, instradamento provider local-first, risorse, policy e Trust Tier 0–4.
- Ciclo Planner → Guard → Safety → Executor → Verifier multi-step.
- Retry/ripianificazione, checkpoint e watchdog indipendente.
- ToolRegistry, profilazione hardware, contratti LAN e protezione sequenza/replay.
- Primitive di trace per valutazione Agent.
- Bridge Orchestrator → Agent Runtime.
- Runtime di osservazione/ripianificazione a ciclo chiuso.
- EventBus runtime tipizzato con correlazione evento/esecuzione/sessione/attività.
- EventBus bounded con limite configurabile e dropping controllato dei log non critici.
- WorldState versionato con provenienza, confidenza e cronologia.
- Context slimming orientato alla VRAM con firme deterministiche delle eccezioni e storico limitato.
- RecoveryController adattivo con retry/replan/degraded-replan/cooling.
- Circuit breaker Recovery con massimo predefinito di 3 fallimenti consecutivi e backoff esponenziale.
- Eccezione `CriticalDeadlock` per loop di fallimento persistente.
- RuntimeWatchdog con modalità NORMAL, DEGRADED, RECOVERY, COOLING e STOPPED.
- Hardware watchdog con monitoraggio termico CPU/GPU e I/O opzionale.
- Timeout fail-fast per blocchi sincroni tramite `RuntimeTimeout` e `run_with_timeout`.
- Contratto Protobuf v3 per `EntityState`, `NetworkPacket`, `UIFrameUpdate` e tipi correlati.
- Nonce crittograficamente casuale e validazione rafforzata nel protocollo di bring-up LAN.
- Documentazione architetturale consolidata e aggiornata in italiano.

## 🟡 Fondazioni — non complete per la produzione

- Persistenza EventBus, audit/replay durevole e trasporto tra processi.
- Valutatore predizione-vs-realtà e metriche produttive.
- Ranking basato su evidenza e persistenza del ciclo di vita della conoscenza verificata.
- Integrazione produttiva Guard AI / Watchdog / Recovery tra PC e telefono.
- TLS/mTLS o Noise completo per il trasporto LAN.
- Generazione e integrazione dei binding Protobuf C++/TypeScript nella toolchain.
- Discovery hardware, probing e benchmark reali.
- Memoria SQLite durevole.
- Sandbox strumenti e applicazione produttiva delle capability.
- Backend produttivi DXGI, Triple Buffer, YOLO, OCR, Kalman e mapping specifico del gioco.
- Adapter live del gioco/client.
- Provider locale `llama.cpp` e provider cloud.
- Benchmark IPC ad alta densità e validazione della Saturazione Controllata.

## 🔴 Non ancora implementato

### Runtime e integrazione

- Persistenza durevole EventBus e runner di replay deterministico.
- PredictionEvaluator completo e pipeline di evidenza delle strategie.
- Integrazione produttiva Planner con World Model + Simulation + Tactical Ranking.
- Propagazione produttiva Guard AI/Watchdog/Recovery PC-telefono.
- Bring-up produttivo Play AI + Play Guard PC + Guard AI telefono.
- Trasporto LAN autenticato completo con TLS/Noise.
- Sandbox strumenti produttiva e autorizzazione basata su capability.

### Apprendimento e strategia

- Progression Engine V2 runtime.
- MAUT / UCB1 / HTN-MCTS.
- Aggiornamenti evidenza Beta-Binomial.
- Ciclo di vita strategie e persistenza mastery.
- Knowledge Base persistente append-only.

### Percezione e telemetria

- DXGI Direct Capture produttivo.
- Triple Buffer lock-free produttivo.
- YOLO produttivo.
- OCR glyph-hash con fallback/cache AI-OCR.
- Tracking Kalman 2D produttivo.
- Valutatore semantico completo specifico del gioco.
- Sincronizzazione PTS.
- Rilevamento anomalie e recupero collegati alla telemetria live.

### Confine gioco e provider AI

- Probe client in sola lettura.
- Adapter azioni basato sulla simulazione.
- Adapter live controllato.
- Provider locale `llama.cpp`.
- Provider cloud con escalation controllata dalla policy.
- Benchmark hardware reale e profili runtime automatici.
- Gate finale di integrazione/rilascio.

## Ottimizzazioni v1.6/v1.7 recepite

Sono state recepite le parti architetturalmente compatibili delle specifiche allegate:

- timeout fail-fast;
- circuit breaker e backoff della Recovery;
- code bounded per EventBus;
- nonce e validazione del protocollo;
- contratto Protobuf v3;
- obiettivi di stress test multi-entità;
- modalità di Saturazione Controllata come obiettivo di validazione;
- separazione decisione/esecuzione del Game Adapter;
- requisito di persistenza append-only delle evidenze.

Le latenze numeriche dichiarate nei documenti allegati sono trattate come **obiettivi di benchmark**, non come prestazioni garantite dal codice.

La parte di riconnessione descritta come tecnica per simulare il comportamento umano allo scopo di evitare sistemi anti-cheat non è stata implementata. NosAi implementerà invece una riconnessione orientata ad affidabilità, rate limiting, autenticazione, TTL e arresto sicuro.

## Percorso corrente

```text
Perception → WorldState → Party/Pet/Partner → Simulation → Tactical Ranking
→ Orchestrator → Agent Planner/Runtime → Guard/Trust/Safety
→ Executor/Game Adapter → Verifier + nuova osservazione → WorldState(vN+1)
                                  │
                                  └─ fallimento → RecoveryController
                                                  ├─ Context Slimming
                                                  ├─ retry / replan
                                                  ├─ circuit breaker
                                                  ├─ modalità degradata
                                                  └─ cooling

Hardware → HardwareWatchdog → segnale runtime
EventBus bounded → ciclo di vita, policy, provider, risorse, azioni, safety, memoria, valutazione e recovery
LAN → protocollo tipizzato + nonce → futura autenticazione crittografica completa
Protobuf → contratto binario versionabile per flussi ad alta frequenza
```

## Prossimo ordine di implementazione

1. Integrare completamente timeout, RecoveryController e HardwareWatchdog nel ciclo Agent Runtime.
2. Persistenza EventBus + replay deterministico.
3. PredictionEvaluator e metriche predizione-vs-realtà.
4. Ranking basato su evidenza + ciclo di vita della conoscenza.
5. Persistenza append-only della Knowledge Base.
6. Guard AI produttivo + integrazione PC/telefono.
7. TLS/Noise per il trasporto LAN e gestione TTL/sessione.
8. Generazione binding Protobuf e integrazione Control Center/Eye AI View.
9. SQLite e recupero durevole delle sessioni.
10. Discovery/benchmark hardware e profili automatici.
11. Provider locale `llama.cpp` e fallback cloud controllato dalla policy.
12. Adapter Perception/Game produttivi e gate finale.
