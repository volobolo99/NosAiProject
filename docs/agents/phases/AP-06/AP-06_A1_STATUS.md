# AP-06 / A1 — Stato

**Avviato per la stessa eccezione dichiarata già usata per AP-04/A1 e
AP-05/A1** (`docs/agents/EXECUTION_QUEUE.md` "Criterio di avanzamento
fase"): questi contratti dipendono solo da `Quest`/`QuestObjective`/
`QuestObjectiveStatus`/`QuestId`/`EntityId`/`ItemId`/`WorldPosition`
(AP-01, già `Integrated`), non da AP-04/AP-05 ancora in corso.

## Ambito

`docs/ROADMAP_ESECUTIVA.md` S:AP-06: "OCR/UI/network evidence →
semantic extraction → Quest Graph → HTN/GOAP → execute → verify.
Supportare travel, dialogue, collect, kill, interact, deliver,
quantities, prerequisites, rewards e catene multi-step." A1 definisce
**solo le forme dati** per il Quest Graph (obiettivi tipizzati,
prerequisiti, reward) — la "semantic extraction" da OCR/UI/network resta
AP-06/A2 (bloccata dallo stesso gap OCR/ML già dichiarato in AP-02, non
riaffrontato qui), l'esecuzione HTN/GOAP resta un problema di
composizione con l'esecuzione già scoperta in AP-04 (`--scout`) e futura
in AP-05, non qualcosa che questo contratto decide.

## Perché contratti nuovi e non modifiche a `Quest`/`QuestObjective`

`Quest`/`QuestObjective` (AP-01, `src/NosAi.Core/WorldModel/QuestContracts.cs`)
sono già scritti, testati, **ma non ancora `Integrated`**: nessun codice
di produzione li costruisce ancora (confermato per grep: `new
QuestObjective(`/`new Quest(` compaiono solo nel loro stesso file di
test). Nonostante questo, li ho lasciati invariati invece di aggiungervi
campi: stesso principio già seguito in AP-04 (`MapModel`/`Tile` invariati,
`ExplorationFootprint` nuovo) e AP-05 (`Skill`/`Mob` invariati,
`CombatActionCandidate` nuovo) — AP-01 resta la forma minima pubblicata,
le fasi successive aggiungono ricchezza di dominio come contratti
compagni, non come modifiche silenziose a un contratto già dichiarato
`Integrated`-ready da un'altra fase.

## Consegnato

`src/NosAi.Core/WorldModel/Quests/QuestGraphContracts.cs`:

- `QuestObjectiveKind` — `Travel`/`Dialogue`/`Collect`/`Kill`/`Interact`/
  `Deliver`, i sei verbi della DoD.
- `QuestObjectiveTarget` — l'identità specifica per tipo di un obiettivo
  (posizione per Travel, NPC per Dialogue/Interact, item per Collect,
  specie mob per Kill, NPC+item per Deliver). Costruttore che valida
  esattamente la combinazione richiesta da ogni `Kind` (stesso
  trattamento di `CombatActionCandidate` in AP-05).
- `EnrichedQuestObjective` — accoppia un `QuestObjective` (AP-01,
  tracciamento progresso) con il suo `QuestObjectiveTarget` (AP-06,
  identità). Non modifica `QuestObjective`.
- `QuestRewardKind`/`QuestReward` — cosa si ottiene completando una
  quest (item/valuta/esperienza), validato allo stesso modo.
- `QuestNode`/`QuestGraph` — l'arco del grafo (prerequisiti come lista di
  `QuestId`) e l'insieme completo. Nome/obiettivi/progresso restano su
  `Quest` (AP-01); questo è solo il grafo e i reward sovrapposti.

`src/NosAi.Core/WorldModel/Quests/QuestGraphPlanner.cs` (A3, puro e
stateless, stesso stile di `ExplorationPlanner`/`CombatPlanner`):

- `IsStartable`/`GetStartableQuests` — una quest è avviabile solo se ogni
  prerequisito è **noto e confermato `Completed`** (un prerequisito mai
  osservato o con stato `Unknown` blocca, mai assunto soddisfatto per
  omissione) e la quest stessa non è già iniziata/completata/fallita.
- `IsObjectiveSatisfied` — completato per `Status` esplicito o per
  `CurrentCount >= RequiredCount`, entrambi già noti (mai per conteggi
  sconosciuti).
- `NextIncompleteObjective` — il primo obiettivo non soddisfatto
  nell'ordine dichiarato di `Quest.Objectives` (stessa convenzione
  ordine-come-sequenza di `NavigationPlan.Waypoints`/`ComboPlan.Steps`).

Nessuna delle due parti richiede dati inventati: sono pura logica su
fatti già noti (a differenza della simulazione di combattimento in
AP-05, qui non serve alcun numero di danno/costo per decidere se una
quest è avviabile o un obiettivo è soddisfatto).

## Test

`tests/NosAi.Core.Tests/WorldModel/Quests/QuestGraphContractsTests.cs`
+ `QuestGraphPlannerTests.cs`: 36 test, tutti verdi. `dotnet build
NosAi.sln -c Release`: 0 errori (1 warning preesistente non collegato).
`dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`:
**498/498**, 0 falliti (462 precedenti + 36 nuovi, zero regressioni).

## Deliberatamente non affrontato qui

- **Semantic extraction OCR/UI/network → `Quest`/`QuestObjective`/
  `QuestObjectiveTarget`** (AP-06/A2): stesso gap OCR/ML già dichiarato
  bloccato in AP-02 (`AP-02_STATUS.md` §10) — nessun modello/dataset
  addestrato esiste in questo repository. Non un problema di questa
  fase, non riaffrontato.
- **Esecuzione HTN/GOAP di un obiettivo scelto** (percorrere una
  `Travel`, parlare con un NPC, uccidere una specie, consegnare un
  item): comporrebbe con l'esecuzione movimento già scoperta in AP-04
  (`--scout`/`WalkCommand`) e con una futura esecuzione combattimento in
  AP-05 (non ancora specificata, vedi `AP-05_A1_STATUS.md`) — non
  qualcosa che i contratti A1 decidono, per lo stesso motivo per cui
  `NavigationPlan` non decide come si cammina cella per cella.

**Livello di verifica:** `Present` — contratti e algoritmo puro scritti,
testati, compilano puliti; non ancora `Integrated` in nessun ciclo
runtime, perché niente li produce/consuma ancora (né `Quest`/
`QuestObjective` stessi lo sono, a monte).
