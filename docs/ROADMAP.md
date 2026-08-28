# NosAi — Tabella di marcia

**Versione:** 1.0 Beta  
**Creatore:** Volodymyr Ryzhuk

> La versione rimane 1.0 Beta finché il creatore non richiede esplicitamente una modifica.

## Fase 0 — Fondazione pulita
- [x] Repository dedicato
- [x] Pulizia del repository
- [x] Architettura e regole di migrazione
- [x] Base decisionale deterministica
- [x] Confine Safety Gate
- [x] Protocollo Guard indipendente dal trasporto
- [x] Contratti WorldState / Goal / Action / Decision
- [x] Fondazione World Model
- [x] Fondazioni Party / Pet / Partner
- [x] Fondazione Coordinated Action Manager
- [x] Tactical Ranking + simulazione deterministica
- [x] Contratti/pipeline di Perception
- [x] Adapter Perception → WorldState
- [x] Contratti Agent Runtime, provider, risorse e policy
- [x] Ciclo autonomo multi-step con verifica
- [x] Recovery retry/replan e watchdog indipendente

## Fase 1 — Avvio minimo affidabile
- [ ] Avvio Play AI su PC
- [ ] Avvio Play Guard su PC
- [ ] Avvio Guard AI su telefono
- [ ] Sessione PC ↔ telefono autenticata
- [ ] Scambio HELLO / CAPABILITIES / HEARTBEAT / STATUS
- [ ] Disconnessione/riconnessione sicura e deterministica
- [ ] Validazione di avvio senza client di gioco

## Fase 2 — Guard e decisione sicura
- [x] Fondazione Guard AI
- [x] Confine policy Trust Tier 1–4
- [x] Integrazione Guard/Safety nel ciclo autonomo
- [x] Registro provider e policy local-first
- [x] Fondazione trace di valutazione runtime
- [ ] Guard AI produttivo PC/telefono
- [ ] Propagazione produttiva dello stato watchdog/recovery
- [ ] Integrazione telemetria

## Fase 3 — Percezione e memoria produttive
- [x] Fondazione visione ROI
- [x] Fondazione tracking temporale
- [x] Fondazione Game State Evaluator
- [ ] DXGI Direct Capture
- [ ] Triple Buffer lock-free
- [ ] Detector YOLO produttivo
- [ ] OCR glyph-hash + fallback/cache AI-OCR
- [ ] Tracking Kalman 2D produttivo
- [ ] Valutatore semantico completo specifico del gioco
- [ ] Memoria SQLite
- [ ] Telemetria sincronizzata PTS
- [ ] Rilevamento anomalie e recupero deterministico

## Fase 4 — Confine gioco
- [ ] Probe di sola lettura del client
- [ ] Adapter di azione basato sulla simulazione
- [ ] Adapter live controllato dietro Guard/Safety

## Fase 5 — Strategia e provider AI
- [ ] Progression Engine V2
- [ ] MAUT / UCB1 / HTN-MCTS
- [ ] Aggiornamenti evidenza Beta-Binomial
- [ ] Ciclo di vita strategie e persistenza mastery
- [ ] Knowledge Base
- [ ] Provider locale `llama.cpp`
- [ ] Provider cloud con escalation controllata dalla policy
- [ ] Benchmark hardware e profili runtime automatici

## Fase 6 — Gate di integrazione
- [x] CI con test/compilazione Python e build del runtime C#
- [ ] Test end-to-end deterministici
- [ ] Test di integrazione runtime
- [ ] Gate benchmark hardware
- [ ] Revisione di prontezza al rilascio

## Punti di implementazione esterna

Restano espliciti e non vengono trasformati silenziosamente in dichiarazioni di implementazione:

- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione specifica del client di gioco`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: compatibilità/ricerca anti-cheat`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: integrazione pacchetti/rete`
- `EXTERNAL_IMPLEMENTATION_REQUIRED: bypass/injection specifici del client`

Il progetto pulito non implementa bypass, evasione anti-cheat, manipolazione pacchetti o injection del client come parte dell'avvio minimo.

## Repository legacy

`volobolo99/NosAi` rimane esclusivamente un riferimento. Un componente è considerato migrato solo dopo revisione architetturale, reimplementazione selettiva e test.
