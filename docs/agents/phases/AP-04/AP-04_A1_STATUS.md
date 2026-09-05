# AP-04 / A1 — Stato

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
