# NosAiProject — Ordine del repository

**Aggiornato:** 2026-09-10  
**Stato:** operativo

## Fonti canoniche

1. `docs/SOURCE_OF_TRUTH.md` — precedenza documentale.
2. `docs/ROADMAP_ESECUTIVA.md` — ordine delle fasi AP-00…AP-10.
3. `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md` — capacità e confini.
4. `docs/NOSAI_ARCHITECTURE_BASELINE.md` — invarianti architetturali.
5. `docs/adr/` — decisioni numerate.
6. `AGENTS.md` e `.claude/CLAUDE.md` — ruoli e regole degli agenti.
7. `docs/REPOSITORY_INVENTORY_2026-09-10.json` — inventario generato dell’albero.

## Ownership delle directory

| Directory | Uso | Regola |
|---|---|---|
| `src/` | runtime C# di produzione | modifiche solo con test .NET e ADR se cambia un confine |
| `nosai/` | runtime/tooling Python | moduli focalizzati; niente duplicati di autorità C# |
| `tests/` | test Python e C# | ogni comportamento nuovo ha test positivo e negativo |
| `schemas/` | schemi JSON condivisi | versionare breaking change; non modificare per aggirare un test |
| `contracts/` | ledger e contratti di fase | stato aggiornato solo da controlli verificabili |
| `scripts/` | build, test, bootstrap e CLI | script riproducibili, senza segreti |
| `docs/` | specifiche, ADR, ricerca e report | una sola fonte canonica; gli indici rimandano, non duplicano |
| `third_party/` | sorgenti esterne e provenance | non è una cartella di codice runtime |
| `tools/` | strumenti di sviluppo e diagnostica | mantenere README, licenza e comando di verifica |
| `data/` | evidenze e configurazioni pubblicabili | dati locali/segreti restano esclusi da Git |

## Archiviazione e rimozione

- I documenti storici delle fasi completate restano in `docs/agents/phases/ARCHIVE/`.
- Un file si elimina solo se non è referenziato da codice, test, CI o documentazione canonica e se non contiene evidenza unica.
- Un documento superato ma utile viene spostato in archivio con una nota di sostituzione.
- Codice duplicato non viene compattato automaticamente: prima si identifica l’autorità, poi si aggiunge un test di non regressione e infine si rimuove la copia.
- Nessuna cancellazione riguarda `third_party/`, catture, calibrazioni o log di evidenza senza decisione esplicita.

## Procedura per gli agenti

1. Consultare questo file e `docs/INDICE_REPO.md`.
2. Cercare il contratto e il test prima del codice.
3. Modificare il file canonico, non una copia di riepilogo.
4. Aggiornare indice, changelog e stato soltanto dopo la verifica.
5. Registrare nel worklog file toccati, motivo, test e rischi residui.


