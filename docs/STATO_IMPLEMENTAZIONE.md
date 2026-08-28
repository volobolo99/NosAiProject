# NosAi — Stato dell'implementazione

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk  
**Aggiornato:** 2026-08-28

Questo documento è il registro dell'implementazione effettiva del repository `volobolo99/NosAiProject`.

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
- WorldState versionato con provenienza, confidenza e cronologia.
- Context slimming orientato alla VRAM con firme deterministiche delle eccezioni e storico limitato.
- RecoveryController adattivo con retry/replan/degraded-replan/cooling.
- RuntimeWatchdog con modalità NORMAL, DEGRADED, RECOVERY, COOLING e STOPPED.
- Hardware watchdog con monitoraggio termico CPU/GPU e I/O opzionale.
- Documentazione architetturale consolidata in italiano.

## 🟡 Fondazioni — non complete per la produzione

- Persistenza EventBus, audit/replay durevole e trasporto tra processi.
- Valutatore predizione-vs-realtà e metriche produttive.
- Ranking basato su evidenza e persistenza del ciclo di vita della conoscenza verificata.
- Integrazione produttiva Guard AI / Watchdog / Recovery tra PC e telefono.
- Discovery hardware, probing e benchmark reali.
- Memoria SQLite durevole.
- Trasporto LAN autenticato e instaurazione crittografica della sessione.
- Sandbox strumenti e applicazione produttiva delle capability.
- Backend produttivi DXGI, Triple Buffer, YOLO, OCR, Kalman e mapping specifico del gioco.
- Adapter live del gioco/client.
- Provider locale `llama.cpp` e provider cloud.

## 🔴 Non ancora implementato

### Runtime e integrazione

- Persistenza durevole EventBus e runner di replay deterministico.
- PredictionEvaluator completo e pipeline di evidenza delle strategie.
- Integrazione produttiva Planner con World Model + Simulation + Tactical Ranking.
- Propagazione produttiva Guard AI/Watchdog/Recovery PC-telefono.
- Bring-up produttivo Play AI + Play Guard PC + Guard AI telefono.
- Trasporto LAN autenticato completo.
- Sandbox strumenti produttiva e autorizzazione basata su capability.

### Apprendimento e strategia

- Progression Engine V2 runtime.
- MAUT / UCB1 / HTN-MCTS.
- Aggiornamenti evidenza Beta-Binomial.
- Ciclo di vita strategie e persistenza mastery.
- Knowledge Base persistente.

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

## Percorso corrente

```text
Perception → WorldState → Party/Pet/Partner → Simulation → Tactical Ranking
→ Orchestrator → Agent Planner/Runtime → Guard/Trust/Safety
→ Executor/Game Adapter → Verifier + nuova osservazione → WorldState(vN+1)
                                  │
                                  └─ fallimento → RecoveryController
                                                  ├─ Context Slimming
                                                  ├─ retry / replan
                                                  ├─ modalità degradata
                                                  └─ cooling

Hardware → HardwareWatchdog → segnale runtime
EventBus → ciclo di vita, policy, provider, risorse, azioni, safety, memoria, valutazione e recovery
```

## Prossimo ordine di implementazione

1. Integrare completamente RecoveryController e segnali HardwareWatchdog nel ciclo produttivo Agent Runtime.
2. Persistenza EventBus + replay.
3. PredictionEvaluator e metriche predizione-vs-realtà.
4. Ranking basato su evidenza + ciclo di vita della conoscenza.
5. Guard AI produttivo + integrazione PC/telefono.
6. Trasporto LAN autenticato e riconnessione deterministica.
7. SQLite e recupero durevole delle sessioni.
8. Discovery/benchmark hardware e profili automatici.
9. Provider locale `llama.cpp` e fallback cloud controllato dalla policy.
10. Adapter Perception/Game produttivi e gate finale.
