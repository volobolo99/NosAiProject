# Handoff AP-01 / A3 — Temporal Prediction (Simulation/Prediction)

```text
TASK_ID: AP-01-A3-TEMPORAL-PREDICTION-001
PHASE: AP-01
AGENT: A3 (Temporal Prediction)
REPO_CLONE: clonato da $HOME/mnt/NosAiProject (branch codex/cursor-perception-contract,
            HEAD 23747c8) in uno scratch locale fuori dal mount, per i vincoli d'ambiente
            descritti sotto.

WRITE_FILES (nuovi, nessuno preesistente sovrascritto):
- src/NosAi.Runtime/TemporalPrediction/PredictionContracts.cs
- src/NosAi.Runtime/TemporalPrediction/TemporalWorldPrediction.cs
- src/NosAi.Runtime/TemporalPrediction/TemporalPredictionEngine.cs
- tests/NosAi.Runtime.Tests/TemporalPredictionEngineTests.cs
- docs/agents/phases/AP-01/A3_HANDOFF_TEMPORAL_PREDICTION.md (questo file)

CHANGED_FILES:
- (identico a WRITE_FILES: 5 file creati, 0 modificati, 0 cancellati nel repository reale)

NOT_MODIFIED (letti soltanto, o non toccati per vincolo ambientale — vedi "Vincoli d'ambiente"):
- src/NosAi.Runtime/Gate3/Gate3WorldState.cs (contratto consumato, intatto)
- src/NosAi.Runtime/Contracts/DataClassification.cs, MapPoint.cs, PredictedOutcome.cs
- src/NosAi.Runtime/Autonomy/TargetSelector.cs (SelectableEntity)
- src/NosAi.Runtime/Perception/PerceptionPipeline.cs (Kalman2DFilter, riusato non modificato)
- src/NosAi.Runtime/Gate1/Gate1CanonicalSnapshot.cs (precedente di versioning)
- src/NosAi.Runtime/Observability/ModuleReachability.cs — **NON modificato nel repository
  reale** per il vincolo "solo file nuovi"; vedi BLOCKERS per la riga esatta da applicare.
- src/NosAi.Runtime/Perception/Fusion/*, src/NosAi.Runtime/WorldModel/ClassifiedWorldModel.cs,
  ObservationSnapshotFusionFeed.cs, src/NosAi.Runtime/Gate3/WorldModelWorldStateSource.cs,
  src/NosAi.Runtime/Gate1/Gate1BootstrapHost.cs, tests/.../SensorFusionTests.cs,
  tests/.../WorldModelRuntimeWiringTests.cs — dichiarati in lavorazione da altri ruoli nel
  repository reale (non presenti/non modificati nel mio clone); non referenziati.

IMPLEMENTATION:
- Namespace NosAi.Runtime.TemporalPrediction (cartella nuova src/NosAi.Runtime/TemporalPrediction/,
  stesso assembly NosAi.Runtime — nessun nuovo .csproj necessario, l'SDK include i .cs per glob).
- Stage "Simulation/Prediction" della pipeline canonica: consuma
  NosAi.Runtime.Gate3.Gate3WorldState (già committato, non nella lista dei file in lavorazione
  altrui) e produce TemporalWorldPrediction, un output puramente dati per uno stadio di
  Ranking/Utility a valle. Nessuna interfaccia di esecuzione: TemporalPredictionEngine espone
  un solo metodo (Predict), nessun riferimento a NosAi.Runtime.Safety/Security, nessun tipo con
  handle/token/capability nel grafo di output (advisory-only per costruzione, non per
  convenzione — vedi i commenti XML su ITemporalPredictionEngine).
- Predicted<T>: sibling di ClassifiedValue<T> pensato per le previsioni — stesso schema
  concettuale (Value/Source/HasValue/FailureReason/Warning) più Confidence, Horizon,
  BasisObservedAtUtc, GeneratedAtUtc, Model. Source è sempre SIMULATED quando HasValue è vero,
  sempre UNKNOWN altrimenti — mai LIVE, mai uno zero fabbricato spacciato per previsione.
- PredictionModel {None, HoldLastValue, ConstantVelocityKalman2D, LinearRate}: ogni valore
  previsto dichiara il metodo deterministico che lo ha prodotto. Con un solo campione la
  posizione usa HoldLastValue (ipotesi "nulla suggerisce il contrario", confidenza ridotta,
  Warning esplicito) — ma la velocità con un solo campione resta UNKNOWN, mai 0: uno zero
  fabbricato si leggerebbe come "fermo, osservato", non come "non ancora misurabile".
- Cinematica (giocatore + ogni SelectableEntity in Gate3WorldState.Entities): riusa
  NosAi.Runtime.Perception.Kalman2DFilter (filtro 2D a velocità costante, già committato e
  vettato altrove nel progetto — non reimplementato) come primitiva di calcolo. L'ingest
  (Filter.Predict(dt) + Filter.Update(x,y)) avviene solo su un nuovo avvistamento più recente
  del precedente; la proiezione verso l'orizzonte richiesto è una funzione pura e ripetibile
  dello stato filtrato corrente (X, Y, VelocityX, VelocityY) — non avanza mai lo stato interno
  del filtro, cosi' più orizzonti possono essere interrogati sullo stesso stato senza effetti
  collaterali.
- HP/MP: tracciamento scalare a due campioni con tasso lineare (LinearRate), clampato a
  [0, MaxHp] quando MaxHp è noto (Gate3WorldState.MaxHp) e a [0, +inf) per MP.
- Confidenza: un'unica formula lineare condivisa da ogni campo,
  `confidence = clamp01(1 - (age + horizon) / (MaxInputAge + MaxHorizon)) * sampleFactor`,
  dove `age` è quanto è vecchia l'ultima osservazione reale rispetto a `nowUtc` e `horizon` è
  quanto lontano si proietta oltre `nowUtc`. Sotto 0 diventa un rifiuto esplicito (Unknown), mai
  un numero di confidenza basso che nessuno è obbligato a controllare. sampleFactor dimezza la
  confidenza quando il campo si basa sull'ipotesi a un solo campione.
- Fail-closed a tre livelli, dal più al meno globale: (1) intero ciclo rifiutato
  (TemporalWorldPrediction.CannotPredict) se l'orizzonte richiesto è fuori range, lo stato non è
  pianificabile (Gate3WorldState.IsPlannable), o la lettura più vecchia dello stato supera
  MaxInputAge; (2) singolo campo rifiutato (Predicted<T>.Unknown) se manca l'osservazione di
  base o la confidenza si esaurisce; (3) nessuna invenzione silenziosa in nessun punto — la
  stessa regola "solo chi legge un fatto assente ne paga il costo" di Gate3WorldState.
- Versionamento: TemporalPredictionContract.Version = "prediction.temporal.v1", stessa
  convenzione (costante stringa pubblica, dichiarata su ogni valore pubblicato) di
  Gate1SnapshotContract.Version in src/NosAi.Runtime/Gate1/Gate1CanonicalSnapshot.cs — è la
  precedente più diretta trovata nel repository per un contratto pubblico versionato.
- Zero-allocation dove ha senso: PositionTrack/ScalarTrack sono classi mutabili riusate per
  entità/giocatore/HP/MP attraverso le chiamate (un dizionario per entità, chiave EntityId),
  nessuna LINQ nel percorso di ingest/proiezione, una sola lista di scratch riusata per il
  pruning dei track scaduti. L'output (TemporalWorldPrediction, la lista di
  PredictedEntityState) alloca comunque, essendo il dato restituito al chiamante — non è un
  percorso a frequenza di rete, è al ritmo del ciclo Gate 3.
- Velocity2D: readonly struct [StructLayout(Pack = 1)], sullo stesso stile di NosFrameHeader.

DEPENDENCIES:
- NosAi.Runtime.Gate3.Gate3WorldState, NosAi.Runtime.Contracts.{ClassifiedValue<T>,
  DataSourceKind, MapPoint}, NosAi.Runtime.Autonomy.SelectableEntity,
  NosAi.Runtime.Perception.Kalman2DFilter — tutti già committati, nessuno nella lista dei file
  in lavorazione da altri ruoli.
- NESSUNA dipendenza esterna nuova (nessun pacchetto NuGet aggiunto).

TEST_COMMANDS:
- export PATH="$HOME/.dotnet8:$PATH"; export DOTNET_ROOT="$HOME/.dotnet8"
- dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
- dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
- dotnet restore tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj
- dotnet build tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
- dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
    --filter "FullyQualifiedName~TemporalPredictionEngineTests"
- dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release   (suite intera)

TEST_RESULTS:
- Build src/NosAi.Runtime: PASS — 0 Warning(s), 0 Error(s).
- Build tests/NosAi.Runtime.Tests: PASS — 0 Warning(s), 0 Error(s).
- Suite dedicata (TemporalPredictionEngineTests, 13 test): PASS — Passed: 13, Failed: 0,
  Total: 13, 0.61s (net8.0-windows via EnableWindowsTargeting, Linux, SDK 8.0.424).
- Suite intera (1721 test): Passed: 1661, Failed: 15, Skipped: 45 — le 15 failure sono
  precedenti al mio contributo e non lo riguardano: 14 sono precondizioni Windows-only
  (RuntimeEnvironmentException "platform.windows ... Ubuntu 22.04.5 LTS: DPAPI key custody
  (ADR-0010) and the capture and input backends require Windows" su Gate1BootstrapHost, più le
  proiezioni schermo/DPI che dipendono dalla stessa catena) — attese su Linux, non legate a
  TemporalPrediction. La 15esima è ModuleReachabilityTests.Every_namespace_in_the_runtime_is_
  declared, che fallisce esattamente perché il nuovo namespace NosAi.Runtime.TemporalPrediction
  non è dichiarato in ModuleReachability.cs (governance test intenzionale — vedi BLOCKERS: non
  ho potuto applicare la correzione nel repository reale per il vincolo "solo file nuovi"). Con
  la riga aggiunta *solo nel mio clone locale di verifica* la suite intera passa a
  Passed: 1662, Failed: 14, Skipped: 45 — le 6 ModuleReachabilityTests passano tutte, a
  conferma che l'unica cosa mancante per una dichiarazione pulita è quella riga.

BUILD_RESULTS: PASS (vedi TEST_COMMANDS/TEST_RESULTS sopra).

VERIFICATION_LEVEL: Integrated (compila da solo, i suoi test passano in isolamento; NON
  "Verified" — nessuna validazione su client reale, nessun collegamento a Gate 3/Ranking finché
  A6 non lo aggancia — per la definizione di verifica del progetto questo è esattamente
  Integrated, non oltre).

DIFF_REVIEW:
- File inattesi: nessuno.
- Cancellazioni: nessuna.
- Segreti: nessuno.
- Conflitti di ownership: nessuno — cartella nuova src/NosAi.Runtime/TemporalPrediction/, un
  solo file di test nuovo (nome non in collisione con i file di test riservati ad altri ruoli).

SAFETY_REVIEW:
- Autorità di esecuzione: NO — nessuna interfaccia di esecuzione esposta, nessun riferimento a
  Safety/Security, l'unico output è un record immutabile.
- Provenienza: ogni valore prodotto è SIMULATED (mai LIVE); ogni valore non producibile è
  UNKNOWN (mai zero fabbricato).
- Fail-closed: sì, su tre livelli (vedi IMPLEMENTATION).
- Contratto versionato: sì, TemporalPredictionContract.Version = "prediction.temporal.v1".
```

