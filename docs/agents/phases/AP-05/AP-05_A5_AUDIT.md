# AP-05 — Combat Intelligence — Audit A5 (Claude, indipendente)

**Autore:** A5 (Claude), audit indipendente (agente in background), su
`--engage` (Q-038/Q-039, DeepSeek). Nessun file di produzione toccato in
questo audit — solo lettura, build, test; la correzione trovata è
applicata separatamente in A6 (vedi `AP-05_STATUS.md`).
**Data:** 2026-09-07.
**Ambito:** verifica indipendente della consegna DeepSeek
`CombatVerificationProjector.cs`/`EngageCommand.cs` (commit `6e9b45d`, su
`origin/main`) contro la specifica
`AP-05_A2A4_DEEPSEEK_engage_command.md` e la decisione documentata in
`AP-05_A1_STATUS.md` §"Decisione presa: percorso (a)".

**Nota sullo stato dell'albero:** stessa situazione di AP-04/A5 — la
consegna è arrivata direttamente su `main`; l'audit ha lavorato su un
worktree isolato puntato a `origin/main` (`7f63c57`), ricompilato e
ritestato lì. Durante l'audit un'altra attività concorrente (questo
stesso agente principale) ha pushato un commit non collegato
(`497eff1`, solo documentazione) sul branch di lavoro condiviso — non ha
toccato nessun file di questa consegna, segnalato solo per trasparenza
sullo stato di un repository condiviso da più agenti attivi in parallelo.

## 1. Difetto reale trovato

### Difetto 1 — `EngageCommand.Run` termina con un'eccezione non gestita su un argomento identificatore presente ma vuoto, invece del pattern `[REFUSED]` pulito usato ovunque altrove nello stesso metodo

**File/riga esatti (prima della correzione):**
`src/NosAi.Runtime/Tactical/EngageCommand.cs:173-175`:
```csharp
ArgumentException.ThrowIfNullOrWhiteSpace(targetEntityId);
ArgumentException.ThrowIfNullOrWhiteSpace(skillId);
ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);
```
Queste righe girano **prima** del controllo `OperatingSystem.IsWindows()`
e prima di qualunque gestione in stile `[REFUSED]`. Il dispatch di
`Program.cs` (righe 217-238) controlla solo il **conteggio** degli
argomenti (`engageIndex + 2 >= args.Length`), non il contenuto — quindi
`--engage "" 201` (un argomento presente ma vuoto, banalmente costruibile
da qualunque script di orchestrazione che non ha ancora risolto un
entity id) supera il controllo di conteggio, raggiunge
`EngageCommand.Run("", "201", 1)`, e lancia. `Program.Main` non ha un
try/catch di livello superiore, quindi questa è un'eccezione non gestita
che termina l'intero processo runtime con uno stack trace .NET, invece
del `[REFUSED] <reason>` + exit code non-zero che ogni altro guard in
questo file (e il resto della famiglia di comandi operatore) produce.

**Scenario di fallimento concreto:** un harness di automazione che pilota
`--engage` da una variabile che si risolve a stringa vuota (es. un
entity id non ancora fuso) abbatte il processo invece di ricevere un
rifiuto pulito su cui ramificare. Severità: minore/robustezza, non un
problema di sicurezza o autorizzazione — `EntityId`/`SkillId`
(`src/NosAi.Core/WorldModel/Identifiers.cs`) lanciano anch'essi su stringa
vuota per la stessa convenzione a livello di repository, quindi non è un
errore isolato, ma `EngageCommand` è il primo comando operatore a
ricevere identificatori come stringhe grezze da argv, e il pattern non
sopravvive all'attraversamento del confine console. Nessun test in
`EngageCommandTests.cs` copriva questo percorso.

## 2. Nessun altro difetto trovato dopo un passaggio avversariale

Verificato esplicitamente, senza trovare problemi:

- **`CombatVerificationProjector.Project`**: ogni `CombatActionKind`
  diverso da `UseSkill` ritorna `Unobserved`/`ResourceNotObservableReason`
  con `Before`/`After` come `WorldFact<double>.Unknown(...)` — confermato
  `.HasValue == false`, non uno zero travestito. I casi `after == before`
  e `after > before` (pozione di mana a metà round) ricadono entrambi
  correttamente su `NoResourceChangeObserved`, mai `ResourceCostConfirmed`
  — la classificazione positiva scatta solo su una discesa netta, quindi
  fallisce verso il sotto-dichiarare, mai il sopra-dichiarare un costo.
- **`EngageCommand.ExecuteOneRound`**: ordine verificato — controllo
  autorità → controllo kind → controllo keybind confermato → lettura
  `before` → `KeyPress` → controllo `accepted` → attesa → lettura `after`
  → proiezione. Il tasto non viene mai premuto prima che `bind.Confirmed`
  sia vero. Un `false` da `input.KeyPress` diventa correttamente
  `Aborted`/`NotAttempted` via `KeyPressNotAcceptedReason` — mai trattato
  come inviato.
- **Ciclo di vita delle risorse**: `ClientMemorySession` è avvolto in
  `using (session)` subito dopo un `TryAttach` riuscito, e ogni ritorno
  anticipato è dentro quello scope — nessuna perdita trovata.
- **Logica di arresto del ciclo `--watch <n>`**: `ResourceCostConfirmed`
  ritorna 0 subito; `Aborted` ritorna `ExitAbandoned` subito; qualunque
  altro esito continua al round successivo — combacia esattamente con il
  commento del codice, nessun errore off-by-one.
