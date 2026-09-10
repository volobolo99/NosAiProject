# Repository cleanup report — 2026-09-10

## Scope

È stato analizzato il tree completo del ramo `main` di `volobolo99/NosAiProject`: 1.472 file iniziali (1.477 nel tree corrente dopo l’aggiornamento documentale), directory di produzione C# e Python, test, contratti, schemi, documentazione, strumenti e vault third-party.

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
- aggiornati `ARCHITECTURE.md`, `TEST_PLAN.md`, `docs/INDICE_REPO.md`, `docs/SOURCE_OF_TRUTH.md`, `docs/MASTER_ROADMAP.md` e `third_party/README.md`;
- aggiunti `docs/CONTRACT_MAP.md`, `docs/SYSTEM_MAP.md`, `docs/AGENT_COORDINATION.md` e `docs/REMAINING_WORK.md`;
- marcato `docs/PIANO_DI_RIORDINO.md` come piano storico;
- registrato l’MCP Hub nella Source of Truth e nel changelog.

## Verifica locale

- `python -m compileall -q nosai/mcp scripts/mcp_hub_server.py scripts/mcp_dashboard_server.py`: superato;
- smoke test locali di contratti, policy, router, simulazione, learning, segreti, audit, governance e inferenza: superati;
- smoke test HTTP della dashboard MCP: superato;
- suite `pytest` completa non eseguita in questo ambiente perché il modulo `pytest` non è installato;
- build .NET completa non eseguita in questo ambiente.

## Stato GitHub Actions

Le ultime esecuzioni di `NosAi CI` e `NosAi .NET (Windows)` risultano concluse con esito `failure` sul commit di riordino. Il connettore GitHub ha restituito i job ma non il contenuto dei log/step, quindi la causa non è stata attribuita né al riordino né a un singolo componente. Il repository è ordinato, ma la verifica CI resta **non verde** e richiede una successiva esecuzione con log disponibili.

## Prossima revisione

Ripetere la stessa procedura il 2026-10-10 oppure dopo una riorganizzazione di namespace, progetti .NET o directory `docs/agents/phases/`.
