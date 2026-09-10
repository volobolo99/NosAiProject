# MCP Development Contracts

## Contratto di implementazione

Ogni nuovo tool deve dichiarare: input JSON versionato, output JSON versionato, rischio (`safe`, `observation`, `network`, `privileged`, `secret_export`), timeout, side effect, test positivi e negativi.

## Autorità

`McpPolicy` è il gate minimo. `Guard`, `Trust` e `Safety` del runtime restano autorità finale sulle azioni di gioco. Il Direttore MCP può proporre modifiche al candidato, ma non può modificare policy, audit, contratti o test di sicurezza per autoapprovarsi.

## Promozione

`DRAFT → TESTED → AUDITED → SHADOW → PROMOTED`.

Una modifica che fallisce un test deterministico, produce dati non provenienti da fonti osservabili o tenta di accedere a stato privilegiato viene rifiutata e registrata nell’audit.


