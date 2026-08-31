# NosAi — Centro di controllo

**Versione progetto:** 1.0 Beta

Il Centro di controllo è una dashboard **solo locale**. Non è un secondo orchestratore: osserva lo stato, presenta provenienza e telemetria e invia comandi espliciti al runtime attraverso il percorso di autorizzazione già definito.

Per l'uso quotidiano su Windows, senza riga di comando:

```powershell
.\scripts\windows\start_control_panel.ps1
```

L'eseguibile è `src/NosAi.ControlPanel/bin/Release/net8.0-windows/NosAi.ControlPanel.exe` (su questa macchina: `C:\Users\volob\Desktop\NosAiProject\src\NosAi.ControlPanel\bin\Release\net8.0-windows\NosAi.ControlPanel.exe`). `NosAi.Runtime.exe` nella stessa cartella è il runtime a console, non il pannello.

Lo script `scripts/windows/start_control_panel.ps1` compila se l'exe manca; se la build fallisce non avvia nulla.

Nella sezione Percezione il probe DXGI misura anche le barre HP/MP del ritaglio HUD: il riempimento è `DERIVED` solo se il profilo colonne sembra una barra (un transitorio pieno/vuoto, o piena nel colore atteso). Una zona scura o rumorosa resta `UNKNOWN`, non 0%. I numeri restano `UNKNOWN` finché non esiste un atlante di glifi addestrato. I ritagli finiscono in `data/perception/crops/` (gitignored) per ispezionare la ROI. Niente di questo entra nello snapshot `gate1.snapshot.v1`.

Modalità: **OSPITATO** (questo processo è il runtime; Ferma lo spegne) oppure **COLLEGATO** (runtime già in ascolto; Scollega non lo spegne). Mostra la versione wire di **questo build** (oggi v3) e lo slot Guard derivato da collegato/autenticato: non inventa una versione letta dal filo. L'abbinamento richiede Python; senza Python non è riuscito. Il giro v3 sul telefono resta un promemoria, non Verified. Dettaglio: `src/NosAi.ControlPanel/README.md`.

Il Centro di controllo web Python resta disponibile per chi preferisce il browser.

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

Il Centro di controllo è composto da **due processi distinti su due porte distinte**:

| Processo | Porta predefinita | Ruolo |
|---|---|---|
| `python -m nosai.dashboard.server` | `8765` | interfaccia operatore servita al browser |
| `dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll` | `8766` | API operatore del runtime (`/api/gate1`, `/api/health`, `/api/command`) |
| `NosAiMasterRuntimeHost` | `8767` | Centro di Controllo Master (`--host-test` e host alternativo) |
| `Gate5IntegratedEngine` | `8768` | Control Center Gate 5 |

Le porte non devono coincidere: due server HTTP sulla stessa porta non convivono e
il secondo processo che parte non riesce ad aprire l'ascolto.

Avvio, nell'ordine:

```bash
# 1. runtime (espone /api/gate1 su 8766 e stampa l'URL esatto all'avvio)
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll

# 2. interfaccia operatore (legge il runtime e serve il browser su 8765)
python -m nosai.dashboard.server
```

Indirizzo dell'interfaccia: `http://127.0.0.1:8765`.

Variabili opzionali:

- `NOSAI_DASHBOARD_HOST`
- `NOSAI_DASHBOARD_PORT`
- `NOSAI_RUNTIME_URL` — origine del runtime da interrogare, predefinita
  `http://127.0.0.1:8766`. Va cambiata solo se il runtime è stato avviato con
  `--dashboard-port` diverso dal valore predefinito.

Opzioni del runtime:

- `--dashboard-port <n>` (o `NOSAI_DASHBOARD_PORT`) — porta dell'API operatore;
  `0` seleziona una porta libera e il runtime stampa quella effettivamente aperta;
- `--no-dashboard` — avvia il runtime senza API operatore.

Se la porta richiesta è occupata, il runtime **non termina**: prosegue con canale
Guard e client collegati, segnala `dashboard_port_in_use:<porta>` e riporta
l'API operatore come non disponibile. La dashboard è una superficie di
osservazione, non un gate di sicurezza, e non deve poter abbattere il runtime.

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
