# AP-00 — Repository Reality Map

**Audit baseline:** `b4f7eaf4c1b59e42be26104b75c0de83ee2f0adb` (`main`)

## Purpose

Questa mappa separa ciò che è **presente nel repository** da ciò che deve ancora essere dimostrato tramite build, test o runtime evidence. Non promuove automaticamente una capability a `Verified`.

## 1. Repository e solution graph

| Area | Evidenza nel repository | Stato AP-00 |
|---|---|---|
| `NosAi.sln` | Solution presente con progetti Runtime, Protocol, GuardClient, GuardAi.App, ControlPanel, Core, Storage, Security, Adapter, Host, Analyzer e test | PRESENT |
| `Directory.Build.props` | Configurazione centralizzata selettiva; non applicata uniformemente a tutti i progetti | PRESENT / DA VALIDARE IN BUILD |
| `third_party/` | Vault/provenance gestito separatamente | PRESENT / PRESERVARE |
| CI | `.github/workflows/ci.yml` presente; restore/build Runtime espliciti | PRESENT / DA VERIFICARE SU RUN REALE |

La solution contiene effettivamente un grafo multi-progetto, quindi l'ipotesi di un singolo Runtime isolato è errata. fileciteturn204file0L2-L2

## 2. Runtime

**Progetto:** `src/NosAi.Runtime/NosAi.Runtime.csproj`

Evidenze:
- `net8.0-windows`;
- executable;
- `NosAi.Runtime.Program` come startup object;
- unsafe abilitato per interop DXGI/Win32;
- dipendenze Protocol e Core;
- package references per Management, SQLite, BouncyCastle e ProtectedData;
- copia opzionale di WinDivert DLL/SYS se presenti nel vault third-party.

Il progetto Runtime è quindi un'applicazione reale, non una semplice libreria. fileciteturn205file0L2-L2

**Stato:** PRESENT. Build locale non eseguita da questo audit remoto.

## 3. Gate1 / Practical Test Center

Ricercare e verificare in locale:
- catalogo T1-T20;
- endpoint/snapshot Gate1;
- `PracticalTestCenterWindow`;
- wiring Runtime → Test Center;
- stato T5 navigation;
- test bloccati intenzionalmente (auth/execution/private-server E2E).

**Stato:** PRESENT secondo il codice/documentazione già censiti; `PASS` runtime non assegnato senza esecuzione reale.

## 4. Gate3

Percorso critico documentato e cercato nel repository:

`Observe → Plan → Simulation → Ranking → Guard → Safety → Execute → Verify`

`Gate3Runtime.cs` è indicato dal repository index come percorso critico completo. fileciteturn203file1L23-L30

**Stato:** PRESENT. Deve essere verificato con test/build e con controllo dei punti di ingresso all'esecuzione.

## 5. Dashboard / Control Panel

`NosAi.ControlPanel` esiste nella solution e contiene il wiring del pannello verso Runtime. La ricerca del repository mostra un riferimento esplicito a `NosAi.Runtime.csproj` dal Control Panel. fileciteturn206file8L105-L113

**Stato:** PRESENT.

Da verificare:
- se il flusso cognitive observability è realmente condiviso tra processi o solo in-process;
- se il polling ha lifecycle/cancellation corretti;
- se ogni dato visualizzato proviene da osservazione reale;
- che il pannello non possieda execution authority.

## 6. Navigation

Il repository contiene contratti/planner/evaluator di navigation e il Test Center prevede T5. La chiusura di T5 richiede evidence osservabile di movimento/replan, non la sola presenza del planner.

**Stato:** IMPLEMENTATION PRESENT / T5 NON PROMOSSO A VERIFIED.

## 7. Core / Storage / Security / Adapter / Host

I progetti sono membri della solution e `Directory.Build.props` applica a diversi di essi nullable, warnings-as-errors e analyzer reference. fileciteturn202file0L2-L2

**Stato:** PRESENT / BUILD DA ESEGUIRE.

## 8. Test surface

Esistono almeno:
- `tests/NosAi.Runtime.Tests`;
- `tests/NosAi.ControlPanel.Tests`;
- `tests/NosAi.Core.Tests`.

Il Runtime test project referenzia direttamente `NosAi.Runtime.csproj`. fileciteturn206file12L160-L168

**Stato:** PRESENT / RESULT NON VERIFICATO IN QUESTO AUDIT.

## 9. Build scripts e CI

Il repository contiene script PowerShell/Shell per restore/build Runtime. fileciteturn206file1L13-L20

La CI contiene uno step di restore del Runtime e una pipeline di build dedicata. fileciteturn206file13L171-L179

**Nota:** la presenza dello script o della workflow non equivale a un risultato PASS. Serve una run effettiva.

## 10. Contraddizioni / rischi da verificare

### R1 — Scope di `Directory.Build.props`

La configurazione root non è globale per tutti i progetti. Questo è intenzionale secondo il commento del file, ma va mantenuto coerente con la roadmap e con il project graph. fileciteturn202file0L2-L2

### R2 — Runtime e ControlPanel

Il Control Panel referenzia Runtime. Va verificato che questo non introduca un secondo orchestratore o un ciclo architetturale indesiderato.

### R3 — Cognitive observability cross-process

La sola presenza di un registry statico non dimostra che un Runtime separato e il Control Panel condividano eventi. Questa è una verifica architetturale obbligatoria prima di dichiarare “live cognitive dashboard”.

### R4 — T5

Planner/evaluator presenti non equivalgono a movimento reale validato. T5 resta aperto fino a evidence runtime.

### R5 — Build evidence

Questo audit remoto non può sostituire l'esecuzione locale di `dotnet restore/build/test` su Windows. Non viene dichiarato alcun PASS in assenza dell'evidence.

## 11. Comandi AP-00 da eseguire localmente

```powershell
dotnet restore NosAi.sln
dotnet build NosAi.sln --configuration Release
dotnet test NosAi.sln --configuration Release

dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --configuration Release
```

Per il ciclo operativo reale, gli agenti devono inoltre catturare:

```powershell
git status --short
git diff --stat
git diff --name-status
```

## 12. AP-00 decisione corrente

**Decisione: OPEN — audit strutturale avviato, non ancora Verified.**

La repository è presente e il grafo applicativo è sostanziale. Il prossimo passo corretto è eseguire i comandi di build/test su ambiente Windows e poi correggere esclusivamente i problemi dimostrati. Nessuna feature AP-01 deve essere usata per mascherare blocker AP-00.
