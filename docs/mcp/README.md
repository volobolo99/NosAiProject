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
- `nosai/mcp/simulation.py` — simulazioni pure e riproducibili.
- `nosai/mcp/learning.py` — candidate → validate → offline skill.
- `nosai/mcp/secrets.py` — cifratura locale Fernet; nessun ritorno plaintext.
- `nosai/mcp/audit.py` — JSONL con redazione automatica.
- `nosai/mcp/server.py` — tool/resource MCP.
- `nosai/mcp/dashboard.py` — API e pannello locale dedicato.

## Flusso online→offline

1. Il pannello abilita MCP Rete.
2. Il router sceglie solo provider qualificati.
3. Risultati e dati osservati entrano come `candidate` con provenienza.
4. La simulazione/replay e l’Auditor verificano il candidato.
5. Solo candidati con evidenza osservabile diventano skill offline.
6. In offline NosAi usa le skill validate senza chiamate di rete.

Le credenziali sono gestite dal pannello ma non vengono mai mostrate integralmente, passate ai modelli o scritte nei log. Un eventuale dato sensibile intercettato viene redatto e non può essere usato come autorizzazione.

## Avvio locale

```powershell
python scripts/mcp_hub_server.py
python scripts/mcp_dashboard_server.py
```

Il pannello ascolta su `127.0.0.1:8770`. L’MCP usa stdio tramite `.mcp.json`.