## Vincoli d'ambiente incontrati

Il mount `$HOME/mnt/NosAiProject` blocca la sovrascrittura di file esistenti (unlink fallisce
con "Operation not permitted"); creare file nuovi funziona. Ho quindi lavorato in un clone
locale (`$HOME/nosai-scratch-a3`, fuori dal mount) per build/test, e alla fine ho copiato con
`cp` solo i file nuovi elencati sopra dentro `$HOME/mnt/NosAiProject`, poi tentato `git add` +
`git commit` lì (esito riportato nel messaggio finale della sessione, non in questo documento
statico).

## Collisione di nome scoperta e risolta

Il namespace inizialmente scelto, `NosAi.Runtime.Prediction`, rompeva la compilazione di
`tests/NosAi.Runtime.Tests/PredictionLedgerTests.cs` (file esistente, non toccato): quel file usa
l'identificatore nudo `Prediction` per riferirsi a `NosAi.Runtime.Learning.Prediction` (il
record del ledger di calibrazione), e avere un namespace annidato chiamato `Prediction`
direttamente sotto `NosAi.Runtime` fa sì che la risoluzione dei nomi di C# preferisca il
namespace al tipo importato con `using`, con errore `CS0118: 'Prediction' is a namespace but is
used like a type`. Rinominato in `NosAi.Runtime.TemporalPrediction` (e la cartella sorgente di
conseguenza): l'errore sparisce, `PredictionLedgerTests.cs` compila invariato. Questo è stato
scoperto **solo** eseguendo davvero la build della suite di test — la ragione per cui l'istruzione
di verificare con build/test reali, e non fidarsi della sola compilazione del progetto runtime, è
stata rispettata fino in fondo.

