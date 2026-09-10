# MCP Development Contracts

## Contratto di implementazione

Ogni nuovo tool deve dichiarare: input JSON versionato, output JSON versionato, rischio (`safe`, `observation`, `network`, `privileged`, `secret_export`), timeout, side effect, test positivi e negativi.

## Autorità

`McpPolicy` è il gate minimo. `Guard`, `Trust` e `Safety` del runtime restano autorità finale sulle azioni di gioco. Il Direttore MCP può proporre modifiche al candidato, ma non può modificare policy, audit, contratti o test di sicurezza per autoapprovarsi.

## Promozione

`DRAFT → TESTED → AUDITED → SHADOW → PROMOTED`.

Una modifica che fallisce un test deterministico, produce dati non provenienti da fonti osservabili o tenta di accedere a stato privilegiato viene rifiutata e registrata nell’audit.



## MCP Chief

Il Chief è un supervisore di governance: health check, raccomandazioni, proposte e gestione staged dei binding. Non possiede autorità di esecuzione, policy override, audit override, secret export o stato privilegiato. Ogni promozione richiede controlli indipendenti (`tests_passed`, `shadow_passed`, `audit_approved`) e conferma esplicita `operator`; il registro è persistito atomicamente e supporta rollback versionato.

Contratto operativo: `contracts/mcp-hub-001.json` v1.1.0. Runbook: `docs/mcp/CHIEF_RUNBOOK.md`.
