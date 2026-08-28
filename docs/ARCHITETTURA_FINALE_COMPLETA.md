# NosAi — Architettura finale completa

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

## Scopo

Questo documento consolida architettura, responsabilità, comunicazioni e ciclo dati di NosAi.

## Mappa generale

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
        ▼
WORLDSTATE vN
        │
        ├── Party
        ├── Pet
        └── Partner
        │
        ▼
SIMULAZIONE
        │
        ▼
TACTICAL RANKING
        │
        ▼
ORCHESTRATOR
        │
        ▼
AGENT PLANNER
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

## Autorità

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
| Watchdog | gestione stato runtime | no | **sì** |
| EventBus | registrazione | no | no |

## Recovery adattivo

Recovery può scegliere tra retry, replan, replan degradato e Cooling. Può modificare modalità e budget del runtime e può riprendere l'esecuzione quando le condizioni lo permettono.

Il contesto diagnostico può essere ridotto tramite `VRAMContextSlimmer`, conservando firme deterministiche e un numero limitato di errori recenti.

## Watchdog adattivo

Il Watchdog gestisce le modalità:

`NORMAL`, `DEGRADED`, `RECOVERY`, `COOLING`, `STOPPED`.

Il watchdog hardware può utilizzare telemetria CPU/GPU e I/O opzionale. La soglia termica predefinita è 80 °C.

## Percezione produttiva prevista

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
WorldState
```

## EventBus

Gli eventi devono conservare identificativi di correlazione, timestamp, sorgente, tipo, versione dello schema e payload strutturato. Il bus serve audit, telemetria, memoria, evidenza, valutazione e replay.

## Contratti principali

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
- `RuntimeEvent`
- `ResourceSnapshot`
- `SessionMessage`

## Ciclo dati

### Osservazione

`percezione grezza → semantica → validazione → WorldStateStore → versione`

### Decisione

`WorldState + obiettivo → simulazione → ranking → orchestrazione → piano`

### Esecuzione

`piano → autorizzazione → executor → risultato → nuova osservazione`

### Recupero

`fallimento → contesto compatto → strategia Recovery → nuova simulazione/ranking → nuovo ciclo`

## Stato della produzione

Le fondamenta deterministiche sono disponibili. Restano da completare e validare i backend produttivi di percezione, persistenza, rete autenticata, provider LLM, adapter di gioco e integrazione hardware reale.

## Regola linguistica

La documentazione è italiana. Codice, identificatori, API, protocolli e nomi tecnici che richiedono la forma originale possono rimanere in inglese.
