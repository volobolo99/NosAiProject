# AP-04 / A1 — Stato

> **Nota di revisione, 2026-09-07.** Questo documento registra lo stato
> al momento in cui fu scritto e alcune sue affermazioni sono state
> superate dal codice. Verificato oggi nel sorgente: `new Portal(` ha un sito: `PortalCrossingDetector.cs:89`; e la consegna A2/A4 non e' piu' PENDING.
> Le righe qui sotto restano com'erano: sono un registro, non una
> descrizione del presente.

**Avviato in anticipo rispetto al gate di fase standard**, in parallelo
all'audit indipendente di AP-03/A5, per la ragione dichiarata in
`docs/agents/EXECUTION_QUEUE.md` ("Eccezione dichiarata"): questi contratti
dipendono solo da `MapModel`/`Tile`/`TileCoordinate`/`TileTraversability`/
`Portal`/`WorldPosition` (AP-01, già `Integrated`), non da come AP-03 li
popola a runtime.

## Consegnato

`src/NosAi.Core/WorldModel/Exploration/ExplorationContracts.cs`:

- `ExplorationFootprint` — quali tile di una mappa il player ha davvero
  visitato (distinto da `MapModel.Tiles`, che per l'unica fonte reale di
  AP-03 — la griglia statica del client — rivela l'intera geometria in un
  colpo solo: "non ancora osservato" non è un segnale di frontiera utile
  qui, "non ancora visitato" sì).
- `FrontierCandidate` — input grezzi di scoring per un candidato di
  esplorazione (information gain, costo, rischio, rilevanza missione); il
  ranking/la selezione restano compito dell'algoritmo A3, non di questo
  contratto.
- `NavigationWaypoint`/`NavigationPlan` — un percorso gerarchico grezzo
  (pochi waypoint, non cella per cella) dal livello strategico verso
  l'esecuzione. Cammino cella-per-cella, stall/displacement,
  replan e verifica **non vengono re-implementati qui**: esistono già,
  sono reali, testati e verificati su Gate 1 in
  `NosAi.Runtime.Navigation.PathWalkController`/`MovementVerifier`/
  `StepGuardChain`.

Test: `tests/NosAi.Core.Tests/WorldModel/Exploration/ExplorationContractsTests.cs`
(12 test, tutti verdi). Build `NosAi.Core` pulita, 0 warning/0 errori.

## Indagine conclusa — decisione presa

Agente read-only tornato. Risultato per i 4 punti aperti:

1. **`NavigationPathfinding.cs` — `WorldMapPortalRouter.PlanMultiMapRoute`**
   fa davvero ricerca su grafo (Dijkstra/BFS per numero di hop) su portali,
   deterministica e corretta come algoritmo. Ma gira su un grafo
   **hardcoded** (`InitializeStandardNosTaleWorldGraph()`: 5 mappe, 4
   portali, coordinate letterali) — nessun loader, nessun parsing, nessuna
   fonte dati reale. È raggiungibile solo dalla propria suite di test
   (`--navigation-test`), da nessun percorso di produzione. **Conclusione:
   non c'è nulla da "fare da ponte"** — non è un bridge/adapter (A2)
   possibile, perché il dato reale a cui fare da ponte non esiste.
2. Confermato con grep (`new Portal(` su tutto `src/`): **zero risultati**.
   `Portal`/`EquatableArray<Portal>` sono sempre vuoti in ogni `MapModel`
   prodotto oggi, ovunque. Nessuna fonte dati reale per identità/
   destinazione dei portali esiste nel repository.
3. Confermato: la derivazione di `ExplorationFootprint` (quali tile
   marcare visitate) resta **A3** (algoritmo puro, Claude), non A2.
4. Il contratto A1 per l'evidenza di esecuzione movimento resta da
   scrivere, ma è un problema secondario rispetto al punto 1-2.