- **Parsing argomenti in `Program.cs`**: `--engage` con 0 o 1 argomenti
  seguenti è rifiutato pulito dal controllo di conteggio; `--watch` con
  valore mancante o non numerico ricade sul default documentato di 1
  round.
- **La dichiarazione su `CommitPointValidator`/il gate di produzione nei
  commenti di `EngageCommand`**: verificata leggendo direttamente
  `RuntimeComposition.CreateSafe()` e `GatedInputBackend`/
  `CommitPointValidator`. `EngageCommand` non chiama mai
  `TryBeginActuation`, quindi non apre mai un `ActuationScope`. Sul gate
  armato di produzione, `GatedInputBackend.KeyPress` → `MayCommit` →
  `MayMove` trova `_scope == null` e rifiuta con
  `CommitScopeRequiredReason` **prima** che `CommitPointValidator.Validate`
  (le cinque condizioni) venga mai invocato. La dichiarazione è **accurata
  nell'esito** (una pressione tasto skill senza pixel sul gate armato
  reale si rifiuta davvero, con la stringa di motivo dichiarata), anche
  se la formulazione del commento sfuma leggermente che
  `CommitScopeRequiredReason` è una costante di `GatedInputBackend` che
  presidia l'ingresso al commit point, non una delle cinque condizioni
  nominate di `CommitPointValidator` stesso — nota di precisione
  documentale, non un difetto funzionale.
- **Nessuna sovra-dichiarazione in alcun output console**: grep su
  hit/damage/kill/struck/landed/success in `EngageCommand.cs`/
  `CombatVerificationProjector.cs` — ogni occorrenza è in un commento XML
  che esplicitamente **nega** che un colpo sia confermato. Le righe
  effettive verso l'operatore (`engage-evidence: ...`, l'header del
  round) non affermano mai un effetto sul bersaglio.
- **Direzione `NosAi.Core` → `NosAi.Runtime`**: confermata corretta.
- **`ActuationAuthority`**: esattamente `Planned`/`Commanded`/`None`,
  `EngageCommand` usa solo `Commanded(Flag)`, un'autorità mancante è
  rifiutata per nome prima di toccare vitali o input.

## 3. Evidenza di build/test (eseguita indipendentemente)

```
export PATH="$PATH:/root/.dotnet"

dotnet build NosAi.sln -c Release
  → Build succeeded. 0 Errori, 1 Warning pre-esistente non collegato.

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release \
  --filter "FullyQualifiedName~EngageCommandTests|FullyQualifiedName~CombatVerificationProjectorTests"
  → Passed: 26, Failed: 0, Skipped: 0, Total: 26

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed: 1967, Failed: 0, Skipped: 58, Total: 2025

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed: 578, Failed: 1 (TransportLoopTests, budget di latenza p99 sensibile
    al carico macchina, non toccato dal diff di questa consegna — flake
    pre-esistente, non una regressione)
```

## 4. Livello di verifica dichiarato accurato?

Sì. `DEEPSEEK_TASKS.md`/`EXECUTION_QUEUE.md` dichiarano `Present` —
"build+test verdi, nessuna regressione, nessun client reale disponibile
per `Verified`" — accurato quanto trovato. Nessuna sovra-dichiarazione.

## 5. Osservazioni architetturali (non difetti di questa consegna, segnalate per fasi future)

- **`EngageCommand` non apre mai uno scope di commit né avvia il monitor
  di input umano/`SessionActuationAuthority`**, a differenza di
  `ScoutCommand`/`WalkCommand`. Coerente con — e non un'estensione
  nascosta di — il limite già dichiarato dalla consegna stessa ("nessun
  ponte Guard/Trust/Safety ancora per questo comando"), dato che una
  pressione tasto skill non ha un bersaglio pixel da cui costruire una
  `CommitRequest`. Significa però che oggi `--engage` sul gate di
  produzione armato è **sempre** rifiutato — non esiste alcun percorso
  per cui possa davvero premere un tasto contro un client live, anche con
  tutto il resto configurato correttamente. Vale la pena segnalarlo
  esplicitamente per chi riprenderà "collegare l'esecuzione skill al
  commit point": non è solo "meno maturo" di `--walk`, è oggi non
  funzionante end-to-end contro il gate armato reale, per costruzione.
- Il ramo `resource_kind_not_observable_for_kind` di
  `CombatVerificationProjector` è irraggiungibile dal percorso live di
  `EngageCommand` (costruisce sempre un candidato `UseSkill` prima di
  chiamare `ExecuteOneRound`, che rifiuta già ogni kind non-`UseSkill`
  prima). Non è un bug — solo codice morto sul percorso live, per design,
  per un projector pensato per essere riusato da futuri chiamanti
  non-`--engage`.

## 6. Verdetto

**AP-05 A2+A4 è pronta per l'integrazione A6**, con un solo blocco
puntuale e a basso rischio da chiudere: il Difetto 1 — sostituire i
`ThrowIfNullOrWhiteSpace`/`ThrowIfLessThan` con un rifiuto `[REFUSED]`
pulito, coerente con ogni altro guard dello stesso metodo. Nessun altro
difetto trovato dopo un passaggio avversariale su verifica risorse,
ordine di esecuzione, ciclo di vita, parsing argomenti, e la
dichiarazione sul gate di produzione (verificata vera, con una sola nota
di precisione documentale non bloccante).
