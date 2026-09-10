# NosAiProject — Mappa del sistema e delle comunicazioni

**Scopo:** descrivere il funzionamento end-to-end e indicare dove trovare ogni modulo, contratto, test e decisione senza rileggere il repository intero.  
**Fonte di precedenza:** `docs/SOURCE_OF_TRUTH.md`, `docs/NOSAI_ARCHITECTURE_BASELINE.md`, `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`.

## 1. Regola fondamentale

Il percorso di controllo del gioco è locale, deterministico dove possibile e fail-closed:

```
Observe → Sensor Fusion → World Model → Simulation/Prediction
→ Ranking → Planner/Orchestrator → Guard → Trust
→ Safety → Execute → Verify → Re-observe
```

Un modello linguistico, il pannello e il MCP possono proporre interpretazioni o piani; nessuno di essi può saltare Guard, Trust, Safety o la verifica post-azione.

## 2. Piani del sistema

| Piano | Implementazione | Responsabilità | Autorità |
|---|---|---|---|
| Live control plane | `src/NosAi.Runtime/`, `src/NosAi.Core/`, `src/NosAi.Adapter/` | osservazione, stato del mondo, decisione, esecuzione e verifica | autorità operativa locale |
| Operator plane | `src/NosAi.ControlPanel/`, `src/NosAi.Host/` | avvio, diagnostica, configurazione, stato e comandi dell’operatore | nessun bypass di Safety |
| MCP advisory plane | `nosai/mcp/`, `scripts/mcp_hub_server.py`, dashboard su `127.0.0.1:8770` | routing modelli, simulazione, ricerca, memoria, audit e proposte | advisory; mai esecuzione diretta |
| Evidence plane | `src/NosAi.Storage/`, `src/NosAi.Runtime/*Replay*`, `data/` | journal, replay, metriche, provenienza e risultati | evidenza, non verità privilegiata |
| Development plane | `contracts/`, `schemas/`, `docs/agents/`, `tests/`, `.github/` | contratti, task, test, CI e handoff fra agenti | gate di integrazione |

## 3. Comunicazione end-to-end

| Da → A | Dati/contratto | Implementazione da consultare | Test/verifica |
|---|---|---|---|
| Client → Capture | frame o pacchetto osservabile | `src/NosAi.Runtime/LiveIntegration/Capture/` | `CaptureEngineTests`, replay |
| Capture → Perception | osservazioni con timestamp, freshness e provenance | `src/NosAi.Runtime/Perception/` | test di capture/perception |
| Perception → World Model | fatti classificati, inclusi `UNKNOWN` | `src/NosAi.Runtime/WorldModel/Fusion/`, `src/NosAi.Core/WorldModel/` | test WorldModel/Fusion |
| World Model → Simulation | snapshot immutabile e contesto slim | `src/NosAi.Runtime/Gate2/`, `src/NosAi.Runtime/Gate3/` | replay deterministico |
| Simulation → Ranking/Planner | outcome ipotetici, costo, rischio e confidence | `Gate3Runtime.cs`, `Tactical/`, `Navigation/` | test ranking/planner |
| Planner → Guard/Trust/Safety | intent/action candidate con digest e precondizioni | `src/NosAi.Runtime/Contracts/`, `Guard/`, `Safety/` | test negativi e fail-closed |
| Safety → Adapter | solo azioni autorizzate | `src/NosAi.Adapter/`, `src/NosAi.Runtime/Tactical/` | post-condition e receipt |
| Adapter → Verify | risultato osservabile o `UNKNOWN` | `src/NosAi.Runtime/Gate3/PostConditions.cs` | verification tests |
| Runtime → Storage | eventi, risultati, hash-chain e replay | `src/NosAi.Storage/`, `Gate2/EventLog*` | journal/replay tests |
| Runtime → Control Panel | snapshot operativo classificato | `Gate1/Gate1CanonicalSnapshot.cs`, `src/NosAi.ControlPanel/` | Gate 1 / dashboard smoke |
| MCP → Runtime | solo proposta, evidenza o skill validata | `nosai/mcp/server.py`, `policy.py`, `learning.py` | `contracts/mcp-hub-001.json` |
| Control Panel → MCP | configurazione, attivazione rete, provider, audit | `nosai/mcp/dashboard.py`, `scripts/mcp_dashboard_server.py` | API smoke + policy tests |

## 4. Moduli principali

### C# live runtime