**Decisione:** il routing multi-mappa via portali (`NavigationPlan` con più
di un waypoint / `UsePortal` valorizzato) è **bloccato dagli stessi motivi
di AP-02**: non manca un'architettura, manca un dato reale (una tabella
portali) che nessun codice in questo repository produce ancora. Non si
inventa una fonte finta per sbloccarlo — stesso principio di
`NullObjectDetector`/OCR in AP-02.

**Ambito onesto per il prossimo passo di AP-04**, quindi ristretto
esattamente come AP-02 lo fu per la percezione:

- **Esplorazione/routing sulla stessa mappa** (nessun portale,
  `NavigationWaypoint.UsePortal = null`) è pienamente costruibile oggi:
  `ExplorationFootprint`/`FrontierCandidate` derivati da `MapModel` +
  storico posizione player (A3, Claude), `NavigationPlan` a un solo
  waypoint verso il candidato scelto (A3), poi un bridge A2/A4 (DeepSeek)
  che cabla il waypoint scelto verso l'esecuzione già reale
  (`PathWalkController`/`WalkCommand`, invariata) e riporta l'evidenza
  (`WalkOutcome`/`MovementVerification`) indietro come fatto canonico.
- **Routing multi-mappa via portali resta esplicitamente rimandato**,
  segnalato in `docs/agents/DEEPSEEK_TASKS.md` come candidato che richiede
  prima una fonte dati reale (es. parsing di una tabella portali dal
  client, stesso genere di lavoro di `MapGridExtractor` per la geometria —
  non ancora investigato).

Prossimo passo reale: Claude scrive AP-04/A3 (footprint + selezione
frontiera + `NavigationPlan` a singolo waypoint, stessa mappa) e il
contratto A1 mancante per l'evidenza di esecuzione movimento; poi
pubblica per DeepSeek la specifica precisa AP-04/A2+A4 (bridge verso
`PathWalkController`/`WalkCommand` reali) — questo sì è il lotto pesante
per DeepSeek, appena la piccola parte A1/A3 è pronta.

**Livello di verifica:** `Present` — contratti scritti, testati,
compilano puliti; non ancora `Integrated` in nessun ciclo runtime, perché
niente li produce/consuma ancora.

## A3 + contratto A1 mancante — consegnati

`src/NosAi.Core/WorldModel/Exploration/ExplorationPlanner.cs` (A3, puro e
stateless, stesso stile di `MapReconstructionFusion.Merge`/
`WorldModelTemporalEnricher`):

- `UpdateFootprint` — marca visitata la tile corrente del player (floor di
  `WorldPosition`), mai duplicata, mai fabbricata da una posizione
  `Unknown`; ricalcola `FullyExplored` confrontando le tile `Walkable`
  note contro quelle visitate (`Derived`, confidence 1.0, stesso pattern
  di `MergeBounds` in AP-03/A3). Nessun cambiamento -> stessa istanza
  (idempotenza, come `MapReconstructionFusion.Merge`).
- `BuildFrontierCandidates` — un `FrontierCandidate` per ogni tile
  camminabile non ancora visitata, ordine deterministico (segue l'ordine
  di `MapModel.Tiles`, già deterministico da AP-03). `InformationGain` =
  conteggio tile camminabili non visitate in un raggio Chebyshev di 3;
  `TravelCost`/`MissionRelevance` = distanza euclidea dal player/dal focus
  di missione (`null` -> rilevanza esattamente zero, come da contratto);
  `Risk` = decadimento lineare a zero oltre 5 tile da ogni mob ostile,
  vivo, con posizione nota (un mob senza uno di questi tre fatti non
  contribuisce rischio — mai assunto ostile/morto per omissione).
- `SelectNextFrontier` — punteggio lineare pesato
  (`FrontierScoringWeights`, default: rischio pesato 2x information
  gain), spareggio deterministico al primo candidato pari (stesso ordine
  di `BuildFrontierCandidates`, quindi due chiamate con gli stessi input
  scelgono sempre lo stesso candidato).
