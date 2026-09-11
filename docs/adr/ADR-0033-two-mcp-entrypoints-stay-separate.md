# ADR-0033 — Due entrypoint MCP restano separati

**Status:** Accepted
**Date:** 2026-09-11
**Contratto:** R-206

## Fatti verificati

1. `scripts/mcp_server.py` (702 righe) espone i tool della catena di
   produzione a 5 fasi descritta in `.claude/CLAUDE.md` sezione 7:
   `local_generate_skeleton`, `cloud_infill_implementation`,
   `preflight_contract_check`, `update_contract_state`,
   `deep_reasoner_solve_crash`, `local_update_documentation`.
2. `scripts/mcp_hub_server.py` (15 righe) e' un entrypoint sottile che
   importa e lancia `nosai.mcp.server.run()`, il quale espone i tool del
   Hub/Director/Auditor/Chief: `mcp_infer`, `mcp_propose_change`,
   `mcp_audit_change`, `mcp_chief_health`, `mcp_role_catalog`,
   `mcp_run_simulation` e simili — la governance a runtime dei provider AI
   (routing, policy, audit, promozione dei binding).
3. I due entrypoint non duplicano funzionalita': uno serve la produzione di
   codice guidata da contratto, l'altro la governance a runtime dei
   modelli/provider. Non condividono stato mutabile.
4. Nessun test o codice attuale presuppone che i due processi siano lo
   stesso entrypoint.

## Decisione

I due entrypoint MCP non si fondono in un unico processo. Restano due
server MCP separati, ciascuno con la propria superficie di responsabilita'
documentata:

- `mcp_server.py` per la catena di produzione a 5 fasi.
- `mcp_hub_server.py` (via `nosai.mcp.server`) per il Hub di governance
  runtime dei provider.

**R-206 si chiude come compatibilita' documentata, non come fusione.**

## Conseguenze

- Nessuna modifica al codice: la separazione gia' esistente viene solo
  formalizzata.
- Chi avvia il progetto deve avviare entrambi i processi se servono
  entrambe le capacita'.
- Se in futuro emergesse una duplicazione reale di funzionalita' fra i due,
  questo ADR si riapre con quell'evidenza puntuale.
