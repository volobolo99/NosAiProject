# AP-08 / A1+A3 — Stato

**Avviato per la stessa eccezione dichiarata già usata per AP-04/05/06/07**
(`docs/agents/EXECUTION_QUEUE.md` "Criterio di avanzamento fase"): questi
contratti dipendono da `Goal`/`Player`/`CombatantStatus`/`Resource`
(AP-01, `Integrated`), `ExplorationFootprint` (AP-04, `Present`) e
`QuestGraph`/`QuestGraphPlanner` (AP-06, `Present`) — non da AP-05/AP-07,
i cui gap dati restano aperti.

## Ambito

`docs/ROADMAP_ESECUTIVA.md` S:AP-08: "Strategic Utility → HTN →
deterministic cost-aware GOAP → reactive rules → Guard/Trust/Safety.
Gestire survival, recovery, quest urgency, progression, farming,
exploration e optimization in modo contestuale. DoD: ogni azione live
deriva da un piano verificabile e attraversa l'unico execution path
autorizzato." Questo passaggio copre **solo la prima tappa** ("Strategic
Utility": quale contesto strategico è più urgente ora) — HTN/GOAP/
Guard/Trust/Safety restano compito di un passaggio successivo, quando i
gap di esecuzione già aperti in AP-04/AP-05 (vedi sotto) saranno risolti.

## Consegnato

`src/NosAi.Core/WorldModel/Strategy/StrategyContracts.cs`:

- `StrategicGoalKind` — i sette contesti della DoD, nello stesso ordine
  ("survival, recovery, quest urgency, progression, farming, exploration
  e optimization").
- `EnrichedGoal` — accoppia un `Goal` (AP-01, invariato) al suo contesto
  tipizzato, stesso principio di `EnrichedQuestObjective` (AP-06).
- `StrategicSignal` — urgenza grezza per un contesto, con motivo
  nominato. Stesso principio "input grezzo, non punteggio finale" di
  `FrontierCandidate`/`CombatSimulationResult`.
- `StrategicPlan` — quale `StrategicGoalKind` perseguire questo ciclo (o
  nessuno), stesso shape di `NavigationPlan`/`ComboPlan`
  (`Unselected(reason)` per "nessun segnale disponibile").

`src/NosAi.Core/WorldModel/Strategy/StrategyPlanner.cs` (A3, parziale e
onesto, puro/stateless):

- `AssessSurvivalUrgency(Player)` — reale, dalla frazione HP fusa
  (`CombatantStatus.Resources`, `ResourceKind.Health`, popolata da AP-02).
  `null` se la risorsa Health non è nota o current/maximum non sono
  entrambi noti — mai letta come "non urgente" per omissione.
- `AssessQuestUrgency(QuestGraph, EquatableArray<Quest>)` — reale,
  incrocia `QuestGraphPlanner.GetStartableQuests`/`NextIncompleteObjective`
  (AP-06). `null` solo se non si sa assolutamente nulla di alcuna quest.
- `AssessExplorationUrgency(ExplorationFootprint)` — reale, da
  `FullyExplored` (AP-04). `null` se non ancora noto.
- `SelectStrategicPlan` — il segnale con urgenza più alta, spareggio
  deterministico al primo (stesso stile di
  `CombatPlanner.SelectNextFrontier`).

## Perché solo 3 contesti su 7

- **Recovery**: per essere un segnale diverso da Survival servirebbe un
  fatto reale "sono in combattimento adesso" — assegnare un'urgenza qui
  solo da soglie HP duplicherebbe Survival sotto un altro nome. Non
  affrontato, non un contratto A1 mancante ma una scelta di scope onesta.
- **Progression, Farming, Optimization**: richiedono dati economici/di
  valore reali (valore del loot, efficienza di farming, valore di
  progressione di un upgrade) che non esistono da nessuna parte in
  questo repository — stesso genere di gap già trovato per le statistiche
  di combattimento in AP-05 e per gli item in AP-07. **Nessun metodo è
  stato scritto per questi tre contesti**: un metodo che ritorna sempre
  zero sarebbe indistinguibile da un segnale reale a valle, e sarebbe
  esattamente il tipo di logica fittizia vietata — l'assenza del metodo è
  la scelta onesta, non un `TODO`.

## Test

`tests/NosAi.Core.Tests/WorldModel/Strategy/StrategyContractsTests.cs` +
`StrategyPlannerTests.cs`: 19 test, tutti verdi. `dotnet build NosAi.sln -c Release`:
0 errori (1 warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **544/544**,
0 falliti (525 precedenti + 19 nuovi, zero regressioni).

## Deliberatamente non affrontato qui

- **HTN/GOAP** (decomposizione task, azioni con precondizioni/effetti/
  costo): la DoD li nomina esplicitamente come tappe successive a
  "Strategic Utility". Costruirli ora, prima che i gap di esecuzione di
  AP-05 (simulazione combattimento) e AP-07 (candidati Equip) siano
  risolti, rischierebbe di fissare un'architettura sbagliata da disfare
  poi — stesso principio già applicato per non aver forzato AP-04/AP-05
  dentro `Gate3Runtime`.
- **"L'unico execution path autorizzato"**: `--scout` (AP-04) ha già
  risolto questo per il movimento con `ActuationAuthority.Commanded`
  verso `WalkCommand.Execute` reale. Per gli altri contesti (combattimento,
  quest, equip) la stessa domanda resta aperta esattamente come descritto
  in `AP-05_A1_STATUS.md`/`AP-07_A1_STATUS.md` — un HTN/GOAP reale
  dovrebbe comporsi con queste risposte dominio per dominio, non
  inventarne una generica qui.

**Livello di verifica:** `Present` — contratti e algoritmo parziale
scritti, testati, compilano puliti; non ancora `Integrated` in nessun
ciclo runtime.

## AP-08/A2+A4 — indagine mirata

Trovato, confermato per ispezione: **una risposta reale a Survival
esiste già come primitiva**, `CombatActionKind.UseConsumable`
(AP-05, `CombatContracts.cs`) esegue via lo stesso meccanismo di
`--engage` — `InputActionEffector.cs` riga 249-251 preme
`consumable.{slot}` per numero di slot (non id oggetto, che resta
sconosciuto — stessa scelta onesta di `--engage` per lo skill id).
Verificarne l'effetto richiederebbe però un confronto **opposto** a
`CombatVerificationProjector` (HP che *sale*, non MP che scende): non un
nuovo meccanismo, ma un'estensione dei contratti AP-05 esistenti — un
eventuale `--recover <slot>` è quindi lavoro di AP-05, non nuova
infrastruttura AP-08.

**Il vero gap di AP-08** è un altro: nessun orchestratore esiste che
legga `StrategyPlanner.SelectStrategicPlan` e scelga/avvii di conseguenza
`--scout`/`--walk`/`--collect`/un futuro `--recover`. È la prima
componente di questo progetto che **sceglierebbe** un'azione invece di
eseguire un atto che l'operatore nomina — una classe di rischio diversa
da ogni comando consegnato finora (`--scout`/`--engage`/`--collect`
eseguono solo ciò che l'operatore chiede). Non la specifico né la avvio
senza una decisione esplicita dell'utente su ambito e cautele.
