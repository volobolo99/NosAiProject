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
