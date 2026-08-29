# NosAi — Centro di controllo

**Versione progetto:** 1.0 Beta

Il Centro di controllo è una dashboard web servita **solo in locale** dal runtime NosAi. Non è un secondo orchestratore: osserva lo stato, presenta provenienza e telemetria e invia comandi espliciti al runtime attraverso il percorso di autorizzazione già definito.

## Obiettivi

- controllo operativo centralizzato;
- ispezione di `WorldState` e delle versioni delle osservazioni;
- visualizzazione di simulazioni, ranking, rischio, confidenza ed evidenza;
- audit di `EventBus` e `Decision Trace`;
- controllo di Watchdog/Recovery e delle modalità runtime;
- monitoraggio delle risorse e degli ingressi del `Provider Router`;
- configurazione delle politiche esposte all'operatore;
- `Eye AI View`: proiezione del client con sovrapposizione della percezione AI.

## Vista Eye AI

La vista deve separare visivamente tre classi di informazione:

1. **Osservato** — fotogramma del client, ROI, oggetti, OCR, coordinate e stato derivati direttamente dalla pipeline di percezione.
2. **Stimato** — previsioni, tracciamento, probabilità/confidenza e `PredictedOutcome`.
3. **Decisionale** — candidato selezionato, ranking, politica applicata, livello Trust ed evidenze.

Ogni elemento deve riportare, quando disponibile: marca temporale, sorgente, versione di `WorldState`, confidenza e stato reale/stimato.

### Monitoraggio del processo decisionale AI

Il Centro di controllo **non espone il chain-of-thought privato**. Espone invece un `Decision Trace` verificabile: ingressi strutturati, candidati, punteggi, rischio, politica, decisioni Guard/Trust/Safety, evidenze, piano, risultato e verifica. Questo mantiene il sistema verificabile senza confondere il ragionamento interno del modello con i dati runtime osservabili.

## Autorità

La dashboard può richiedere azioni, ma non bypassa:

`Planner → Guard → Trust → Safety → Executor`.

La dashboard non deve trasformare un dato visualizzato in un'autorizzazione implicita. I comandi `STOP`, `Recovery`, `Cooling`, `Resume`, `Checkpoint` e `Re-observe` devono essere sottoposti alle politiche del runtime.

## Backend locale

Avvio:

```bash
python -m nosai.dashboard.server
```

Indirizzo predefinito: `http://127.0.0.1:8765`.

Variabili opzionali:

- `NOSAI_DASHBOARD_HOST`
- `NOSAI_DASHBOARD_PORT`

Il server di base non inventa telemetria: quando il runtime o l'adapter di percezione non sono collegati, l'interfaccia mostra esplicitamente `unavailable` / `not connected`.

## Integrazione prevista

Il passo successivo è collegare un `DashboardRuntimeAdapter` al runtime esistente, preferibilmente tramite contratti di `EventBus`, `WorldState` e telemetria, evitando import circolari e accessi diretti ai componenti di esecuzione.

Contratti minimi da esporre:

- `WorldStateSnapshot`;
- `RuntimeEvent`;
- `ResourceSnapshot`;
- `SimulationResult`;
- `AgentPlan` (sola lettura nella UI);
- `GuardDecisionContext` / risultato Safety;
- `VerifierResult`;
- stato `Recovery`;
- fotogramma di percezione + metadati dell'overlay.

## Sicurezza locale

Il server ascolta sull'interfaccia di loopback per impostazione predefinita. Un'eventuale esposizione LAN deve essere un traguardo separato e utilizzare autenticazione, autorizzazione, protezione anti-replay e gli stessi contratti previsti dalla rete NosAi.
