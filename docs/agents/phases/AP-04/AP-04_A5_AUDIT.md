# AP-04 — Exploration & Navigation — Audit A5 (Claude, indipendente)

**Autore:** A5 (Claude), audit indipendente (agente in background), su
`--scout` (Q-028/Q-029, DeepSeek). Nessun file di produzione toccato in
questo audit — solo lettura, build, test; la correzione trovata è
applicata separatamente in A6 (vedi `AP-04_STATUS.md`).
**Data:** 2026-09-07.
**Ambito:** verifica indipendente della consegna DeepSeek
`MovementVerificationProjector.cs`/`ScoutCommand.cs` (commit `1891e7d`,
`d011815`, `7080fcd`, su `origin/main`) contro la specifica
`AP-04_A2A4_DEEPSEEK_scout_command.md`, con lo stesso metodo già usato per
AP-01/A5, AP-02/A5, AP-03/A5: verifica empirica contro il runtime .NET
reale, mai fiducia nel self-report di un agente precedente.

**Nota sullo stato dell'albero al momento dell'audit:** la consegna
DeepSeek è arrivata direttamente su `main` (non sul branch di lavoro di
Claude); l'audit ha lavorato su un worktree isolato puntato a
`origin/main` (`7f63c57`), ricompilato e ritestato lì, poi rimosso il
worktree. Tutti i riferimenti file:riga sotto sono contro quell'albero,
identico come percorsi relativi al repository di lavoro.

## 1. Difetto reale trovato

### Difetto 1 — `onStepVerified` viene invocato anche per un passo mai emesso, contraddicendo il proprio contratto dichiarato

**Severità: Media** (nessun consumatore di produzione esiste ancora oltre
la stampa a console di `ScoutCommand`, ma è una violazione diretta e
riproducibile della garanzia che il parametro stesso dichiara).

**File/riga esatti (prima della correzione):**
`src/NosAi.Runtime/Navigation/WalkCommand.cs:333`, nel caso
`WalkOutcome.Stepping` di `WalkCommand.Execute`:
```csharp
MovementVerification verification = report.Verification;
controller.NoteStepOutcome(in verification);
onStepVerified?.Invoke(to, verification);   // riga 333 — incondizionato

if (report.Emitted) { ... }
else { return Finish(ExitGuardRefused, ...); }
```
`onStepVerified` viene invocato **prima** del controllo `report.Emitted`,
quindi scatta anche quando nulla è stato emesso (un rifiuto del guard,
finestra di sessione assente, geometria sconosciuta, rifiuto di scope o
di click — ogni percorso `NotEmitted(...)` in `SingleStepExecutor.Step`
imposta `Emitted=false` e `Verification = MovementVerification.NotAttempted(reason)`,
cioè `Outcome=Aborted`).