## Relazione con il codice di simulazione/predizione già esistente

`NosAi.Runtime.Gate3.SimulationEngine` / `PredictedOutcome` (già committati, già agganciati in
`Gate3ExecutionOrchestrator`) simulano l'*effetto di una candidata azione* da una tabella fissa
di delta per tipo di azione. Questo modulo (`TemporalPredictionEngine` /
`TemporalWorldPrediction`) proietta invece come evolve il *mondo stesso* nel breve periodo,
indipendentemente da quale azione venga scelta — dove sarà un'entità, come si muove l'HP — così
uno stadio di ranking può chiedersi "sarà ancora vero quando l'azione arriverà a bersaglio"
prima ancora di scegliere una candidata. I due sono complementari e non si sovrappongono:
nessuna collisione di nomi con `SimulationEngine`/`PredictedOutcome`/`TacticalRankingEngine`
(namespace separato, tipi con nomi distinti).

## Scoperta importante durante la ricognizione: due binari paralleli per il World Model

Nel Project (`claude/HANDOFF_AP-01_A1_WORLD_MODEL_CONTRACTS.md`) risulta un lavoro di A1 che
introduce `src/NosAi.Core/WorldModel/*` (`Fact<T>`, `FactProvenance`, `WorldModelSnapshot`,
ecc.) con una nota esplicitamente indirizzata "a A2 (sensor fusion) e A3 (temporal belief)" su
come costruire previsioni con `Fact<T>.Simulated(...)`/`Derived`. **Quel lavoro non è presente
nel mio clone**: il documento stesso dice che il push è stato rifiutato dal proxy git della
sessione e che il commit esiste solo come patch allegata (`AP-01-A1-world-model-contracts.patch`),
mai applicata a `main`. Confermato anche direttamente: `src/NosAi.Core/WorldModel/` non esiste
nel repository clonato da `$HOME/mnt/NosAiProject`. Per le istruzioni ricevute per questo task —
costruire solo contro tipi realmente committati — non ho potuto e non ho dovuto dipendere da
`Fact<T>`/`WorldModelSnapshot`; ho costruito contro `Gate3WorldState`/`ClassifiedValue<T>`
(l'unico World Model realmente presente nel repository).

**Nota per A6 (e per un futuro A3 se la patch di A1 viene applicata):** quando/se
`AP-01-A1-world-model-contracts.patch` viene applicata e mergiata, questo modulo avrà bisogno di
un adattatore aggiuntivo (nuovo file, non una riscrittura) che consumi anche
`NosAi.Core.WorldModel.WorldModelSnapshot`/`PlayerState`/`MobState` e pubblichi previsioni come
`Fact<T>.Simulated(...)`, secondo la convenzione che A1 ha già scritto per questo scopo. Non è
stato fatto qui perché quel contratto non esiste ancora nel ramo committato.

## Cosa manca (riga esatta per A6)

`src/NosAi.Runtime/Observability/ModuleReachability.cs` va aggiornato con una voce per il nuovo
namespace, altrimenti `ModuleReachabilityTests.Every_namespace_in_the_runtime_is_declared`
fallisce (verificato: è l'unica delle 15 failure della suite intera causata da questo
contributo). Non l'ho applicato nel repository reale perché è un file esistente e il vincolo
d'ambiente per questo task è "solo file nuovi". Riga da aggiungere (in coda alla sezione
"nothing reaches them at all", dopo la voce `NosAi.Runtime.Telemetry`):