- `BuildNavigationPlan` — un solo waypoint, stessa mappa,
  `UsePortal = null` sempre (l'ambito è ristretto qui, non solo
  documentato: la funzione non ha modo di produrre un waypoint
  multi-mappa).

`src/NosAi.Core/WorldModel/Exploration/MovementExecutionContracts.cs` (il
contratto A1 mancante): `MovementExecutionResult` (enum) e
`MovementExecutionEvidence` — la forma canonica, basata su `WorldFact<T>`,
dell'esito di un tentativo di movimento. Rispecchia nel significato
`NosAi.Runtime.Navigation.MovementOutcome`/`MovementVerification` (già
reali, verificati su Gate 1) **senza che `NosAi.Core` li referenzi
direttamente** (`NosAi.Core` ha zero dipendenze per design): il bridge da
`MovementVerification` reale a questo contratto è lavoro di AP-04/A2.

Test: `tests/NosAi.Core.Tests/WorldModel/Exploration/ExplorationPlannerTests.cs`
(20 test) + `MovementExecutionContractsTests.cs` (7 test) — tutti verdi.
`dotnet build NosAi.sln -c Release`: 0 errori (1 warning preesistente non
collegato, `CognitiveObservabilityBridgeTests.cs`, non toccato qui).
`dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`:
**419/419**, 0 falliti (388 precedenti + 31 nuovi, zero regressioni).

**Livello di verifica A3 + contratto A1 mancante:** `Present` — stessa
ragione di sopra: nessun ciclo runtime li consuma ancora.

## Prima di scrivere la specifica pesante per DeepSeek (A2/A4): una domanda architetturale reale

`WalkCommand.Execute` (`src/NosAi.Runtime/Navigation/WalkCommand.cs`) è
già un bridge reale, completo e testato da una destinazione `MapPoint`
arbitraria sulla stessa mappa fino all'esecuzione cella-per-cella
(`AStarPathfinder` per il path — **reale, non fixture**, a differenza di
`WorldMapPortalRouter`: non serve alcun dato portale per instradare sulla
stessa mappa). Quindi il "ponte" tecnico da un `NavigationWaypoint` verso
l'esecuzione esiste già quasi per intero.

Ma `WalkCommand.Execute` oggi richiede una `ActuationAuthority`, e questa
ha solo due varianti legittime per progetto (ADR-0020, nessuna terza
"autonoma" prevista): `Commanded` (un operatore ha digitato un comando
nominato — `--walk`) o `Planned` (porta un `SafetyToken` emesso da
`ActionTokenIssuer.TryAuthorize`, che passa per `GuardPolicyEngine` e
`TrustBoundary` — la vera catena Guard→Trust→Safety). Un'esplorazione
autonoma **non ha un operatore che digita nulla**: usare `Commanded` con
una stringa inventata sarebbe esattamente l'authority "non attribuibile"
che ADR-0020 esiste per vietare. L'unica strada legittima è ottenere un
`SafetyToken` reale via `ActionTokenIssuer`, il che richiede un
`ActionCandidate` con `Target = ActionTarget.Position(...)` e un
`PredictedOutcome` — esattamente il genere di oggetti che
`Gate3Runtime.cs` costruisce già per le proprie azioni (grep conferma:
righe ~357, ~424, ~1206, ~1534).

**Prima di scrivere la specifica DeepSeek A2/A4, serve sapere se
"aggiungere il waypoint di AP-04 come una candidata azione in più dentro
la pipeline già reale di Gate3Runtime" è un'estensione piccola e pulita,
o se richiede prima pezzi che non esistono ancora** (es. un `ActionType`
dedicato a "esplora/muoviti", una lista di sorgenti di candidati aperta
vs. chiusa dentro `Gate3Runtime`, una `PredictedOutcome` per il movimento
che sia reale e non solo un placeholder fisso). Ho lanciato un'indagine
read-only mirata su `Gate3Runtime.cs` per rispondere con citazioni di
codice esatte, prima di fissare per DeepSeek un'architettura sbagliata
che poi va disfatta — stesso principio già applicato per il router
portali in questa stessa fase. Risultato e decisione finale nella
prossima sezione di questo file.

