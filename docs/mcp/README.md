# NosAi MCP Hub

Versione del contratto: `mcp-hub-001@1.0.0`  
Entry point: `scripts/mcp_hub_server.py`  
Pannello dedicato: `scripts/mcp_dashboard_server.py`

## Scopo

Il MCP Hub fornisce strumenti di supporto al motore locale: catalogo e routing dei modelli, simulazione tattica deterministica, raccolta di evidenze, apprendimento online→offline, audit e gestione cifrata delle chiavi. Non sostituisce `Guard`, `Trust` o `Safety` e non possiede autorità diretta di esecuzione.

## Modalità operative

| Modalità | Default | Rete | Uso |
|---|---:|---:|---|
| `offline` | sì | no | lavoro locale, replay, simulazione, riuso delle skill |
| `network` | no | sì | provider online attivati dall’operatore, ricerca e confronto |

L’attivazione richiede una richiesta con `confirmation: "operator"`. La configurazione JSON non può accendere la rete da sola.

## Componenti

- `nosai/mcp/contracts.py` — contratti tipizzati e versionabili.
- `nosai/mcp/policy.py` — invarianti immutabili e autorizzazione strumenti.
- `nosai/mcp/router.py` — routing local/free-first con capability e policy.
- `nosai/mcp/inference.py` — gateway di inferenza compatibile Ollama/OpenAI; chiamate remote solo in modalità rete.
- `nosai/mcp/simulation.py` — simulazioni pure e riproducibili.
- `nosai/mcp/learning.py` — candidate → validate → offline skill.
- `nosai/mcp/secrets.py` — cifratura locale Fernet; nessun ritorno plaintext.
- `nosai/mcp/audit.py` — JSONL con redazione automatica.
- `nosai/mcp/server.py` — tool/resource MCP.
- `nosai/mcp/director.py` + `nosai/mcp/auditor.py` — proposta, veto e rollback esterno.
- `nosai/mcp/roles.py` — ruoli MCP, catalogo dipendenti e verifica di copertura (`DEFAULT_EMPLOYEE_ROLES`).
- `docs/mcp/ROLE_CATALOG.md` + `schemas/mcp_employee_role.schema.json` — identità, specializzazioni, modelli primari/fallback e divieti.
- `nosai/mcp/dashboard.py` — API e pannello locale dedicato.

## Binding dinamico dei dipendenti

Il pannello espone il catalogo e il ciclo controllato:

- `GET /api/mcp/roles` — ruoli e specializzazioni;
- `GET /api/mcp/role-bindings` — binding attivi versionati;
- `GET /api/mcp/role-proposals` — proposte in shadow;
- `POST /api/mcp/role-bindings/propose` — crea una proposta senza cambiare il runtime;
- `POST /api/mcp/role-bindings/promote` — promuove solo con conferma dell’operatore e `tests_passed`, `shadow_passed`, `audit_approved`;
- `POST /api/mcp/role-bindings/rollback` — ripristina il binding precedente.

Le scritture sono atomiche su `data/mcp/role_bindings.json`. Il router usa il binding promosso quando l’inferenza specifica `role_id`; se nessun modello qualificato del binding è disponibile, fallisce in modo esplicito invece di scegliere silenziosamente un modello diverso.

## Flusso online→offline

1. Il pannello abilita MCP Rete.
2. Il router sceglie solo provider qualificati.
3. Risultati e dati osservati entrano come `candidate` con provenienza.
4. La simulazione/replay e l’Auditor verificano il candidato.
5. Solo candidati con evidenza osservabile diventano skill offline.
6. In offline NosAi usa le skill validate senza chiamate di rete.

Le credenziali sono gestite dal pannello ma non vengono mai mostrate integralmente, passate ai modelli o scritte nei log. Un eventuale dato sensibile intercettato viene redatto e non può essere usato come autorizzazione.

Il Direttore MCP può proporre modifiche soltanto in aree non protette. L’Auditor controlla test, compatibilità dei contratti e invarianti Safety; un controllo mancante produce veto e rollback richiesto.

## Relazione con l’orchestratore storico

`scripts/mcp_hub_server.py` è l’entrypoint canonico del MCP Hub operativo descritto da
`contracts/mcp-hub-001.json`. `scripts/mcp_server.py` resta il server storico della
catena skeleton/infill/preflight/debug. I due entrypoint non sono intercambiabili: un
task deve indicare quale contratto usa. La cascata free-first del server storico è
ancora tracciata da `contracts/free-first-006.json` e `contracts/mcp-cascata-007.json`
e non va considerata completa finché `scripts/free_first.py` non supera i test dedicati.

## Avvio locale

```powershell
python -m pip install -e ".[mcp]"
python scripts/mcp_hub_server.py
python scripts/mcp_dashboard_server.py
```

Il pannello ascolta su `127.0.0.1:8770`. L’MCP usa stdio tramite `.mcp.json`.

