# AP-04 — Exploration & Navigation — Stato finale

## 1. Ambito

Scoperta e attraversamento progressivo di mappe senza percorso hardcoded
(`docs/ROADMAP_ESECUTIVA.md` S:AP-04). Routing multi-mappa via portali
resta esplicitamente rimandato (nessuna fonte dati reale, vedi
`AP-04_A1_STATUS.md`) — questa fase copre esplorazione/navigazione entro
una singola mappa già ricostruita (AP-03).

## 2. A1 — Contratti esplorazione/navigazione

`src/NosAi.Core/WorldModel/Exploration/ExplorationContracts.cs`:
`ExplorationFootprint`, `FrontierCandidate`, `NavigationWaypoint`,
`NavigationPlan`. `src/NosAi.Core/WorldModel/Exploration/MovementExecutionContracts.cs`:
`MovementExecutionResult`, `MovementExecutionEvidence` (contratto A1
mancante, aggiunto dopo A3 una volta chiaro cosa l'esecuzione reale poteva
riportare). Vedi `AP-04_A1_STATUS.md`.

## 3. A3 — `ExplorationPlanner`

`src/NosAi.Core/WorldModel/Exploration/ExplorationPlanner.cs`: footprint,
ranking frontiera, `NavigationPlan` a singolo waypoint, stessa mappa. Vedi
`AP-04_A1_STATUS.md` §"A3 + contratto A1 mancante".

## 4. A2+A4 — `MovementVerificationProjector` + comando operatore `--scout` (DeepSeek)

Indagine su `Gate3Runtime` conclusa prima di specificare (vedi
`AP-04_A1_STATUS.md`): candidate generation chiusa/hardcoded, predizione
placeholder fissa, nessun effettore reale per `MoveToPosition` — nessun
bridge verso quella pipeline. Specifica scritta:
`AP-04_A2A4_DEEPSEEK_scout_command.md`.

Consegnato da DeepSeek (commit `1891e7d`, `d011815`, `7080fcd`):

- `src/NosAi.Runtime/WorldModel/Fusion/MovementVerificationProjector.cs`
  (A2) — bridge puro e meccanico da `MovementVerification` reale a
  `MovementExecutionEvidence`.
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs` (A4) — comando operatore
  `--scout [--watch <n>]`: calcola un `NavigationPlan` dal World Model
  reale ed esegue chiamando `WalkCommand.Execute` **invariato**, con
  `ActuationAuthority.Commanded("--scout")` — stessa famiglia di autorità
  di `--walk`, stessa guard chain, stesso verificatore, zero bypass, zero
  duplicazione della logica di cammino.
- `src/NosAi.Runtime/Navigation/WalkCommand.cs` — un solo parametro
  opzionale additivo, `onStepVerified`, default `null`: zero cambio di
  comportamento per ogni chiamante esistente.
- 60 test nuovi tra i due file di test (`ScoutCommandTests.cs`,
  `MovementVerificationProjectorTests.cs`).

## 5. A5 — Audit indipendente

Report completo: `AP-04_A5_AUDIT.md`. **Un difetto reale trovato**:
`onStepVerified` veniva invocato anche per un passo mai emesso (rifiuto
guard a metà cammino), contraddicendo il contratto dichiarato tre volte
(commento XML del parametro, commento XML di `ScoutCommand.onEvidence`,
testo della specifica) — origine nella specifica stessa, non in una
deviazione di DeepSeek, che ha implementato fedelmente quanto scritto.
Nessun altro difetto trovato dopo un passaggio avversariale su autorità,
fabbricazione dati nel projector, confine `NosAi.Core`/`NosAi.Runtime`,
ciclo di vita risorse, robustezza Unknown/null, parsing `--watch`,
concorrenza.

## 6. A6 — Integrazione finale

Applicata l'unica correzione richiesta: in `WalkCommand.Execute`,
`onStepVerified?.Invoke(to, verification)` è stato spostato dentro il
blocco `if (report.Emitted)`, dopo `stepsEmitted++` — un passo che un
guard rifiuta non raggiunge più la callback, esattamente come il
contratto dichiarato promette. Nessuna modifica di firma pubblica.

Aggiunto un test di regressione dedicato,
`WalkCommandTests.OnStepVerified_NeverFiresForAStepAGuardRefused_OnlyForEmittedSteps`:
un cammino a due celle dove il primo passo riesce (la callback dispone la
prova e disarma l'input live) e il secondo viene rifiutato dal guard di
policy — la callback deve scattare esattamente una volta, mai due. Verde
sulla correzione, rosso (con la vecchia riga 333 incondizionata) se
reintrodotto — confermato manualmente ripercorrendo la logica prima della
correzione.

**Evidenza:**
```
dotnet build NosAi.sln -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release \
  --filter "FullyQualifiedName~WalkCommandTests|FullyQualifiedName~ScoutCommandTests"
  → Passed! Failed: 0, Passed: 25, Skipped: 0, Total: 25

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1971, Skipped: 58, Total: 2029

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 579, Skipped: 0, Total: 579
```

Nessuna regressione rispetto ai conteggi riportati da A5 (36/36 il
sottoinsieme filtrato, ora 25 perché `MovementVerificationProjectorTests`
non rientra in questo filtro ristretto; 1971 = 1967 + 4 nuovi test tra
questo fix e quello di AP-05, vedi `AP-05_STATUS.md`).

## 7. Livello di verifica finale — AP-04

**`Integrated`**: A1+A2+A3+A4 costruiscono un albero unico che compila
pulito e passa tutti i test combinati, incluso il difetto reale trovato
dall'audit indipendente A5 e corretto in questo passaggio. **Non
`Verified`**: nessuna validazione contro un client NosTale reale — questo
ambiente Linux non ha un client reale né l'hardware target.

## 8. Item aperti, esplicitamente rimandati

- **Routing multi-mappa via portali**: nessuna fonte dati reale, stesso
  limite già dichiarato in `AP-04_A1_STATUS.md`.
- **`FrontierCandidate.Risk` come `double` grezzo invece di
  `WorldFact<double>`**: segnalato da A5 §5, non corretto qui (richiede
  una decisione di design, non un fix puntuale).
- **`--scout` non ancora eseguito contro un client reale**: stesso limite
  di ogni altro stadio, non una lacuna di codice.
