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

## Cosa NON è ancora deciso (blocca la specifica precisa DeepSeek A2/A4)

Prima di pubblicare per DeepSeek una specifica precisa di AP-04/A2
(adapter/estrazione) e AP-04/A4 (wiring runtime), serve sapere:

1. **`NosAi.Runtime.Navigation.Pathfinding.NavigationPathfinding.cs`**
   sembra già fare routing multi-mappa attraverso portali (route con
   `CurrentMapId`/`UsePortalToNextMap`). Se è reale (non solo testato su
   fixture) e ha una fonte dati portali vera, allora costruire
   `NavigationPlan` è un lavoro di **bridge/adapter (A2)**, non un nuovo
   algoritmo (A3) — esattamente come AP-03/A2 ha fatto ponte verso
   `MapGrid` invece di reinventare la lettura della griglia statica.
2. Se invece è test-only o la connettività dei portali non ha ancora una
   fonte dati reale, `NavigationPlan`'s costruzione è un vero algoritmo
   nuovo (A3, Claude) da scrivere sopra il grafo mappa/portali.
3. `ExplorationFootprint`'s derivazione (quali tile marcare visitate dalla
   storia delle posizioni del player) è più probabilmente **A3** (algoritmo
   puro, stesso genere di `WorldModelTemporalEnricher` in AP-01), non A2 —
   correzione rispetto a una prima ipotesi.
4. Manca ancora un contratto A1 per l'evidenza di esecuzione movimento
   (bridge di `WalkOutcome`/`MovementOutcome`/`StepGuardOutcome` verso una
   forma `WorldFact`-based) — necessario prima che un vero task A2 di
   "adapter" possa essere specificato con precisione.

Un agente di investigazione (read-only) è in corso su questi 4 punti.
Appena torna, questo file viene sostituito con la decisione presa e, se il
risultato lo permette, con la specifica precisa AP-04/A2 e/o A4 per
DeepSeek in un comando dedicato (stesso livello di dettaglio di
`docs/agents/phases/AP-03/AP-03_A4_CLAUDE_persistence_and_wiring.md`).

**Livello di verifica:** `Present` — contratti scritti, testati,
compilano puliti; non ancora `Integrated` in nessun ciclo runtime, perché
niente li produce/consuma ancora (previsto per AP-04/A2-A4).
