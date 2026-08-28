# NosAi Control Center

**Versione progetto: 1.0 Beta**

Il Control Center è una dashboard web servita **solo in locale** dal runtime NosAi. Non è un secondo orchestratore: osserva lo stato, presenta provenienza e telemetria e invia comandi espliciti al runtime attraverso il percorso di autorizzazione già definito.

## Obiettivi

- controllo operativo centralizzato;
- ispezione di `WorldState` e versioni delle osservazioni;
- visualizzazione di simulazioni, ranking, rischio, confidenza ed evidenza;
- audit del `EventBus` e del decision trace;
- controllo di Watchdog/Recovery e modalità runtime;
- monitoraggio risorse e input del Provider Router;
- configurazione delle policy esposte all'operatore;
- `Eye Ai View`: proiezione del client + overlay della percezione AI.

## Eye Ai View

La vista deve separare visivamente tre classi di informazione:

1. **Osservato** — frame client, ROI, oggetti, OCR, coordinate e stato derivati direttamente dalla pipeline di percezione.
2. **Stimato** — predizioni, tracking, probabilità/confidenza e `PredictedOutcome`.
3. **Decisionale** — candidato selezionato, ranking, policy applicata, Trust level ed evidenze.

Ogni elemento deve riportare, quando disponibile: timestamp, sorgente, `WorldState` version, confidence e stato real/stimato.

### Monitoraggio del pensiero AI

Il Control Center **non espone chain-of-thought privato**. Al suo posto espone un `Decision Trace` verificabile: input strutturati, candidati, score, rischio, policy, Guard/Trust/Safety decision, evidenze, piano, risultato e verifica. Questo mantiene il sistema auditabile senza confondere ragionamento interno del modello con dati runtime osservabili.

## Autorità

Il dashboard può richiedere azioni, ma non bypassa:

`Planner → Guard → Trust → Safety → Executor`.

La dashboard non deve trasformare un dato visualizzato in un'autorizzazione implicita. I comandi `STOP`, `Recovery`, `Cooling`, `Resume`, `Checkpoint` e `Re-observe` devono essere sottoposti alle policy del runtime.

## Backend locale

Avvio:

```bash
python -m nosai.dashboard.server
```

Default: `http://127.0.0.1:8765`.

Variabili opzionali:

- `NOSAI_DASHBOARD_HOST`
- `NOSAI_DASHBOARD_PORT`

Il server base non inventa telemetria: quando il runtime/perception adapter non è collegato, l'interfaccia mostra esplicitamente `unavailable`/`not connected`.

## Integrazione prevista

Il passo successivo è collegare un `DashboardRuntimeAdapter` al runtime esistente, preferibilmente tramite EventBus/WorldState/telemetry contracts, evitando import circolari e accessi diretti ai componenti di esecuzione.

Contratti minimi da esporre:

- `WorldStateSnapshot`
- `RuntimeEvent`
- `ResourceSnapshot`
- `SimulationResult`
- `AgentPlan` (read-only nella UI)
- `GuardDecisionContext` / Safety result
- `VerifierResult`
- `Recovery status`
- perception frame + overlay metadata

## Sicurezza locale

Il server ascolta su loopback per default. Un'eventuale esposizione LAN deve essere un traguardo separato e usare autenticazione, autorizzazione, anti-replay e gli stessi contratti previsti dalla rete NosAi.
