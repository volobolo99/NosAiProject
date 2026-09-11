# Mappa agenti Claude Code

## Scopo

Collega ogni agent-type Claude Code (`.claude/agents/*.md`) all'`employee_id` stabile definito in `nosai/mcp/roles.py`, e dichiara quali ruoli non hanno un agent-type dedicato e perché.

## Origine dei dati

`scripts/generate_claude_agents.py` legge `nosai/mcp/roles.py::DEFAULT_EMPLOYEE_ROLES` (fonte dei fatti: purpose, capabilities, forbidden) e la propria mappa interna `AGENT_SPECS` (slug, model, tools, tipo A/B — decisione di governance, non deducibile automaticamente dal ruolo). Rilanciarlo rigenera solo i 4 file marcati "generato" sotto; `revisore.md` e `verificatore.md` restano scritti a mano.

## Tabella degli agenti

| File | employee_id | Tipo | Origine |
|---|---|---|---|
| `.claude/agents/security-agent.md` | `employee.security` | A (subagente pieno) | generato da `scripts/generate_claude_agents.py` |
| `.claude/agents/mcp-chief.md` | `employee.mcp_chief` | A (esclude promote/rollback binding dal frontmatter `tools`) | generato da `scripts/generate_claude_agents.py` |
| `.claude/agents/coding-agent.md` | `employee.coding` | B (guscio sottile, delega a `cloud_infill_implementation`, mai Write/Edit) | generato da `scripts/generate_claude_agents.py` |
| `.claude/agents/documentation-agent.md` | `employee.documentation` | B (guscio sottile, delega a `doc_agent.py`/`local_update_documentation`, mai Write/Edit) | generato da `scripts/generate_claude_agents.py` |
| `.claude/agents/revisore.md` | `employee.reviewer` | A (già esistente, aggiornato con riferimento esplicito al ruolo) | scritto a mano, non rigenerato dallo script |
| `.claude/agents/verificatore.md` | `employee.testing` | A (già esistente, aggiornato con riferimento al ruolo e comando `pytest`) | scritto a mano, non rigenerato dallo script |
| `.claude/agents/esploratore.md` | nessuno | agente di supporto alla ricerca nel repository, non collegato a un employee_id del catalogo runtime | scritto a mano |

## Ruoli senza agent-type

`employee.orchestrator_cto`, `employee.product_architect`, `employee.perception`, `employee.world_model`, `employee.planning`, `employee.decision`, `employee.action`, `employee.memory`, `employee.model_scout`.

- **Orchestrator/CTO e Product & Architecture Lead**: operano nella sessione principale per decisione diretta, non come subagenti delegati.
- **Perception, World Model, Planning, Decision, Action, Memory, Model Scout**: sono invocati a runtime da `mcp_infer(role_id=...)` dal loop di gioco, non dal Task tool di Claude Code — creare un subagente per loro confonderebbe due concetti di agente diversi.