Questo contraddice tre dichiarazioni esplicite e concordanti dello stesso
contratto: il commento XML del parametro `onStepVerified` su
`WalkCommand.Execute` stesso ("A step a guard refused never reaches this
callback — nothing was emitted"), il commento XML di `onEvidence` su
`ScoutCommand.ExecuteOneRound` ("Never called for a step that was never
emitted (a guard refusal ends the round before any step)"), e il testo
della specifica stessa (§2a).

**Perché è raggiungibile, non ipotetico:** `PathWalkController.Next`
decide `WalkOutcome.Stepping` solo da geometria/occupazione statica
rivalidata — non consulta mai la guard chain live (autorità di sessione,
rilevamento input umano, policy di sicurezza). Quelle si controllano solo
quando `executor.Step` chiama `_guards.Authorize`. Lo scenario che
l'intera architettura di guardia esiste per gestire — la mano
dell'operatore tocca il mouse a metà cammino, o l'autorità di sessione
scade tra una `Next()` e la `Step()` successiva — produce esattamente
`WalkOutcome.Stepping` seguito da un rifiuto guard: è lo scenario
primario di interruzione di sicurezza, non un caso limite.

**Copertura di test che confermava l'assenza di verifica su questo
punto:** l'unico caso guard-adiacente in `ScoutCommandTests.cs`
(`UnreachableDestination_WalkReturnsNoPath_AndNoStepIsEverEmitted`) copre
il percorso a lunghezza zero (non raggiunge mai `WalkOutcome.Stepping`).
Nessun test in `WalkCommandTests.cs` (non toccato da questa consegna) o
in `ScoutCommandTests.cs` esercitava un rifiuto guard **a metà cammino**
(dopo almeno un passo riuscito), quindi questo difetto di ordinamento
aveva copertura zero in entrambe le direzioni.

**Nota di attribuzione:** la specifica che ho scritto io stesso per
DeepSeek (`AP-04_A2A4_DEEPSEEK_scout_command.md`, §"MODIFY, additive
only") istruiva esplicitamente a posizionare la chiamata esattamente dove
si trovava, prima del controllo `Emitted`. DeepSeek ha implementato la
specifica letteralmente e correttamente — il difetto origina nella
specifica, non in una deviazione di implementazione, ma è un difetto vivo
nel codice consegnato oggi, in un file (`WalkCommand.cs`) pienamente
nell'ambito di questo audit.

## 2. Nessun altro difetto trovato dopo un passaggio avversariale

Verificato esplicitamente, senza trovare problemi:

- **Gestione dell'autorità:** `ScoutCommand.ExecuteOneRound` inoltra solo
  l'`ActuationAuthority` ricevuta, invariata, a `WalkCommand.Execute`;
  l'unico sito di costruzione live (`ScoutCommand.RunWindows`) usa
  `ActuationAuthority.Commanded(Flag)` — esattamente i due tipi legittimi
  di ADR-0020, nessun bypass (`Commanded` lancia su un nome vuoto/nullo,
  quindi non è costruibile un'autorità "anonima").
- **Nessuna fabbricazione nel projector:** `MovementVerificationProjector.ToResult`
  è uno switch esaustivo sui 5 valori di `MovementOutcome` con un
  `throw new ArgumentOutOfRangeException` di default — fallisce chiuso su
  qualunque valore futuro non mappato, mai un default silenzioso.
  `Observed` è costruito da `verification.Observed is { } at`
  indipendentemente da `Outcome`, quindi non può mai riportare un fatto
  `Live` che il verificatore non ha davvero osservato.
- **Confine `NosAi.Core`/`NosAi.Runtime`:** `NosAi.Core.csproj` non
  dichiara alcun `ProjectReference`; un grep sull'intero repository
  conferma che nessun riferimento a `NosAi.Runtime` dentro
  `src/NosAi.Core/` esiste fuori da un commento XML `<c>`.
  `MovementVerificationProjector.cs` vive in
  `NosAi.Runtime.WorldModel.Fusion` e referenzia `NosAi.Core.WorldModel*`
  — la direzione corretta.
- **Ordinamento del ciclo round (`ExecuteOneRound`/`RunWindows`):**
  ritorna correttamente `null` quando nulla è raggiungibile; `RunWindows`
  si ferma correttamente su un run `null` o su `ExitCode != ExitArrived`,
  e il ciclo `for` non ha errori off-by-one.
- **Ciclo di vita delle risorse:** `MapReconstructionSource` è creato una
  sola volta (`using var`) fuori dal ciclo round e riusato; ogni `return`
  anticipato nel ciclo è dentro lo scope `using`, quindi la disposizione
  è garantita su ogni percorso di uscita.
- **Robustezza su valori nulli/Unknown:** `MapGrid.IsLoaded` ritorna
  correttamente `false` per una griglia fallita, degradando all'onesto
  rifiuto `ExitNotAdmitted` di `WalkCommand.Execute` invece di una NRE.
- **Parsing `--watch <n>` in `Program.cs`:** input negativo, zero, non
  numerico, o un flag finale senza valore ricadono tutti correttamente sul
  default di 1 round, nessun crash.
- **Concorrenza:** `--scout` è dispatchato da una singola catena `if`
  sequenziale in `Program.Main`, come ogni altro comando operatore.
- **Limite mob-feed:** dichiarato onestamente e chiaramente nei commenti
  di classe di `ScoutCommand.cs` ("No live mob feed... every
  `FrontierCandidate.Risk` is exactly `0`... never a fabricated mob
  position"), non nascosto.

## 3. Evidenza di build/test (eseguita indipendentemente)

```
export PATH="$PATH:/root/.dotnet"

dotnet build NosAi.sln -c Release
  → Build succeeded. 1 Warning (pre-esistente, xUnit2031 non collegato). 0 Errori.

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release \
  --filter "FullyQualifiedName~ScoutCommandTests|FullyQualifiedName~MovementVerificationProjectorTests|FullyQualifiedName~WalkCommandTests"
  → Passed: 36, Failed: 0, Skipped: 0, Total: 36

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed: 1967, Failed: 0, Skipped: 58, Total: 2025

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed: 579, Failed: 0, Skipped: 0, Total: 579
```

Confermato via `git show 1891e7d -- tests/NosAi.Runtime.Tests/WalkCommandTests.cs`
(vuoto): la modifica additiva a `WalkCommand.Execute` non ha richiesto
alcuna modifica a quel file di test, come dichiarato dalla consegna.

## 4. Livello di verifica dichiarato accurato?

Sì. `EXECUTION_QUEUE.md`/`DEEPSEEK_TASKS.md` dichiarano Q-028/Q-029
**`DONE` a livello `Present`** — esplicitamente non `Verified`, con la
motivazione "nessun client reale". Quella dichiarazione, ristretta a
"build+test verdi, nessuna regressione", è accurata e riprodotta sopra.
Non è sovra-dichiarata. "I test sono verdi" non va però letto come "la
consegna è comportamentalmente corretta in ogni aspetto documentato": il
Difetto 1 è una violazione di contratto reale che la suite verde non
cattura, perché nessun test esercitava un rifiuto guard a metà cammino.

## 5. Osservazioni architetturali (non difetti di questa consegna, segnalate per fasi future)

- **`FrontierCandidate.Risk` è un `double` grezzo, non un `WorldFact<double>`.**
  `ScoutCommand.cs` dichiara onestamente in prosa che `Risk == 0` oggi
  significa "non misurato" (nessun feed mob), non "confermato sicuro" —
  ma il sistema di tipi non porta questa distinzione. In tensione con
  l'invariante "Unknown non è zero/false/empty": il momento in cui un
  chiamante legge `Risk` direttamente (ranking, filtro, un futuro gate
  combattimento-adiacente) senza leggere anche la prosa del commento di
  classe, `0` si legge silenziosamente come "sicuro" anziché "non
  misurato". Da rappresentare esplicitamente (es. `WorldFact<double>` o
  un flag "misurato" separato) prima di appoggiarci sopra una decisione
  rilevante per la sicurezza.
- **Due rappresentazioni della "mappa corrente" per round**
  (`MapModel` dalla `MapReconstructionSource` cachata, e `MapGrid` riletto
  da disco ogni round) possono in linea di principio disaccordarsi se
  l'estrazione griglia fallisce in un round ma il `MapModel` cachato
  riflette ancora un successo precedente. Oggi degrada in modo sicuro
  (`grid.IsLoaded` rifiuta onestamente), non è un bug, solo da tenere
  presente per chi unificherà i due percorsi di lettura mappa.
- `MapReconstructionSource` è costruito con `logger: null` in
  `ScoutCommand.RunWindows`, quindi il proprio logging interno di errore
  (apertura store fallita, persistenza fallita, caricamento fallito) è
  silenziosamente perso per un operatore che esegue `--scout`
  interattivamente — gap minore di osservabilità, non di correttezza.

## 6. Verdetto

**AP-04 A2+A4 è pronta per l'integrazione A6**, con un solo blocco
puntuale e a basso rischio da chiudere: il Difetto 1 — spostare
`onStepVerified?.Invoke(to, verification)` dentro il blocco
`if (report.Emitted)`, nessuna modifica di firma pubblica. Nessun altro
difetto trovato dopo un passaggio avversariale su autorità, fabbricazione
dati, confine Core/Runtime, ciclo di vita risorse, robustezza
Unknown/null, parsing argomenti e concorrenza.
