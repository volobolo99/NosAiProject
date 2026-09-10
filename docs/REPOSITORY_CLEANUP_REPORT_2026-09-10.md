# Repository cleanup report — 2026-09-10

## Scope

È stato analizzato il tree completo del ramo `main` di `volobolo99/NosAiProject`: 1.472 file iniziali, directory di produzione C# e Python, test, contratti, schemi, documentazione, strumenti e vault third-party.

## Controlli eseguiti

- conteggio e classificazione di ogni directory di primo livello;
- verifica degli entrypoint (`NosAi.sln`, package Python, MCP Hub, dashboard);
- ricerca repository di `TODO`, `FIXME`, `TBD`, `XXX`, `NotImplementedError`;
- controllo dei riferimenti dei documenti front-door e delle fonti canoniche;
- verifica dello stato recente dei file chiave tramite cronologia Git;
- controllo delle directory archivio e dei materiali third-party;
- aggiornamento dei conteggi nell’inventario e degli indici di routing.

## Decisioni

### File mantenuti

- `docs/MASTER_ROADMAP.md` resta generato da `contracts/ledger.json` e non viene accorpato.
- `docs/agents/phases/ARCHIVE/` resta storico referenziato dai report di fase.
- `third_party/` resta separato dal runtime per provenance e licenze.
- `scripts/free_first.py` resta perché è referenziato da test/contratti e rappresenta una capacità in completamento, non un file orfano.
- `tools/deepseek-mcp/` resta perché è un server MCP di sviluppo distinto dal nuovo MCP Hub operativo.

### File non eliminati

Non è stato eliminato alcun file: nessun candidato è risultato contemporaneamente non referenziato, privo di evidenza unica e privo di uso in build/CI/runtime. La rimozione in questi casi avrebbe distrutto storia o capacità di diagnosi.

### Correzioni applicate

- aggiunto `docs/REPOSITORY_ORDER.md` con ownership, archiviazione e regole di rimozione;
- aggiunto `docs/REPOSITORY_INVENTORY_2026-09-10.json` come fotografia machine-readable;
- aggiornati `ARCHITECTURE.md`, `TEST_PLAN.md`, `docs/INDICE_REPO.md` e `third_party/README.md`;
- marcato `docs/PIANO_DI_RIORDINO.md` come piano storico;
- registrato l’MCP Hub nella Source of Truth e nel changelog.

## Prossima revisione

Ripetere la stessa procedura il 2026-10-10 oppure dopo una riorganizzazione di namespace, progetti .NET o directory `docs/agents/phases/`.


