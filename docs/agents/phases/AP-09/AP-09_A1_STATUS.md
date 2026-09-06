# AP-09 / A1+A3 — Stato

## Scoperta preliminare — non ripetere il lavoro già fatto

A differenza di AP-04→AP-08, questa fase **non parte da zero**.
`src/NosAi.Core/Memory/` e `src/NosAi.Core/Knowledge/` esistono già,
sono reali e testati, presenti fin dal primo commit dell'attuale `main`
(non scritti in questa sessione, non parte del lavoro AP-00→AP-08):

- `Memory/AdaptiveKnowledgeContracts.cs` — `KnowledgeScope` (i 7 valori
  esatti della DoD: Universal/Progression/Class/Specialist/Context/
  Character/Environment), `KnowledgeStatus` (i 7 stati esatti del
  lifecycle: Discovered/Candidate/Testing/Promising/Validated/Verified/
  Deprecated), `KnowledgeEntry`, `IAdaptiveKnowledgeStore`,
  `KnowledgePathPolicy` (percorso file sicuro, verificato contro path
  escape).
- `Memory/FileSystemAdaptiveKnowledgeStore.cs` — store reale su
  filesystem, scrittura atomica (file temp + move), query per
  scope/topic.
- `Memory/InMemoryStore.cs` + `Memory/MemoryTypes.cs` — `MemoryRecord`/
  `IMemoryStore`, rifiuta un record con provenance `Unknown` a meno che
  non sia esplicitamente `Reasoning` (stesso principio "Unknown non è
  mai comodo" già visto ovunque in questo progetto).
- `Knowledge/KnowledgeIngestionContracts.cs` +
  `AdaptiveKnowledgeIngestionEngine.cs` — **reale confine non
  privilegiato applicato al livello della memoria**: `ForbiddenMarkers`
  rifiuta esplicitamente fonti che menzionano "gm/admin/server
  database/packet injection/exploit/hack/dupe/bot" prima ancora che una
  candidate knowledge venga salvata. `EvidenceKnowledgeValidator`
  promuove `Candidate → Tested → Validated → Verified` solo con evidenza
  osservata indipendentemente e confidence misurata — esattamente il
  meccanismo "solo l'evidenza osservabile nel client/test environment
  può promuovere a conoscenza verificata" della DoD.
- `Knowledge/KnowledgeStrategyBridge.cs` — `AdaptiveStrategyMemory`
  interroga lo store per obiettivo, filtra solo `Validated`/`Verified`,
  ordina per priorità/confidence. Un consumatore reale esiste già
  (`NosAi.Core.Progression.KnowledgeMissionStrategyAdapter`).

Tutto testato: `tests/NosAi.Core.Tests/Memory/AdaptiveKnowledgeStoreTests.cs`,
`AdaptiveKnowledgeIngestionEngineTests.cs`,
`Progression/KnowledgeMissionStrategyAdapterTests.cs`. **Non ancora
cablato in `NosAi.Runtime`/`Program.cs`** (stesso stato di "Present" del
resto del lavoro AP-00→AP-08: nessun ciclo runtime lo consuma ancora) e
**completamente disconnesso** da `NosAi.Core.WorldModel.*` (nessun
riferimento in nessuna delle due direzioni, prima di questo passaggio).

## Un problema reale già presente, segnalato e non toccato

`KnowledgeScope` è **duplicato**: dichiarato identicamente sia in
`NosAi.Core.Memory` che in `NosAi.Core.Knowledge` (stessi 7 valori, due
tipi distinti), con una funzione di mappatura manuale
(`AdaptiveKnowledgeIngestionEngine.MapScope`,
`KnowledgeCandidateStrategyProjector.MapScope`) per convertire tra le
due. Il lifecycle è **frammentato in due state machine diverse**:
`Memory.KnowledgeStatus` (7 stati, quelli della DoD) vs
`Knowledge.KnowledgeLifecycle` (7 stati ma **diversi**: Candidate/
Tested/Validated/Verified/RevalidationRequired/Deprecated/Forbidden —
manca "Discovered"/"Testing"/"Promising", aggiunge
"RevalidationRequired"/"Forbidden"). Stessa natura del problema
`DataSourceKind` già segnalato da AP-01/A5 (triplicazione tra
`NosAi.Runtime.Contracts`, `NosAi.Core.Hardware`, `NosAi.Core.WorldModel`)
— **non risolto qui**: sistemare due enum/lifecycle già in uso da codice
e test reali senza un comando dedicato rischierebbe una rottura non
richiesta ("non fare refactoring ampio di codice non correlato").
Segnalato in `docs/agents/DEEPSEEK_TASKS.md` come candidato di
riconciliazione futura.

## Consegnato in questo passaggio (estensione additiva, non duplicazione)

`src/NosAi.Core/Memory/MemoryTypes.cs` — `MemoryType` estesa da 5 a 10
valori: la DoD ne nomina dieci ("Working, episodic, semantic,
procedural, spatial, combat, quest, character, failure e reasoning
memory"), il tipo esistente ne aveva cinque
(Working/Episodic/Semantic/Procedural/Reasoning). Aggiunti
`Spatial`/`Combat`/`Quest`/`Character`/`Failure` **in coda**, non
nell'ordine della DoD, per non rinumerare i cinque valori già esistenti
(enum a base `byte`, nessun uso di uno switch esaustivo trovato per
grep, ma prudenza gratuita costa zero). Nessuno switch esaustivo su
`MemoryType` esiste nel repository (verificato per grep).

`src/NosAi.Core/Memory/ActionOutcomeLedger.cs` (nuovo, colma un gap
reale non coperto dal codice esistente):

- `ActionOutcomeLedgerEntry` — collega un `WorldAction` (AP-01) a una
  categoria di memoria e un `Context` libero (chiave stabile per
  raggruppare "questo genere di situazione": un id skill, un tipo
  obiettivo quest, una specie di mob). Colma la voce DoD "Action-outcome
  ledger", che il codice esistente non copriva affatto.
- `LocalOutcomePrediction`/`LocalOutcomeSimulator.Predict` — colma
  "Simulazione deterministica locale per conseguenze a breve termine"
  **onestamente**: conta gli esiti storici realmente registrati per un
  `Context` (successi/fallimenti/in corso) e calcola un tasso di
  successo solo sulla storia risolta. Un ledger vuoto per un contesto
  ritorna conteggi a zero e **`SuccessRate = null`**, mai un tasso
  inventato (0% e "nessuna storia" sono fatti diversi, stessa disciplina
  di ogni `WorldFact<T>.HasValue` in questo progetto). Nessun numero
  fabbricato: è pura conta di frequenza su storia reale, non una formula
  di danno/costo inventata come quelle correttamente rifiutate in AP-05/
  AP-07.

## Test

`tests/NosAi.Core.Tests/Memory/ActionOutcomeLedgerTests.cs`: 11 test,
tutti verdi (6 sull'estensione di `MemoryType`, 5 su
`LocalOutcomeSimulator`). `dotnet build NosAi.sln -c Release`: 0 errori
(1 warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **555/555**,
0 falliti (544 precedenti + 11 nuovi, zero regressioni — inclusi tutti i
test preesistenti di Memory/Knowledge/Progression, verificati
esplicitamente verdi prima di procedere).

## Deliberatamente non affrontato qui

- **Riconciliazione `KnowledgeScope`/lifecycle duplicati**: segnalato,
  non risolto (vedi sopra).
- **Collegamento tra `NosAi.Core.WorldModel.*` (AP-01→AP-08) e
  `NosAi.Core.Memory`/`Knowledge`**: restano due isole. Un vero
  "Working/Episodic/Semantic/... memory" della DoD dovrebbe
  probabilmente leggere/scrivere `WorldModelSnapshot`/`Quest`/
  `CombatActionCandidate` ecc., non solo `WorldAction`. Il ledger appena
  aggiunto è il primo ponte (via `ActionId`/`ActionOutcome`), non
  l'integrazione completa.
- **Persistenza del ledger tra cicli/sessioni**: `LocalOutcomeSimulator`
  è puro, non possiede uno store. Cablarlo (SQLite, stesso genere di
  `MapModelStore` in AP-03) è un passaggio A4 futuro, non affrontato qui.
- **`Adaptive Knowledge Expansion` (ereditarietà per classe)**: la DoD
  descrive conoscenze di classe ereditate da nuovi personaggi della
  stessa classe. Nessun concetto di "classe personaggio" esiste in
  `NosAi.Core.WorldModel` — stesso genere di gap dati già visto per le
  statistiche skill/item in AP-05/AP-07. Non affrontato, segnalato.

**Livello di verifica:** `Present` per il nuovo contenuto (ledger +
simulazione), stesso di ogni altra fase; il codice Knowledge/Memory
preesistente resta al proprio livello già stabilito (testato, non
cablato nel runtime).

## AP-09/A2+A4 — indagine mirata

**Persistenza**: pattern reale e riusabile in modo additivo.
`src/NosAi.Storage/MapModelStore.cs` (AP-03) apre una connessione SQLite
propria via `VolumeLocator`/`SqliteJournalOptions`, verifica
`journal_mode`/`synchronous`/`busy_timeout` per lettura, non per
assunzione (righe 173-189), crea una propria tabella dedicata
(`map_models`, righe 165-171) e converte il dominio tramite DTO privati
simmetrici in entrambe le direzioni (righe 218-324) — nessuna tabella
condivisa, nessuna dipendenza dal journal Gate 1
(`SqliteEventJournal`/`IEventJournal`, che serve solo le 11
`PipelineStage` del percorso critico, `src/NosAi.Core/PipelineStage.cs:14-27`,
non un ledger generico). Lo stesso schema si applica **additivamente** a
`ActionOutcomeLedgerEntry`: una classe nuova nello stesso progetto, una
tabella dedicata nuova (es. `action_outcome_ledger`, chiave `EntryId`),
senza nemmeno lo strato DTO che `MapModelStore` ha dovuto costruire per
`EquatableArray<T>`/`WorldFact<T>` — `ActionOutcomeLedgerEntry` non
contiene né l'uno né l'altro (`Guid`/`ActionId`/`MemoryType`/
`ActionOutcome`/`string`/`DateTime`, `ActionOutcomeLedger.cs:27-33`).
Nessun meccanismo nuovo va inventato qui.

**Registrazione automatica**: bloccata, per un fatto verificato per
grep, non per assunzione. `new WorldAction(` non compare mai in `src/`
— solo in `tests/NosAi.Core.Tests/WorldModel/PlanningContractsTests.cs:13,21`.
Nessuno dei cinque comandi (`EngageCommand`, `RecoverCommand`,
`AutoplayCommand`, `CollectCommand`, `ScoutCommand`) costruisce un
`WorldAction`. Ciò che producono davvero, dopo ogni round, sono due tipi
di evidenza propri e diversi da `WorldAction`: `CombatExecutionEvidence`
+ `CombatExecutionResult` (`EngageCommand`/`RecoverCommand`/
`AutoplayCommand`-Survival, `CombatExecutionContracts.cs:86-93`) e
`MovementExecutionEvidence` + `MovementExecutionResult`
(`ScoutCommand`/`CollectCommand`/`AutoplayCommand`-Exploration,
`MovementExecutionContracts.cs:13-28,51-57`). `ActionId` — "Identity of
one recorded `WorldAction`" (`Identifiers.cs:107`) — non ha quindi mai un
`WorldAction` reale a cui riferirsi in nessun ciclo runtime oggi.

**La correlazione esiste, ma è parziale in modo strutturale, non solo
assente per pigrizia.** `CombatVerificationProjector.Project`/
`ProjectRecovery` (righe 61-98, 122-159) confermano
`CombatExecutionResult.ResourceCostConfirmed`/`ResourceGainConfirmed`
solo da una lettura vitali prima/dopo realmente cambiata — un mapping
onesto verso `ActionOutcome.Succeeded` esiste. `NoResourceChangeObserved`
(entrambe le letture arrivate, risorsa immobile, righe 93-95/154-156) è
difendibile come `ActionOutcome.Failed`. Ma `CombatExecutionResult.Unobserved`
(righe 70-77, 79-86, 130-137, 139-146: risorsa non osservabile per questo
`Kind`, o lettura vitali mancante) e `.Aborted` (guard/keybind/gate hanno
rifiutato l'atto prima ancora che partisse) non hanno **nessuna**
destinazione onesta in `ActionOutcome`: un enum di sole tre voci
(`InProgress`/`Succeeded`/`Failed`, `PlanningContracts.cs:13-18`)
dichiaratamente privo di "Unknown" **per costruzione**, perché quella
voce dovrebbe vivere nel wrapper — `WorldAction.Outcome` è infatti
`WorldFact<ActionOutcome>` (`PlanningContracts.cs:31`), non
`ActionOutcome` nudo. `ActionOutcomeLedgerEntry.Outcome`, invece, è
`ActionOutcome` nudo (`ActionOutcomeLedger.cs:31`) — la stessa via di
fuga che `WorldAction` si è data non è disponibile qui. E `Aborted`/
`Unobserved` non sono casi rari da trascurare: sono esattamente ciò che
`EngageCommand` documenta oggi come risultato sul gate di produzione
armato (`EngageCommand.cs:55-58`, "a skill key press has no target
pixel... refuses with the commit point's scope-required reason"), e
`EngageCommand`/`RecoverCommand` già trattano `Aborted` come categoria
propria con un codice di uscita diverso da successo/fallimento
(`EngageCommand.cs:268-272`, `RecoverCommand.cs:278-282`) — coerente con
l'idea che non sia nemmeno un'azione da registrare come "fallita". Lo
stesso scollamento vale, identico, per
`MovementExecutionResult.Unobserved`/`.Aborted`
(`ScoutCommand`/`CollectCommand`).

**Conclusione**: A2+A4 è genuinamente bloccato, non per assenza di
wiring ma per un gap nel contratto stesso di AP-09/A1, su due punti
distinti: (1) nessun produttore di `WorldAction` esiste in nessun ciclo
runtime — un `ActionId` reale richiede un `WorldAction` reale, non un id
sintetizzato ad hoc dentro un comando che nessun'altra parte del World
Model legge o scrive mai; (2) anche accantonando (1),
`ActionOutcomeLedgerEntry.Outcome` non ha spazio per rappresentare
onestamente `Unobserved`/`Aborted`, che sono l'esito osservato oggi sulla
maggioranza dei round reali (gate di produzione armato, nessuna arma
input). Nessuna delle due è una decisione di wiring che un A2+A4 possa
prendere da solo: sono estensioni del contratto fondante di
`ActionOutcomeLedgerEntry`/`WorldAction` (bridging
`CombatExecutionResult`/`MovementExecutionResult` verso `ActionOutcome`,
ed eventualmente wrappare `Outcome` in `WorldFact<ActionOutcome>` come
già fa `WorldAction`) — compito di Claude prima che un A2+A4 sia
scrivibile, non avviato qui.

## Correzione di contratto (Claude, questo passaggio)

Applicate entrambe le estensioni richieste dall'indagine sopra, in
`NosAi.Core` (nessuna dipendenza nuova, nessun wiring runtime):

- `Memory/ActionOutcomeLedger.cs` — `ActionOutcomeLedgerEntry.Outcome` è
  ora `WorldFact<ActionOutcome>` (era `ActionOutcome` nudo): un esito mai
  osservato si rappresenta con `WorldFact<ActionOutcome>.Unknown(reason)`,
  mai forzato in `Failed`. `LocalOutcomePrediction` ha un nuovo campo
  `UnknownCount`; `LocalOutcomeSimulator.Predict` conta un `Outcome` con
  `HasValue == false` solo lì, mai nel totale assestato né in
  `InProgressCount`.
- `WorldModel/WorldActionProjector.cs` (nuovo) — il produttore reale di
  `WorldAction` che mancava: `FromCombat`/`FromMovement` proiettano
  `CombatExecutionEvidence`/`MovementExecutionEvidence` (già prodotte da
  `--engage`/`--recover`/`--autoplay`/`--scout`/`--collect`) in un
  `WorldAction` vero; `ToLedgerEntry` lo registra nel ledger senza
  ri-derivare l'esito. Mappatura dichiarata esplicitamente nei commenti:
  cambio risorsa confermato → `Succeeded`; nessun cambio osservato →
  `Failed`; `Unobserved`/`Aborted` → `Unknown`, mai indovinato.

Non affrontato qui, resta A2+A4 vero: nessun comando chiama ancora
`WorldActionProjector`, nessuno store persiste `ActionOutcomeLedgerEntry`
(pattern `MapModelStore` riusabile, vedi indagine sopra) — il contratto è
ora scrivibile, la specifica DeepSeek non è stata scritta in questo
passaggio.

**Test**: `tests/NosAi.Core.Tests/WorldModel/WorldActionProjectorTests.cs`
(19 test, nuovo) + 1 test aggiunto a `ActionOutcomeLedgerTests.cs`
(`Predict_UnknownOutcomeEntries_CountedSeparately_NeverAsSettledOrInProgress`).
`dotnet build NosAi.sln -c Release`: 0 errori, 0 warning nuovi. `dotnet
test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`:
**600/600**, 0 falliti (580 precedenti + 20 nuovi, zero regressioni).
`dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c
Release`: **2018/2076**, 0 falliti, invariato (nessun consumatore in
`NosAi.Runtime` di questi due tipi ancora, come atteso).

**Livello di verifica**: `Present` — contratto esteso, testato, compila
pulito; non ancora `Integrated` (nessun chiamante runtime).

## A2+A4 (Q-061, DeepSeek) — consegna + audit A5 indipendente

Consegnato da DeepSeek (commit `fedda0c`), esattamente secondo la
specifica (`AP-09_A2A4_DEEPSEEK_ledger_wiring.md`): `ActionOutcomeLedgerStore`
(nuovo, `NosAi.Storage`, persistenza append-only stile `MapModelStore`,
nessun layer DTO, `TryOpenFromVolume` sul modello di
`CollectCommand.LiveScope.TryOpen`), `ActionOutcomeRecorder` (nuovo,
`NosAi.Runtime.WorldModel.Fusion`, no-op documentato su store `null`),
wiring chirurgico in `EngageCommand`/`RecoverCommand`/`ScoutCommand`/
`AutoplayCommand` — solo nei rispettivi `RunWindows`, nessuna firma di
`ExecuteOneRound`/`ExecuteOneCycle` toccata, `CollectCommand.cs` non
sfiorato.

**Audit indipendente (Claude, A5)**: rilettura riga per riga di tutti gli
8 file diff-ati contro la specifica, poi build/test rieseguiti in un
worktree isolato puntato su `origin/main` (mai fidandosi della consegna).
**Nessun difetto reale trovato** — la prima consegna DeepSeek di questa
sessione a passare un audit senza correzioni necessarie, oltre a
`--recover`. Verificato esplicitamente:

- `ActionOutcomeLedgerStore.Append`/`ReadEntry` leggono ogni colonna per
  nome (`reader.GetOrdinal("...")`), mai per indice posizionale, come
  richiesto — nessun rischio di disallineamento colonne silenzioso.
- Round-trip di un `Outcome` sconosciuto (`WorldFact<ActionOutcome>.Unknown`)
  testato esplicitamente e verificato: `HasObservedValue` torna `false`,
  `Reason` preservato, nessun valore `ActionOutcome` fabbricato — il caso
  esatto per cui la correzione di contratto Q-060 esiste.
- Registrazione mai bloccante: `TryOpenFromVolume` cattura solo
  `InvalidOperationException` (volume non montato) e stampa `[WARN]`,
  mai un'eccezione non gestita; `ActionOutcomeRecorder.RecordCombat`/
  `RecordMovement` con `store == null` è un no-op verificato da test
  dedicato (nessun file creato).
- `AutoplayCommand`: `evidence.Candidate` riusato direttamente (mai
  ricostruito) per registrare l'atto Survival dispatchato — prova che
  l'entry registrata è esattamente l'atto premuto, non una seconda
  congettura indipendente. Nessun nuovo campo aggiunto a
  `AutoplayCycleResult`.
- Test: `ActionOutcomeLedgerStoreTests` (5, incluso il round-trip Unknown,
  ordinamento/filtro per contesto case-sensitive, policy WAL verificata)
  e `ActionOutcomeRecorderTests` (4, valore atteso calcolato via
  `WorldActionProjector` stesso, mai ri-derivato indipendentemente) —
  esattamente la disciplina richiesta dalla specifica.

**Nota minore, non bloccante**: `RecordedAtUtc`/`issuedAtUtc` in tutti e
quattro i siti di chiamata riusano il timestamp di inizio round/ciclo
(`nowUtc`/`now`), non l'istante effettivo in cui `store.Append` gira —
scelta della specifica stessa (non una deviazione DeepSeek), quindi
`RecordedAtUtc` può essere indietro di quanto dura la finestra di
verifica (~350ms per gli atti combattimento). `Outcome.ObservedAtUtc`
(il timestamp che conta per la semantica dell'evidenza) resta invece
accurato, perché viene dall'evidenza stessa via `WorldActionProjector`.
Non corretto qui: bookkeeping, non correttezza.

**Evidenza build/test (indipendente, worktree isolato su `origin/main`)**:
```
dotnet build NosAi.sln -c Release → 0 Errori, 1 Warning preesistente non collegato.
dotnet test .../NosAi.Core.Tests.csproj --filter "~ActionOutcomeLedgerStoreTests" → 5/5
dotnet test .../NosAi.Runtime.Tests.csproj --filter "~ActionOutcomeRecorderTests" → 4/4
dotnet test .../NosAi.Core.Tests.csproj → 605/605 (un fallimento isolato in
  TransportLoopTests alla prima esecuzione, non riprodotto alla riesecuzione —
  stesso flake da carico macchina già documentato più volte in questa sessione,
  non collegato a questa consegna)
dotnet test .../NosAi.Runtime.Tests.csproj → 2022/2080, 0 falliti, 58 skip
```

**Livello di verifica — AP-09/A2+A4**: `Integrated` — compila pulito,
tutti i test combinati passano, wiring reale nei quattro comandi già
integrati. Non `Verified`: nessun percorso di lettura wired (per scelta
dichiarata nella specifica), nessuna sessione reale ha ancora prodotto
righe in `action_outcome_ledger` su un client NosTale vero.
