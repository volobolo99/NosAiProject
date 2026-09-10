# MCP Agent Quick Index

Per programmare Chief e Research Lab iniziare da [AI_PROGRAMMING_GUIDE.md](AI_PROGRAMMING_GUIDE.md).

Usare questo indice prima di modificare il sistema: riduce contesto, token e regressioni.

| Necessità | File da leggere | Contratto/test |
|---|---|---|
| policy e confini | `nosai/mcp/policy.py`, `contracts/mcp-hub-001.json` | `tests/test_mcp_hub_policy.py` |
| stato/attivazione rete | `nosai/mcp/contracts.py`, `nosai/mcp/server.py` | `schemas/mcp_activation.schema.json` |
| routing modelli | `nosai/mcp/router.py`, `config/mcp.default.json` | `schemas/mcp_status.schema.json` |
| inferenza | `nosai/mcp/inference.py` | `mcp_infer`; chiavi solo tramite `SecretStore` |
| simulazione | `nosai/mcp/simulation.py` | `schemas/mcp_simulation_request.schema.json` |
| apprendimento offline | `nosai/mcp/learning.py` | `schemas/mcp_learning_candidate.schema.json`, `tests/test_mcp_hub_learning.py` |
| chiavi API | `nosai/mcp/secrets.py`, `nosai/mcp/dashboard.py` | nessun plaintext; test cifratura |
| audit | `nosai/mcp/audit.py` | payload sempre redatto |
| Direttore/Auditor | `nosai/mcp/director.py`, `nosai/mcp/auditor.py` | `tests/test_mcp_hub_governance.py` |
| MCP Chief/watchdog | `nosai/mcp/chief.py`, `nosai/mcp/bindings.py`, `docs/mcp/CHIEF_RUNBOOK.md` | `tests/test_mcp_chief.py`, `mcp_chief_health`, `/api/mcp/chief` |
| ruoli e dipendenti | `nosai/mcp/roles.py`, `docs/mcp/ROLE_CATALOG.md` | `RoleArchitect.verify()`, `verify_employee_catalog()` |
| pannello | `nosai/mcp/static/index.html`, `scripts/mcp_dashboard_server.py` | API `/api/mcp/*`, ruoli `/api/mcp/roles` e verifica `/api/mcp/roles/verify` |

## Regole per i modelli programmatori

1. Leggere prima il contratto e il test del componente.
2. Non cambiare `McpPolicy` per aggirare un rifiuto.
3. Non aggiungere endpoint che restituiscono segreti, token o stato privilegiato.
4. Ogni modifica deve avere test negativo e test di determinismo quando applicabile.
5. Aggiornare il contratto solo con una nuova versione e una nota di migrazione.


## Ricerca ed evoluzione

Leggere RESEARCH_LAB_SPEC.md e contracts/mcp-research-lab-001.json.
Usare il singolo pacchetto LAB interessato: contiene dipendenze, file e prove richieste.
LAB-01 evidenze; LAB-02 stato; LAB-03 supervisore; LAB-04 ruoli/worker;
LAB-05 esperimenti; LAB-06 rilasci; LAB-07 pannello e certificazione.
