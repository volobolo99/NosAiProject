# MCP Agent Quick Index

Usare questo indice prima di modificare il sistema: riduce contesto, token e regressioni.

| Necessità | File da leggere | Contratto/test |
|---|---|---|
| policy e confini | `nosai/mcp/policy.py`, `contracts/mcp-hub-001.json` | `tests/test_mcp_hub_policy.py` |
| stato/attivazione rete | `nosai/mcp/contracts.py`, `nosai/mcp/server.py` | `schemas/mcp_activation.schema.json` |
| routing modelli | `nosai/mcp/router.py`, `config/mcp.default.json` | `schemas/mcp_status.schema.json` |
| simulazione | `nosai/mcp/simulation.py` | `schemas/mcp_simulation_request.schema.json` |
| apprendimento offline | `nosai/mcp/learning.py` | `schemas/mcp_learning_candidate.schema.json`, `tests/test_mcp_hub_learning.py` |
| chiavi API | `nosai/mcp/secrets.py`, `nosai/mcp/dashboard.py` | nessun plaintext; test cifratura |
| audit | `nosai/mcp/audit.py` | payload sempre redatto |
| pannello | `nosai/mcp/static/index.html`, `scripts/mcp_dashboard_server.py` | API `/api/mcp/*` |

## Regole per i modelli programmatori

1. Leggere prima il contratto e il test del componente.
2. Non cambiare `McpPolicy` per aggirare un rifiuto.
3. Non aggiungere endpoint che restituiscono segreti, token o stato privilegiato.
4. Ogni modifica deve avere test negativo e test di determinismo quando applicabile.
5. Aggiornare il contratto solo con una nuova versione e una nota di migrazione.


