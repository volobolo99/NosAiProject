# AP-00 — Repository Reality Map

**Audit baseline:** `221daea2676bc8a096ef8045b1042c5af3da58c9` (`main`)

## Purpose

Questa mappa separa ciò che è **presente nel repository** da ciò che deve ancora essere dimostrato tramite build, test o runtime evidence. Non promuove automaticamente una capability a `Verified`.

## 1. Repository e solution graph

| Area | Evidenza nel repository | Stato AP-00 |
|---|---|---|
| `NosAi.sln` | Solution presente con progetti Runtime, Protocol, GuardClient, GuardAi.App, ControlPanel, Core, Storage, Security, Adapter, Host, Analyzer e test | PRESENT |
| `Directory.Build.props` | Configurazione centralizzata selettiva; non applicata uniformemente a tutti i progetti | PRESENT / DA VALIDARE IN BUILD |
| `third_party/` | Vault/provenance gestito separatamente | PRESENT / PRESERVARE |
| CI | `.github/workflows/ci.yml` presente; restore/build Runtime espliciti | PRESENT / DA VERIFICARE SU RUN REALE |

La solution contiene effettivamente un grafo multi-progetto, quindi l'ipotesi di un singolo Runtime isolato è errata.

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

Il progetto Runtime è quindi un'applicazione reale, non una semplice libreria.

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

`Gate3Runtime.cs` espone anche `StageBoard` attraverso l'orchestratore, rendendo gli stage osservabili senza concedere al pannello autorità di esecuzione.

**Stato:** PRESENT. Deve essere verificato con test/build e con controllo dei punti di ingresso all'esecuzione.

## 5. Dashboard / Control Panel

`NosAi.ControlPanel` esiste nella solution e ospita `Gate1BootstrapHost` in-process tramite `ProjectReference` a `NosAi.Runtime.csproj`.

Il Control Panel dispone di:
- `RuntimeSession` per sessioni `Hosted` e `Attached`;
- `CaptureAsync()` via snapshot in-process oppure HTTP `/api/gate1` per runtime collegato;
- `CognitiveMemoryWindow` read-only;
- `CognitiveObservabilityRegistry` locale al processo del pannello;
- `CognitiveRuntimeTraceBridge` che ascolta `Gate3DecisionLoop` e `StageBoard` quando il pannello avvia il runtime hosted.

**Stato:** PRESENT / INTEGRATION PATH IDENTIFICATO.

### AP-00.2 — risultato audit Runtime → Gate1 → Gate3 → Dashboard

1. `RuntimeSession` costruisce un `CognitiveRuntimeTraceBridge` quando `host.Decisions` è disponibile.
2. Il bridge esistente ascolta `loop.Orchestrator.StageBoard.StageRecorded` e `loop.CycleCompleted`, quindi non esiste un riferimento a una classe bridge inesistente: il simbolo `CognitiveRuntimeTraceBridge` è effettivamente presente nel progetto ControlPanel.
3. `Gate3DecisionLoop` pubblica inoltre trace cognitivi dettagliati sul registry statico del progetto Runtime.
4. Il Control Panel mantiene però un **secondo registry statico**, separato da quello Runtime, e il bridge riversa nel registry del Control Panel gli stage/decisioni necessari alla UI.
5. Di conseguenza il percorso dashboard **hosted/in-process** è osservabile tramite bridge, mentre il percorso **attached/cross-process** non può condividere uno static registry e deve usare una superficie IPC/HTTP esplicita se in futuro si vorrà una cognitive trace completa anche per runtime esterno.
6. Questa separazione non è un blocker per il Test Center Gate1, ma è un limite architetturale da mantenere esplicito: non dichiarare “live cognitive dashboard cross-process” finché non esiste un endpoint/transport dedicato per la cognitive observability.

**Verdetto AP-00.2:** `INTEGRATION PATH PRESENT`, con **limite cross-process documentato**. Nessuna modifica al critical path Gate3 è giustificata da questa sola evidenza.

## 6. Navigation

Il repository contiene contratti/planner/evaluator di navigation e il Test Center prevede T5. La chiusura di T5 richiede evidence osservabile di movimento/replan, non la sola presenza del planner.

**Stato:** IMPLEMENTATION PRESENT / T5 NON PROMOSSO A VERIFIED.

## 7. Core / Storage / Security / Adapter / Host

I progetti sono membri della solution e `Directory.Build.props` applica a diversi di essi nullable, warnings-as-errors e analyzer reference.

**Stato:** PRESENT / BUILD DA ESEGUIRE.

## 8. Test surface

Esistono almeno:
- `tests/NosAi.Runtime.Tests`;
- `tests/NosAi.ControlPanel.Tests`;
- `tests/NosAi.Core.Tests`.

Esiste un test dedicato al `CognitiveRuntimeTraceBridge` nel progetto ControlPanel.Tests e test dedicati al `CognitiveObservabilityBridge` nel progetto Runtime.Tests. La presenza dei test non sostituisce l'esecuzione.

**Stato:** PRESENT / RESULT NON VERIFICATO IN QUESTO AUDIT.

## 9. Build scripts e CI

Il repository contiene script PowerShell/Shell per restore/build Runtime.

La CI contiene uno step di restore del Runtime e una pipeline di build dedicata.

**Nota:** la presenza dello script o della workflow non equivale a un risultato PASS. Serve una run effettiva.

## 10. Contraddizioni / rischi da verificare

### R1 — Scope di `Directory.Build.props`

La configurazione root non è globale per tutti i progetti. Questo è intenzionale secondo il commento del file, ma va mantenuto coerente con la roadmap e con il project graph.

### R2 — Runtime e ControlPanel

Il Control Panel referenzia Runtime e ospita `Gate1BootstrapHost` in-process. Il repository non mostra un secondo orchestratore indipendente nel pannello: le decisioni rimangono nel Runtime/Gate3.

### R3 — Cognitive observability cross-process

Il registry del Runtime e quello del Control Panel sono process-local. `CognitiveRuntimeTraceBridge` copre il caso hosted/in-process. Per `Attached` serve un transport esplicito per portare trace cognitivi al pannello; il solo registry statico non basta.

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

**Decisione: OPEN — audit strutturale in corso.**

La repository è presente e il grafo applicativo è sostanziale. AP-00.2 ha confermato il wiring hosted Runtime → Gate3 → CognitiveRuntimeTraceBridge → Control Panel, ma ha anche confermato che la cognitive observability completa non è cross-process. Il prossimo passo corretto è validare build/test su Windows e poi affrontare esclusivamente i blocker dimostrati. Nessuna feature AP-01 deve essere usata per mascherare blocker AP-00.
