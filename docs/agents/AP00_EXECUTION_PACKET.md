# AP-00 Execution Packet — Stabilizzazione Base

> Stato: READY FOR LOCAL EXECUTION
> Fase: AP-00
> Agenti: A1 Claude · A2 Cursor · A3 Claude · A4 Cursor · A5 Claude · A6 Claude/Cursor
> Regola: nessun agente dichiara `Verified` senza evidenza reale di build/test.

## 1. Ordine operativo

1. Ogni agente legge `docs/agents/AGENT_START_HERE.md`.
2. Legge `CLAUDE.md`, `.cursorrules`, `docs/SOURCE_OF_TRUTH.md`.
3. Legge `docs/agents/AGENT_COMMAND_REGISTRY.md`, `FILE_OWNERSHIP_MATRIX.md`, `REPOSITORY_AUDIT_CHECKLIST.md`.
4. Legge solo il command file AP-00 assegnato.
5. Registra `START_HEAD` dal branch locale aggiornato con `origin/main`.
6. Esegue audit prima di modificare codice.
7. Modifica esclusivamente i file nella propria ownership.
8. Esegue test/build mirati.
9. Controlla `git status`, `git diff --stat` e `git diff --name-status`.
10. Produce `AGENT_SESSION_CHECKPOINT.md` secondo lo schema canonico.
11. A6 integra solo dopo la consegna completa di A1-A5.

## 2. Obiettivo AP-00

Stabilizzare la base esistente senza riscrivere il prodotto. L'output deve essere una fotografia verificabile e, dove necessario, correzioni minime che rendano coerenti:

- solution/project graph;
- Runtime e suoi entry point;
- Gate1/Test Center;
- Gate3 decision path;
- ControlPanel e osservabilità cognitiva read-only;
- test projects e CI;
- documentazione rispetto al codice reale;
- confini di sicurezza e third-party provenance.

## 3. Vincoli assoluti

- Non cancellare file esistenti.
- Non sostituire alberi Git.
- Non usare `git reset --hard`, `git clean -fd`, force push o equivalenti distruttivi come soluzione a un conflitto.
- Non modificare `third_party/` salvo richiesta esplicita di A6 per provenienza/licenza.
- Non introdurre server/admin/GM state o API privilegiate.
- Non dare al cognitive dashboard alcuna execution authority.
- Non introdurre chain-of-thought privata: solo trace tecnico strutturato ed evidence.
- Nessun TODO/FIXME/pseudocode/stub/placeholder nei file consegnati.
- Se una correzione richiede un file fuori ownership, STOP e passa il problema ad A6.

## 4. A1 — Claude — Runtime contracts/domain audit

READ:
- `src/NosAi.Core/`
- `src/NosAi.Runtime/Contracts/`
- `docs/ROADMAP_ESECUTIVA.md`
- `docs/UNPRIVILEGED_DEMO_SPEC.md`

WRITE:
- solo file di contract/domain esplicitamente necessari e già assegnati dalla matrice ownership.

TASK:
- individuare contratti duplicati o incompatibili;
- verificare nomi, namespace, nullable e invarianti;
- non rifattorizzare indiscriminatamente il Runtime;
- produrre elenco di incompatibilità con path e simbolo esatto.

EXIT:
- zero modifiche se l'audit non trova problemi;
- oppure modifiche complete + test mirati.

## 5. A2 — Cursor — Runtime integration/perception audit

READ:
- `src/NosAi.Runtime/`
- `src/NosAi.Adapter/`
- `src/NosAi.Protocol/`
- `tests/NosAi.Runtime.Tests/`

WRITE:
- esclusivamente adapter/perception/runtime-integration assegnati dalla ownership matrix.

TASK:
- verificare entry point e wiring reali;
- verificare lifecycle/disposal/cancellation;
- verificare che le osservazioni siano realmente client-visible/unprivileged;
- individuare riferimenti a tipi/file inesistenti.

## 6. A3 — Claude — Gate/algorithms audit

READ:
- `src/NosAi.Runtime/Gate3/`
- `src/NosAi.Core/`
- test Gate3/runtime correlati;
- `docs/GATE3_PIPELINE.md`.

WRITE:
- solo planning/algorithm files assegnati.

TASK:
- verificare catena `Observe → Plan → Simulation → Ranking → Guard → Safety → Execute → Verify`;
- controllare che simulation/ranking non abbiano side effects;
- verificare invarianti di guard/safety;
- segnalare ogni punto in cui cognition/planning potrebbe bypassare Guard/Safety.

## 7. A4 — Cursor — Dashboard/runtime wiring audit

READ:
- `src/NosAi.ControlPanel/`
- runtime endpoint/snapshot contracts;
- cognitive observability contracts/registry;
- Gate1 Test Center.

WRITE:
- esclusivamente UI/runtime wiring assegnato.

TASK:
- verificare che dashboard e runtime condividano realmente il canale dati previsto;
- verificare polling, cancellation e gestione errori;
- verificare read-only boundary;
- verificare che i dati visualizzati siano reali e non placeholder.

## 8. A5 — Claude — Validation/docs audit

READ:
- `tests/`
- `.github/workflows/`
- `docs/agents/`
- `docs/ROADMAP_ESECUTIVA.md`.

WRITE:
- test/docs assegnati dalla matrice ownership.

TASK:
- definire la matrice evidence AP-00;
- controllare test duplicati o non collegati ai progetti;
- documentare comandi esatti di build/test;
- non dichiarare PASS se il comando non è stato eseguito.

## 9. A6 — Integration Gate

A6 NON parte prima delle cinque consegne.

CHECK:
- stesso `START_HEAD` o rebase/merge dichiarato;
- ownership rispettata;
- nessuna cancellazione inattesa;
- project references coerenti;
- namespace coerenti;
- build dei progetti interessati PASS;
- test interessati PASS;
- sicurezza invariata;
- third_party invariato/provenienza preservata;
- documentazione allineata al codice reale.

A6 assegna:
- `Present` se esiste ma non è verificato;
- `Implemented` se implementato e localmente coerente;
- `Integrated` se integrato nel grafo;
- `Done` se criteri di fase soddisfatti;
- `Verified` solo con evidenza riproducibile.

## 10. Handoff minimo obbligatorio

Ogni agente deve consegnare:

```text
TASK_ID:
AGENT:
PHASE: AP-00
START_HEAD:
END_HEAD:
STATUS: Present | Implemented | Integrated | Done | Verified
FILES_CREATED:
FILES_MODIFIED:
FILES_NOT_MODIFIED:
TEST_COMMANDS:
TEST_RESULTS:
BUILD_COMMANDS:
BUILD_RESULTS:
DIFF_REVIEW:
DELETIONS_DETECTED: none | list
OWNERSHIP_CONFLICTS: none | list
SECURITY_CHECK:
THIRD_PARTY_CHECK:
BLOCKERS:
NEXT_AGENT_ACTION:
```

## 11. Criterio di uscita AP-00

AP-00 è chiuso solo quando A6 dispone di evidenza sufficiente per descrivere con precisione lo stato reale del repository e non rimangono blocker critici sconosciuti nella base Runtime/Gate/Dashboard/Test. In caso contrario AP-00 resta aperto e le feature successive non devono mascherare i problemi della base.
