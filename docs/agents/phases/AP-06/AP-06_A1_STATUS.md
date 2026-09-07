# AP-06 / A1 — Stato

> **Nota di revisione, 2026-09-07.** Questo documento registra lo stato
> al momento in cui fu scritto e alcune sue affermazioni sono state
> superate dal codice. Verificato oggi nel sorgente: `CollectCommand` esiste da `src/NosAi.Runtime/Navigation/CollectCommand.cs:53`, con il flag registrato in `Program.cs`.
>
> **Aggiunta 2026-09-08.** Le due righe che dichiarano la *semantic
> extraction* bloccata dal gap OCR/ML (§ Ambito e § Deliberatamente non
> affrontato qui) sono superate per la metà che riguarda il **testo**: la
> premessa è giusta — nessun opcode del filo porta il testo di missione o
> di dialogo — ma il testo sta nei file del client, in italiano, ed è
> importato dal 2026-09-07 (Q-129): `quest` e `npctalk` sono fra le
> `ReferenceImporter.TextOnlyTables` (`ReferenceImporter.cs:134-135`), con
> 3 639 testi di missione e 22 358 battute di NPC misurati. Resta bloccato
> dal gap OCR/ML solo ciò che dipende da un modello addestrato — nessun
> `.onnx` e nessun `data/perception/glyphs.atlas` esistono nel repository
> (`git ls-files`) — e resta aperto il collegamento id sul filo → chiave
> di tabella, che non è un problema di OCR.
>
> Le righe qui sotto restano com'erano: sono un registro, non una
> descrizione del presente.

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

## AP-06/A2+A4 — indagine mirata, decisione, algoritmo e specifica DeepSeek

Stessa disciplina "investigate before speccing" già usata per AP-04
(`--scout`) e AP-05 (`--engage`): prima di dichiarare A2 bloccato per
intero sul gap OCR/ML (come la sezione precedente assumeva), è stata
condotta un'indagine mirata riga per riga invece di assumere.

**Trovato, non assunto:** la DoD di AP-06 dice "OCR/UI/**network**
evidence" — tre fonti, non una sola. Il canale rete per `Collect` esiste
già, reale e decodificato, **indipendentemente da qualunque modello
ML**:

- `NosTaleWorldProtocolDecoder.cs` decodifica già i pacchetti `ivn`
  (contenuto slot inventario), `get` (raccolta oggetto a terra) e `drop`
  (oggetto a terra visibile) in `InventorySlotReading`/`ItemPickup`/
  `GroundItem` (`src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`).
- `GameplayObservationProjector.Project` (AP-01/A2, **già `Integrated`**)
  fonde già questi pacchetti in `Player.Inventory: EquatableArray<InventoryItem>`
  e `WorldModelSnapshot.Drops: EquatableArray<Drop>`, con la stessa
  convenzione `ItemId(vnum.ToString())` usata ovunque nel progetto.
  Questo NON è un gap da chiudere per `Collect`: è già chiuso, da
  un'altra fase, e questa sezione lo usa senza rifarlo.
- `LiveObservationGateway.Capture()` (`src/NosAi.Runtime/LiveIntegration/LiveObservationGateway.cs`)
  dà una lettura live "un colpo solo" (nessun loop di polling nascosto),
  esattamente lo stesso ruolo di `ClientMemorySession.TryReadPlayerVitals`
  per AP-05/A2+A4 — componibile prima/dopo un'esecuzione.

**Cosa resta bloccato, confermato non assunto:**

- **Travel**: non serve nulla di nuovo. `WalkCommand.Execute`/`--walk`
  (già reale, Gate-1-verificato) esegue già esattamente "vai a una
  posizione" — un obiettivo `Travel` compone con l'esecuzione esistente
  senza scrivere altro codice.
- **Kill**: bloccato due volte, non una sola. Oltre al gap HP-mob già
  noto (AP-05), `GameplayObservationProjector`'s stesso commento dice
  che `WorldModelSnapshot.Mobs`/`Npcs` restano vuoti perché "un vnum di
  rete non si può distinguere da solo tra mostro e NPC senza un
  catalogo di riferimento" — quindi anche solo *identificare* il
  bersaglio di un obiettivo Kill dal World Model canonico è bloccato,
  non solo verificarne l'esito.
- **Dialogue/Interact/Deliver**: nessuna primitiva di esecuzione esiste
  (nessun intento tastiera "interact" in `KeybindsCheck.RuntimeIntentPrefixes`,
  nessun pacchetto di dialogo decodificato in `ProtocolMap`/
  `NosTaleWorldProtocolDecoder`) e nessun canale di verifica (serve
  leggere il testo di una finestra di dialogo, OCR bloccato). Restano
  `Present`/non specificati.

**Consegnato ora (A2, Claude — nessun lavoro DeepSeek necessario per
questo pezzo, i dati sono già fusi da un'altra fase):**

`src/NosAi.Core/WorldModel/Quests/QuestGraphPlanner.cs`,
`AssessCollectProgress(QuestObjectiveTarget, EquatableArray<InventoryItem>, DateTime) -> WorldFact<int>`
— il conteggio osservato per un obiettivo `Collect`, dal `Player.Inventory`
già fuso. Disciplina "unknown non è zero" applicata con un ragionamento
esplicito e non ovvio (documentato nel proprio commento XML): un
inventario vuoto è genuinamente ambiguo in questo World Model
(`EquatableArray<InventoryItem>` non è nullable, quindi "mai osservato"
e "confermato vuoto" collassano nella stessa rappresentazione un
livello sopra, in `GameplayObservationProjector` stesso) e quindi
restituisce `Unknown`, non zero; un inventario non vuoto che
semplicemente non elenca l'item richiesto è invece un caso diverso e
non ambiguo (il canale è confermato vivo) e restituisce zero **noto**.
Somma tra slot multipli dello stesso item; qualunque slot corrispondente
con quantità `Unknown` rende il totale `Unknown` (mai una somma
parziale spacciata per completa).

Test: `tests/NosAi.Core.Tests/WorldModel/Quests/QuestGraphPlannerTests.cs`
(6 test nuovi, tutti verdi). `dotnet build NosAi.sln -c Release`: 0
errori (1 warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **579/579**,
0 falliti (573 precedenti + 6 nuovi, zero regressioni).

**Specifica DeepSeek scritta (A4)**:
`docs/agents/phases/AP-06/AP-06_A2A4_DEEPSEEK_collect_command.md` —
comando operatore `--collect <x> <y> <vnum> [<requiredCount>]`: cammina
verso la posizione data (`WalkCommand.Execute`, riusato invariato,
`ActuationAuthority.Commanded`), legge l'inventario reale prima/dopo
con `LiveObservationGateway.Capture()` + `GameplayObservationProjector`
(entrambi già reali e `Integrated`), confronta con
`QuestGraphPlanner.AssessCollectProgress`. Ambito volutamente ristretto
allo stesso modo di `--engage`: l'operatore nomina posizione e item
direttamente, nessuna scoperta automatica del `GroundItem` più vicino
(richiederebbe un algoritmo di selezione non ancora scritto — segnalato
come estensione futura, non un blocco).

**Livello di verifica per questo passaggio:** `Present` — indagine
conclusa, decisione presa, algoritmo A2 scritto/testato, specifica
DeepSeek A4 completa e precisa. Non ancora `Integrated`: `CollectCommand`
non è stato ancora scritto (compito DeepSeek, A4).