## Indagine su Gate3Runtime conclusa — decisione finale sull'ambito A2/A4

Risultato (citazioni esatte nel report dell'agente, non ripetute qui in
dettaglio): **non è un'estensione piccola e pulita**. `ActionPlanner.Plan`
(`Gate3Runtime.cs`) genera una lista di candidati **chiusa e hardcoded**
dentro il file stesso (rami `if` letterali per HP-critico/contrattacco/
skill/attacco/`TargetEntity`/`MoveToPosition` legato a un `Goal` di
caccia); aggiungere l'esplorazione come sorgente richiederebbe modificare
`Gate3Runtime.cs` stesso, non iniettare un componente. Inoltre mancano
altri due pezzi reali, non solo l'innesto: (X1) `GoalStack` produce un
`Goal` solo via `Hunt(...)` (richiede un vnum) — non esiste un `Goal`
"esplora e basta, niente da cacciare" da cui far nascere il candidato
`MoveToPosition` di AP-04; (X2) `SimulationEngine.Simulate` per
`MoveToPosition` è un placeholder fisso (`timeMs=400`, rischio da HP,
**mai** letto `candidate.Target` — nessuna predizione reale di arrivo);
(X3) l'effettore live per `MoveToPosition`
(`InputActionEffector.ClickPoint`) è un singolo click-teleport sul
pixel proiettato, **non** la camminata cella-per-cella verificata di
`PathWalkController`/`WalkCommand` — quindi anche un candidato
autorizzato non percorrerebbe davvero il tragitto.

