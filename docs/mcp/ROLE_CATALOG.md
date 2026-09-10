# NosAiProject — Catalogo dei dipendenti AI

Questo catalogo distingue l’identità professionale dal modello che la esegue. Un modello può essere sostituito senza perdere memoria, responsabilità, permessi o storico del dipendente.

## Fonte e implementazione

- Ruoli di sviluppo: `AGENTS.md`
- Catalogo runtime MCP: `nosai/mcp/roles.py` (`DEFAULT_EMPLOYEE_ROLES`)
- Schema: `schemas/mcp_employee_role.schema.json`
- Verifica: `RoleArchitect.verify_employee_catalog()`
- Ruoli MCP di governance: `DEFAULT_ROLES` (director, auditor, simulator, learning)

## Dipendenti configurati

| ID stabile | Nome | Responsabilità principale | Modello primario | Fallback |
|---|---|---|---|---|
| `employee.orchestrator_cto` | Orchestrator/CTO | architettura, scomposizione, routing e approvazione | Claude | Qwen3 Coder 30B |
| `employee.mcp_chief` | MCP Chief | salute continua dell’Hub, ottimizzazione, proposte, promozione e rollback dei binding | Claude | Qwen3 Coder 30B, DeepSeek V4 Flash |
| `employee.product_manager` | Product Manager | requisiti, roadmap e criteri di accettazione | Qwen2.5 Coder 7B locale | Claude |
| `employee.game_ai_architect` | Game AI Architect | confini cognitivi, ADR e invarianti | Claude | Qwen3 Coder 30B |
| `employee.perception` | Perception Agent | cattura, OCR/CV e classificazione osservazioni | Gemini Flash Lite | Qwen3 Coder 30B, Qwen2.5 locale |
| `employee.world_model` | World Model Agent | fusione sensori, temporalità e identità entità | Qwen3 Coder 30B | DeepSeek V4 Flash |
| `employee.planning` | Planning Agent | HTN/GOAP, navigazione e replanning | Qwen3 Coder 30B | DeepSeek V4 Flash |
| `employee.decision` | Decision Agent | ranking di azioni, utilità, rischio e confidence | Qwen3 Coder 30B | DeepSeek V4 Flash |
| `employee.action` | Action Agent | dispatch autorizzato, receipt e post-condition | DeepSeek V4 Flash | Qwen3 Coder 30B |
| `employee.memory` | Memory Agent | memoria episodica, semantica, procedurale e outcome ledger | DeepSeek V4 Flash | Qwen3 Coder 30B |
| `employee.coding` | Coding Agent | implementazione nei file assegnati e unit test | DeepSeek V4 Flash | Qwen3 Coder 30B |
| `employee.testing` | Testing Agent | test, benchmark e classificazione dei fallimenti | DeepSeek V4 Flash | Qwen3 Coder 30B |
| `employee.security` | Security Agent | minacce, segreti, permessi e revisioni sensibili | Claude | DeepSeek V4 Flash |
| `employee.reviewer` | Reviewer Agent | revisione indipendente prima dell’integrazione | Claude | DeepSeek V4 Flash |
| `employee.documentation` | Documentation Agent | documenti canonici, changelog, mappe e riepiloghi | Qwen2.5 Coder 7B locale | DeepSeek V4 Flash |

## Cosa è stabile e cosa è sostituibile

**Stabile:** `employee_id`, scopo, responsabilità, capacità, divieti, necessità di approvazione, memoria e audit.

**Sostituibile:** `primary_model`, `fallback_models`, provider, temperatura, limiti di token e strumenti disponibili, ma solo dopo qualificazione della nuova configurazione.

Cambiare modello, provider o configurazione richiede ripetere i test del ruolo interessato. Il nome commerciale del modello non è una qualifica: la qualifica appartiene alla coppia modello + configurazione + ruolo + versione del test.

## Autorità

- Il dipendente non può auto-aumentare permessi o rimuovere divieti.
- Il modello non può diventare autorità di Safety, policy o audit solo perché è più capace.
- Il Direttore assegna il lavoro; il **MCP Chief** sorveglia l’Hub e propone ottimizzazioni; l’Auditor indipendente può porre veto e imporre rollback.
- Le attività di gioco restano subordinate a Guard, Trust e Safety del runtime locale.
- La sostituzione automatica del modello richiede shadow mode, test, periodo di prova e rollback, come descritto in `docs/AGENT_COORDINATION.md`.

## Collegamenti rapidi

- Mappa sistema: `docs/SYSTEM_MAP.md`
- Mappa contratti: `docs/CONTRACT_MAP.md`
- Lavoro residuo: `docs/REMAINING_WORK.md`
- Regole operative: `.claude/CLAUDE.md`
