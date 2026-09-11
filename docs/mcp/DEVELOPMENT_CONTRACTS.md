# MCP Development Contracts

## Contratto di implementazione

Ogni nuovo tool deve dichiarare: input JSON versionato, output JSON versionato, rischio (`safe`, `observation`, `network`, `privileged`, `secret_export`), timeout, side effect, test positivi e negativi.

## Autorità

`McpPolicy` è il gate minimo. `Guard`, `Trust` e `Safety` del runtime restano autorità finale sulle azioni di gioco. Il Direttore MCP può proporre modifiche al candidato, ma non può modificare policy, audit, contratti o test di sicurezza per autoapprovarsi.

## Enforcement dei ruoli a runtime

`nosai/mcp/enforcement.py` espone `require_capability(employee_id, capability, *, employees=DEFAULT_EMPLOYEE_ROLES)`, chiamata come prima istruzione di `InferenceGateway.infer()` (`nosai/mcp/inference.py`). Solleva `RoleEnforcementError` se la capability richiesta è in `employee.forbidden` o non è in `employee.capabilities`. Prima di questa funzione, `capabilities`/`forbidden` erano letti solo da `RoleArchitect.verify_employee_catalog()` come controllo statico del catalogo, mai applicati a runtime: `mcp_infer` è ora fail-closed per ogni chiamata che dichiara un ruolo. Limite noto e deliberato: se `employee_id` o `capability` sono vuoti, la funzione non fa nulla — le chiamate che non dichiarano un ruolo restano permissive. Test: `tests/test_mcp_enforcement.py`, `tests/test_mcp_hub_inference.py`.

## Promozione

`DRAFT → TESTED → AUDITED → SHADOW → PROMOTED`.

Una modifica che fallisce un test deterministico, produce dati non provenienti da fonti osservabili o tenta di accedere a stato privilegiato viene rifiutata e registrata nell’audit.

## Evidenze di promozione

Il tool `mcp_record_evidence` registra soltanto digest e stato, firma il record con la chiave locale dell'autorità e assegna una scadenza. `mcp_role_promote_binding` riceve `evidence_ids` con le chiavi `tests`, `shadow` e `audit`; ogni record deve avere firma valida, digest candidato uguale, risultato `pass|ok|verified`, essere fresco e provenire da un esecutore distinto dall'autore candidato e dagli altri due esecutori. Il vecchio formato di booleani (`tests_passed`, `shadow_passed`, `audit_approved`) è rifiutato. I record non contengono chiavi API, token o credenziali.



## MCP Chief

Il Chief è un supervisore di governance: health check, raccomandazioni, proposte e gestione staged dei binding. Non possiede autorità di esecuzione, policy override, audit override, secret export o stato privilegiato. Ogni promozione richiede gli ID di tre evidenze firmate (`tests`, `shadow`, `audit`) e conferma esplicita `operator`; i booleani storici sono rifiutati. Il registro usa `McpStateStore` SQLite condiviso, revisioni monotone, idempotenza e rollback versionato; l'export JSON non è la sorgente di concorrenza.

Contratto operativo: `contracts/mcp-hub-001.json` v1.1.0. Runbook: `docs/mcp/CHIEF_RUNBOOK.md`.