**Conclusione onesta, coerente con la roadmap canonica stessa**: il
percorso "Strategic Orchestrator → HTN/GOAP → Guard → Trust → Safety" per
la *nuova* architettura AP-00→AP-10 non è compito di AP-04 — è
letteralmente **AP-08 "Strategic Autonomy + HTN"**
(`docs/ROADMAP_ESECUTIVA.md`), una fase futura non ancora iniziata.
`Gate3Runtime` è il sistema Gate 1-6 preesistente (binario diverso, non
ancora riconciliato con la nuova architettura — vedi il doppio binario
già segnalato nell'analisi iniziale del repository); forzare AP-04 dentro
`Gate3Runtime` oggi vorrebbe dire o modificare un file non di proprietà
di questa fase per costruire tre pezzi mancanti (X1/X2/X3) che sono fuori
ambito, oppure aggirare Guard/Trust/Safety con un'authority `Commanded`
inventata — entrambe le strade sono escluse.

**Ambito reale e onesto per AP-04/A2+A4, costruibile oggi senza toccare
Gate3Runtime e senza bypassare nulla:** un nuovo comando operatore
`--scout`, esattamente della stessa famiglia legittima di `--walk`/
`--screen-autocalibrate` (un umano digita il comando; l'automazione che
segue è "comandata", non "autonoma inventata" — nessuna violazione di
ADR-0020). Chiamato `--scout` e non `--explore` per non confonderlo con
il namespace/concetto di dominio `Exploration` (`ExplorationFootprint`,
`ExplorationPlanner`), che restano invariati. `--scout` calcola il
`NavigationPlan` dal World Model reale (via `ExplorationPlanner`, questa
fase) e lo esegue chiamando **direttamente** `WalkCommand.Execute`
(invariato) con `ActuationAuthority.Commanded("--scout")` — riusa il 100%
della catena Guard→Trust→Safety→Execute→Verify già reale e verificata su
Gate 1 per `--walk`, zero duplicazione, zero bypass. Questo è il lotto
pesante reale per DeepSeek (A4) più il piccolo bridge A2
(`MovementVerification` reale → `MovementExecutionEvidence`, contratto
già scritto). X1/X2/X3 restano segnalati come lavoro futuro di AP-08, non
di AP-04 — vedi `docs/agents/DEEPSEEK_TASKS.md`.

Specifica dettagliata: `docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_scout_command.md`.

**Livello di verifica AP-04 complessivo:** `Present` (A1+A3, contratti e
algoritmo puro) + A2/A4 `PENDING` (specifica pubblicata, in attesa che
DeepSeek la esegua).

## Seconda indagine sui portali (su richiesta esplicita dell'utente) — un percorso reale trovato, non ancora bloccato

La decisione sopra ("bloccato, manca una fonte dati reale") resta corretta
per la pista che era stata verificata allora: nessuna tabella client
decodificata elenca destinazioni di portale (confermato di nuovo per grep
su `GameReferenceDatabase.cs`/`STATO_IMPLEMENTAZIONE.md`: zero riscontri
su "portal"/"warp"/"teleport"), e `third_party/sources/taletool/` contiene
solo `UPSTREAM.md` (materiale di riferimento AGPL, non un parser
integrabile). Una **seconda pista, non verificata allora**, risulta però
reale e costruibile oggi: **osservare l'attraversamento**, non leggerlo da
un file. `ClientMemorySession.TryReadMapId` (offset
`NosTaleClientLayout.MapIdModuleOffset`, già provato e wired, non solo in
fase di ricerca) e `TryReadPlayer` (posizione X/Y live) sono infrastruttura
già reale e già polled ogni ciclo da `ScoutCommand`/`WalkCommand` — un
cambio di map id tra due letture consecutive è un evento osservabile
onestamente, senza inventare nulla: il giocatore era su una mappa, ora è
su un'altra, e l'ultima posizione nota sulla mappa di partenza è la
migliore stima onesta di dove si trovi l'uscita.

**Consegnato in questo passaggio** (A1+A3, Claude, puro):
`src/NosAi.Core/WorldModel/Reconstruction/PortalCrossingDetector.cs` —
`MapPositionReading` (map id + posizione + istante) e
`PortalCrossingDetector.DetectCrossing(previous, current)`: `null` se le
due letture condividono la stessa mappa, altrimenti un `Portal` (AP-01,
`SourceMap`/`SourcePosition` dalla lettura precedente,
`DestinationMap`/`IsActive` dalla lettura corrente, tutti `Live`).
`Portal.Id` deriva da mappa sorgente + posizione arrotondata
(`PositionRoundingUnits`, dichiarata esplicitamente una scelta di giudizio
non calibrata) — proprio perché `MapReconstructionFusion.MergeByKey` unisce
per `Id`: senza arrotondare, ogni attraversamento dello stesso portale
fisico produrrebbe una riga nuova invece di raffinare la stessa. Nessun
nuovo meccanismo di persistenza necessario: `MapObservationBatch.Portals`
(AP-03, già esistente) e la fusione via `MapReconstructionFusion` sono già
il punto di integrazione — questo contratto produce solo il `Portal` da
mettere in quel campo.

**A2+A4 specificato (Q-071, DeepSeek)**: `docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_portal_crossing_wiring.md`
— nuovo `MapReconstructionSource.RecordPortalCrossing` (merge indipendente
dal `Resolve` del ciclo corrente) + hook nel polling mappa/posizione già
esistente in `ScoutCommand`/`AutoplayCommand.RunWindows`, nessuna nuova
infrastruttura. `--collect`/`--engage`/`--recover` esclusi (non pollano
map id per round). `WorldMapPortalRouter` non toccato — questo task
accumula solo dati, non li consuma per il routing.

**Test**: `tests/NosAi.Core.Tests/WorldModel/Reconstruction/PortalCrossingDetectorTests.cs`,
7 test (nessuna crossing, campi del `Portal` derivato, confidence
di default/personalizzata, id stabile per attraversamenti dello stesso
portale con jitter di posizione, id diverso per portali chiaramente
distinti, id invariato per la stessa sorgente anche con destinazioni
diverse osservate). `dotnet build NosAi.sln -c Release`: 0 errori, 1
warning preesistente non collegato. `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **630/630**,
0 falliti (623 precedenti + 7 nuovi, zero regressioni; un fallimento
isolato di `TransportLoopTests` nella prima esecuzione, non riprodotto
alla riesecuzione — stesso flake da carico macchina già documentato più
volte in questa sessione).

**Livello di verifica**: `Present` — contratto e algoritmo puro scritti,
testati, compilano puliti; non ancora `Integrated` (nessun chiamante
runtime).

## Audit A5 (Claude) — Q-071 consegnato, nessun difetto trovato

Consegna DeepSeek `4f62c0d` (`feat(AP-04): persist observed portal crossings
in --scout/--autoplay rounds`), diretta su `main`. Rilettura riga per riga
del diff contro `AP-04_A2A4_DEEPSEEK_portal_crossing_wiring.md`: le tre
modifiche (`RecordPortalCrossing` in `MapReconstructionSource.cs`, hook in
`ScoutCommand.RunWindows`, hook identico in `AutoplayCommand.RunWindows`)
riproducono il codice della specifica carattere per carattere, incluso il
commento sull'invariante di cache (`_cachedMapId`/`_cachedResult` toccati
solo se coincidono con `Portal.SourceMap`). Nessun file fuori ambito
toccato; `PortalCrossingDetector.cs`/`MapObservationBatch.cs`/
`MapReconstructionFusion.cs` invariati come richiesto.

**Build/test indipendenti** (worktree isolato su `origin/main`, non sulla
consegna stessa):
```
dotnet build NosAi.sln -c Release → 0 Errori, 1 Warning preesistente non collegato
dotnet test .../NosAi.Runtime.Tests.csproj --filter "~MapReconstructionSourceTests" → 11/11
dotnet test .../NosAi.Runtime.Tests.csproj → 2028/2086, 0 falliti, 58 skip
dotnet test .../NosAi.Core.Tests.csproj → 629/630 (1 fallito: TransportLoopTests, flake di timing già
  documentato — riesecuzione isolata: 5/5 verde)
```

Quarta consegna DeepSeek consecutiva senza difetti in questa sessione
(dopo `--recover`, il ledger AP-09, il wiring `TargetStateComposer`
AP-02). **Livello**: `Present` invariato per il codice nuovo —
`RunWindows` non ha test unitari per convenzione dichiarata (vale anche
per `WalkCommand`); non `Integrated` finché un operatore non conferma che
un attraversamento di portale reale durante `--scout`/`--autoplay`
produce davvero una riga in `MapModel.Portals` persistita.

## Terza indagine (su richiesta esplicita dell'utente) — routing multi-mappa, algoritmo A3 consegnato

Con Q-070/Q-071/Q-072 chiusi, `MapModel.Portals` accumula `Portal` reali
per ogni mappa attraversata da un operatore. Indagine (background agent):
`WorldMapPortalRouter` (`NavigationPathfinding.cs:411-431`, sistema Gate
1-6 pre-canonico) fa già routing multi-mappa reale come algoritmo
(Dijkstra su portali) ma solo sul grafo hardcoded di test (5 mappe/4
portali fittizie) — nessun chiamante reale lo usa. Il contratto AP-04/A1
(`NavigationWaypoint.UsePortal: PortalId?`, `NavigationPlan`) supporta
già il multi-mappa; mancava solo l'algoritmo che lo popola da `Portal`
osservati invece di restare vincolato a una sola mappa
(`ExplorationPlanner`'s stesso commento di classe dichiarava questa
restrizione esplicitamente, ora aggiornato).

**Consegnato in questo passaggio** (A3, Claude, puro):
`src/NosAi.Core/WorldModel/Exploration/MultiMapRoutePlanner.cs` —
`MultiMapRoutePlanner.PlanRoute(knownMaps, startMap, startPosition,
destinationMap, destinationPosition, nowUtc) -> NavigationPlan`. BFS
sugli archi diretti (`Portal.SourceMap` → `Portal.DestinationMap`),
nessun arco sintetizzato: un `Portal` è usabile solo se
`SourcePosition`/`DestinationMap`/`IsActive` sono tutti confermati
(`IsActive` `Unknown` non è mai trattato come "probabilmente ancora
attivo" — stesso invariante "Unknown is not zero/false/empty"). Nessuna
bidirezionalità assunta: l'andata e il ritorno sono due `Portal.Id`
distinti (posizione arrotondata su mappe diverse), il ritorno serve la
propria osservazione reale. Nessuna connessione mai osservata → sempre
`NavigationPlan.Unreachable(NoRouteKnownReason)`, mai una stima.

**Test**: `tests/NosAi.Core.Tests/WorldModel/Exploration/MultiMapRoutePlannerTests.cs`,
10 test (stessa mappa senza portale, rotta diretta, rotta a due
attraversamenti tramite mappa intermedia, nessuna rotta nota, portale con
`IsActive`/`DestinationMap` non confermato escluso dal grafo, direzione
mai assunta bidirezionale, nessun loop infinito su un ciclo di portali,
la rotta più corta vince tra due candidate, determinismo a parità di
input). `dotnet build NosAi.sln -c Release`: 0 errori, 1 warning
preesistente non collegato. `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **640/640**,
0 falliti (630 precedenti + 10 nuovi, zero regressioni).

**Livello**: `Present` — algoritmo puro scritto, testato, compila pulito;
non ancora `Integrated` (nessun chiamante runtime: serve prima
un'enumerazione di tutte le mappe persistite in `MapModelStore`, oggi
solo `Save`/`TryLoad` per singola mappa, e un consumatore reale — A2+A4
specificato per DeepSeek, vedi
`docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_route_command.md`).

## Audit A5 (Claude) — Q-078 consegnato, nessun difetto trovato

Consegna DeepSeek `35fc3ef` (`feat(AP-04): map enumeration + read-only
--route diagnostic command`), diretta su `main`. Rilettura riga per riga
del diff contro `AP-04_A2A4_DEEPSEEK_route_command.md`: `MapModelStore.ListMapIds`,
`MapReconstructionSource.LoadAllKnownMaps`, `RouteProbe` e il wiring in
`Program.cs` (incluso `KnownProbeFlags`) riproducono la specifica
carattere per carattere. Nessun file fuori ambito toccato;
`MultiMapRoutePlanner.cs` invariato come richiesto.

**Build/test indipendenti** (worktree isolato su `origin/main`):
```
dotnet build NosAi.sln -c Release → 0 Errori, 1 Warning preesistente non collegato
dotnet test .../MapModelStoreTests.csproj --filter "~MapModelStoreTests" → 8/8
dotnet test .../NosAi.Runtime.Tests.csproj --filter "~MapReconstructionSourceTests" → 14/14
dotnet test .../NosAi.Runtime.Tests.csproj → 2071/2129, 0 falliti, 58 skip
dotnet test .../NosAi.Core.Tests.csproj → 642/643 (1 fallito: TransportLoopTests, stesso
  flake di macchina condivisa già documentato — riesecuzione isolata 5/5 verde,
  poi 643/643 pulito anche sotto carico dell'intera suite)
```

Sesta consegna DeepSeek consecutiva senza difetti in questa sessione.
**Livello**: `Present` per il codice nuovo — `RouteProbe.Run` non ha
test unitari per convenzione dichiarata (stessa di `TargetChainProbe.Run`);
non `Integrated` finché un operatore non conferma che `--route` contro
un client reale con almeno un attraversamento di portale già osservato
produce un piano corretto.