```csharp
new("NosAi.Runtime.TemporalPrediction", ModuleReach.Unreferenced,
    "AP-01/A3: the Simulation/Prediction pipeline stage -- short-horizon "
    + "kinematic/vitals forecasting from Gate3WorldState, advisory-only "
    + "(TemporalPredictionEngine). Exercised by its own suite but nothing "
    + "in Gate 3 or a ranking/utility stage calls it yet; wiring it into "
    + "the decision loop is deferred to the AP-01 integration pass (A6), "
    + "once the in-flight World Model/Fusion work lands "
    + "(ClassifiedWorldModel, ObservationSnapshotFusionFeed, "
    + "WorldModelWorldStateSource)."),
```

Applicata questa riga in un clone locale di verifica, le 6 `ModuleReachabilityTests` passano e
la suite intera scende a 14 failure (tutte precondizioni Windows-only, indipendenti da questo
contributo).

## Wiring differito (atteso, come da istruzioni ricevute)

`TemporalPredictionEngine` non è agganciato a nessun chiamante di produzione: nessun punto in
`Gate3DecisionLoop`/`Gate3ExecutionOrchestrator` lo invoca ancora, per costruzione — le
istruzioni per questo task chiedevano esplicitamente di non toccare i file in lavorazione da
altri ruoli (`Perception/Fusion/*`, `WorldModel/ClassifiedWorldModel.cs`,
`ObservationSnapshotFusionFeed.cs`, `Gate3/WorldModelWorldStateSource.cs`,
`Gate1/Gate1BootstrapHost.cs`) e quindi di non presumerne le forme non ancora committate.
Il collegamento — chi chiama `TemporalPredictionEngine.Predict(...)` con quale
`Gate3WorldState` e quale orizzonte, e chi consuma `TemporalWorldPrediction` nello stadio di
Ranking/Utility — è il lavoro di integrazione (A6), da fare una volta che il lavoro in corso su
World Model/Fusion è committato e la forma finale di `Gate3WorldState` (o del suo successore) è
stabile.
