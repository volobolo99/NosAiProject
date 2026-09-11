# NosAiProject — Lavoro residuo e criteri di completamento

**Scopo:** elenco operativo delle attività ancora necessarie. Non usare la percentuale globale come prova di funzionamento: ogni voce richiede l’evidenza indicata.

## Priorità P0 — sbloccare la verifica

| ID | Attività | Dipendenze | Evidenza di chiusura |
|---|---|---|---|
| R-001 | ~~Rendere verdi le GitHub Actions e acquisire i log dei job falliti~~ — **chiuso 2026-09-11** | accesso ai log/runner | `NosAi CI` e `NosAi .NET (Windows)` verdi su main (commit dac9ec6); i due test DPI falliti erano un runner CI headless senza sessione desktop reale (`InteractiveDesktopOnlyFactAttribute`), non un bug applicativo |
| R-002 | ~~Decidere quale stack possiede lo stato canonico (C# o Python)~~ — **chiuso 2026-09-11** | C-204 | ADR-0030 accettato; C-204 `TEST_VERIFIED` nel ledger (gate 2 → 100%) con entrambi i canali implementati e testati (commit ba31c2e, b3aceb8) |
| R-003 | ~~Verificare l’esecuzione reale del MCP Director → worker → Auditor~~ — **chiuso 2026-09-11** | MCP Hub, policy, provider | Handoff valido e veto/rollback registrati su `AuditLog` reale, replay deterministico di `run_simulation` (tests/test_mcp_hub_director_auditor_e2e.py, 7/7 verdi), più l'anello worker (`tests/test_mcp_hub_worker_ring_e2e.py`, 2 test): `test_documentation_capability_routes_to_the_free_local_provider` verifica il solo instradamento, senza rete; `test_the_worker_ring_runs_a_real_model_after_director_and_auditor_approve` riproduce `mcp_propose_change` → `mcp_audit_change` → `mcp_infer` con lo stesso wiring di `create_server()` isolato su `tmp_path`, chiamando davvero `InferenceGateway.infer()` contro Ollama locale (qwen2.5-coder:7b) e verificando sia il testo di risposta reale sia i tre eventi persistiti su `AuditLog` nell'ordine corretto. Se Ollama non è raggiungibile il test si dichiara saltato (`pytest.mark.skipif`) invece di fallire o passare senza aver verificato nulla — non equivale a `Verified` in ogni ambiente, dipende da Ollama in esecuzione sulla macchina che esegue i test. Verificato in sessione con Ollama attivo: entrambi i test verdi (0.31s), suite Python intera invariata |
| R-004 | ~~Chiudere la specifica di rilascio end-to-end~~ — **chiuso 2026-09-11** | C-404 | `contracts/ledger.json`: C-404 `VERIFIED` (gate 4 → 75%). Python `compileall` pulito, 536/536 test; .NET Release build pulita, 2838/2840 test (i 2 falliti erano la flakiness transitoria da contention di suite, corretta separatamente in questa stessa sessione); 22 suite di certificazione tutte EXIT=0 (commit dcd6991). Con l'operatore al computer, eseguiti anche i due probe reali che mancavano: `--dxgi-probe` (Desktop Duplication su 1920x1200, frame con 4096 colori distinti, pixel live) e `--input-probe` (mouse su 3 punti, errore massimo 0px, cursore ripristinato; tastiera via VK_F24 osservata da un hook a basso livello, mai ricevuta da alcuna applicazione; 70 ms, esito 0). Resta aperto solo C-402 (fuzzing) nello stesso Gate 4 |

## Priorità P1 — colmare i blocchi funzionali

| ID | Attività | Riferimento | Evidenza |
|---|---|---|---|
| R-101 | Fornire asset/back-end ML per OCR, object detection e tracking | AP-02/Q-014 | cattura reale con confidence/provenance e test su replay |
| R-102 | Completare dati reali di skill, danno, costo e cooldown | AP-05 | simulazione e ranking confrontati con catture reali |
| R-103 | Validare combattimento su client reale | Q-038/Q-039 | esecuzione autorizzata, receipt e post-condition su Windows |
| R-104 | Completare canale e verifica equip/unequip/upgrade | AP-07, Q-089 | primitive di azione, verifica prima/dopo e test negativi |
| R-105 | Completare fonti reali per mob/NPC/dialogue/interact/deliver | AP-06 | obiettivi quest grounded e completamento multi-step |
| R-106 | Eseguire la certificazione di autonomia su hardware reale | AP-10 | report con evidenza per ogni stadio, non solo codice presente |

## Priorità P2 — hardening e decisioni architetturali

| ID | Attività | Riferimento | Decisione richiesta |
|---|---|---|---|
| R-201 | Decidere se introdurre codice C/C++ e quindi ASan/ctypes | C-003–C-005 | aprire ramo nativo oppure riscrivere la FASE 5 sugli stack reali |
| R-202 | Decidere se una FSM sostituisce o affianca Planner/Orchestrator | C-304 | un’unica autorità di decisione, ADR e test |
| R-203 | Decidere l’eventuale hook memoria/DLL | C-105 | contratto, limiti e test; nessuna implementazione implicita |
| R-204 | Aggiungere fuzzing ai parser di input | C-402 | corpus, harness, esecuzione riproducibile e report |
| R-205 | Completare la cascata free-first del MCP | `scripts/free_first.py`, docs/mcp | test provider, fallback, quote, timeout e funzionamento reale |
| R-206 | Consolidare o formalizzare i due entrypoint MCP | `scripts/mcp_server.py` vs `scripts/mcp_hub_server.py` | ADR di consolidamento oppure compatibilità documentata e testata |
| R-207 | Generare un catalogo machine-readable di tutte le funzioni pubbliche | `docs/INDICE_REPO.md` oggi è principalmente type/file-level | script di scansione su checkout completo, `FUNCTION_INDEX.json` e verifica link/test |
| R-208 | Eseguire watchdog periodico del MCP Chief in ambiente operativo e collegarlo a metriche provider/latency | MCP Chief, observability | report di health tick e raccomandazioni su replay reali, senza auto-mutazioni non autorizzate |

## Ordine consigliato

```
R-001 → R-002 → R-003
      ↘ R-101/R-102/R-104/R-105 in parallelo su file disgiunti
R-004 → R-106 → rilascio
R-201/R-202/R-203 prima di introdurre nuove autorità o nuovi stack
```

## Regola di avanzamento

Una voce passa a `DONE` solo con:

- contratto/CID collegato;
- file e test reali;
- comando eseguito;
- risultato registrato;
- audit indipendente quando cambia sicurezza o autorità;
- aggiornamento di ledger, queue e documentazione.

Se l’evidenza dipende da un client Windows, provider online, GPU o hardware non disponibile, lo stato resta `BLOCKED` o `PRESENT`, mai `VERIFIED`.

## Collegamenti

- Mappa contratti: `docs/CONTRACT_MAP.md`
- Mappa sistema: `docs/SYSTEM_MAP.md`
- Coordinamento agenti: `docs/AGENT_COORDINATION.md`
- Coda attiva: `docs/agents/EXECUTION_QUEUE.md`
- Ledger: `contracts/ledger.json`

## Chief e Research Lab — progettazione importata

Fonte: docs/mcp/RESEARCH_LAB_SPEC.md; contratto: contracts/mcp-research-lab-001.json.
LAB-01 (evidenze firmate), LAB-02 (stato SQLite con migrazione JSON, revisioni,
idempotenza e lease), il supervisore health e il verifier contratti sono presenti su
`main`. Restano LAB-03..LAB-07: supervisione end-to-end, worker pool, esperimenti,
rilasci a mandato e certificazione del pannello. R-208 è coperto solo a livello di
API: serve ancora il watchdog operativo e la raccolta di metriche reali.
Il laboratorio non è attivo finché prove e integrazione end-to-end non sono completate.

## Indice funzioni e audit verificato

Consultare [FUNCTION_INDEX.md](FUNCTION_INDEX.md) per ricerca per file/simbolo,
source revision e copertura. [DOCUMENTATION_ALIGNMENT_AUDIT.md](DOCUMENTATION_ALIGNMENT_AUDIT.md)
elenca disallineamenti, cinque firme risolte e undici ancora da precisare. R-207 ha
ora un indice statico con diagnostica parser e modalità strict; resta la rigenerazione
a ogni revisione e la verifica del call graph.