- **Core contracts:** tipi condivisi e invarianti senza dipendenze.
- **Protocol/Security:** framing, sessione, cifratura, capability e anti-replay.
- **LiveIntegration/Capture:** sorgenti di traffico, replay e osservazione del client.
- **Perception:** sensori Network/Memory/Screen/Local e riduzione in osservazioni.
- **WorldModel:** fusione, identità, temporalità, mappe, quest, inventario e stato.
- **Gate 1:** bootstrap, identità runtime, hardware, canale operatore e snapshot.
- **Gate 2:** Observe → WorldState, delta-sync, log e persistenza.
- **Gate 3:** simulazione, ranking, planner, esecuzione autorizzata e post-condition.
- **Tactical/Navigation/Raids:** strategie di movimento e combattimento specializzate.
- **Storage:** journal SQLite, WAL, replay e provenienza.
- **ControlPanel/Host:** superfici operative e diagnostica; non sono autorità di gioco.

L’elenco di classi e firme pubbliche è mantenuto in `docs/INDICE_REPO.md`; prima di modificare un metodo, cercare il tipo lì e poi aprire il test indicato.

### Python e MCP

- `nosai/mcp/contracts.py`: request/response versionate.
- `policy.py`: rete esplicita, rifiuto privilegi e secret-export.
- `router.py`: selezione per capability, costo, latenza e disponibilità.
- `inference.py`: gateway locale/online; online solo con MCP Rete attivo.
- `simulation.py`: calcolo puro e riproducibile.
- `learning.py`: candidate → validate → skill offline con evidenza.
- `secrets.py`: cifratura locale; nessun plaintext restituito.
- `audit.py`: audit JSONL redatto.
- `director.py`: propone modifiche al MCP in aree consentite.
- `auditor.py`: verifica indipendente, veto, sospensione e rollback.
- `roles.py`: verifica copertura dei ruoli.
- `server.py` e dashboard: espongono tools/resources senza autorità di esecuzione.

## 5. Stato dei confini da non confondere

- Il runtime C# possiede oggi il percorso live e la Safety authority.
- Il package Python `nosai/` contiene runtime/tooling e MCP; il contratto di proprietà dello stato fra C# e Python è ancora aperto nel ledger come **C-204**.
- `GuardClient` e `GuardAi.App` restano nel tree per compatibilità/storico, ma la baseline corrente non li considera necessari al percorso PC-first; non introdurre nuove dipendenze mobile senza ADR.
- `tools/deepseek-mcp` è un server di sviluppo distinto dal MCP Hub operativo.

## 6. Contratti e stato

La lista completa dei CID, file, test, stato, blocker e domande aperte è in `docs/CONTRACT_MAP.md`, derivata da `contracts/ledger.json`.

I contratti aperti che influenzano la progettazione sono almeno:

- proprietà dello stato C# ↔ Python (**C-204**);
- eventuale FSM rispetto a Planner/Orchestrator (**C-304**);
- fuzzing e procedura di rilascio verificata (**C-402**, **C-404**);
- contratti nativi/ASan se il progetto acquisirà davvero codice C/C++ (**C-003–C-005**);
- eventuale hook memoria/DLL, che richiede una decisione di confine (**C-105**).

## 7. Procedura di ricerca rapida per un agente

1. Aprire `docs/SOURCE_OF_TRUTH.md`.
2. Cercare il CID in `docs/CONTRACT_MAP.md`.
3. Aprire file obiettivo e test associato.
4. Consultare `docs/SYSTEM_MAP.md` per il confine e il flusso dati.
5. Cercare il task in `docs/agents/EXECUTION_QUEUE.md`.
6. Modificare solo i file assegnati; produrre handoff strutturato.
7. Eseguire i test del contratto, aggiornare ledger e documentazione solo dopo l’evidenza.

Comandi utili:

```bash
rg -n "C-204|C-304|NomeTipo|NomeMetodo" docs contracts src nosai tests
rg -n "public (sealed )?(class|record|interface)|public .*\(" src nosai
python -m compileall -q nosai
python -m pytest -q tests
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
```

## 8. Definizione di “funzionante”

La presenza di file non equivale a funzionalità verificata. Un modulo è pronto soltanto quando:

1. il contratto è mappato;
2. il codice reale compila;
3. esistono test positivi e negativi;
4. la comunicazione con il modulo precedente/successivo è cablata;
5. l’errore è fail-closed e conserva `UNKNOWN`;
6. il risultato è registrato nel ledger;
7. l’evidenza d’ambiente reale è presente quando richiesta dalla roadmap.
