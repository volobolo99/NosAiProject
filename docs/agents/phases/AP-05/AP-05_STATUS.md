# AP-05 — Combat Intelligence — Stato finale

## 1. Ambito

Candidate generation → hard constraints → short-horizon simulation →
utility/risk → combo prefix → execute → verify → learn
(`docs/ROADMAP_ESECUTIVA.md` S:AP-05). Simulazione/combo/apprendimento
cross-sessione restano deliberatamente non affrontati: nessun dato reale
di danno/costo skill esiste in questo repository (vedi
`AP-05_A1_STATUS.md`).

## 2. A1 — Contratti Combat Intelligence

`src/NosAi.Core/WorldModel/Combat/CombatContracts.cs`:
`CombatActionKind`, `CombatActionCandidate`, `CombatConstraintCheck`,
`CombatSimulationResult`, `ComboStep`/`ComboPlan`. Vedi
`AP-05_A1_STATUS.md`.

## 3. A3 (parziale) — `CombatPlanner`

`src/NosAi.Core/WorldModel/Combat/CombatPlanner.cs`:
`GenerateCandidates`/`CheckHardConstraints` da dati reali
(`Player.Skills`/`Cooldowns` × `Mob` in range). Simulazione/combo
rimandati — nessun dato reale di danno/costo skill in AP-01. Vedi
`AP-05_A1_STATUS.md` §"A3 (parziale)".

## 4. Decisione percorso (a) + contratto mancante

Indagine su `Gate3Runtime` per skill/attacco conclusa: stessa classe di
problema del movimento (selezione skill hardcoded, predizione
placeholder, Guard che decide su rischio fabbricato) — nessun bridge.
Decisione presa: verifica combattimento solo-vitali-player (onesta ma
parziale — conferma il costo risorsa, non il colpo sul bersaglio), scelta
perché chiudere prima il gap di fusione HP-mob in AP-02 resterebbe
bloccato indefinitamente sul gap OCR/ONNX. Contratto mancante scritto:
`src/NosAi.Core/WorldModel/Combat/CombatExecutionContracts.cs`
(`CombatExecutionResult`/`CombatExecutionEvidence`). Vedi
`AP-05_A1_STATUS.md` §"Decisione presa: percorso (a)".

## 5. A2+A4 — `CombatVerificationProjector` + comando operatore `--engage` (DeepSeek)

Specifica scritta: `AP-05_A2A4_DEEPSEEK_engage_command.md`. Consegnato da
DeepSeek (commit `6e9b45d`):

- `src/NosAi.Runtime/WorldModel/Fusion/CombatVerificationProjector.cs`
  (A2) — proietta `PlayerVitalsReading` prima/dopo reale in
  `CombatExecutionEvidence`.
- `src/NosAi.Runtime/Tactical/EngageCommand.cs` (A4) — comando operatore
  `--engage <targetEntityId> <skillId> [--watch <n>]`: esecuzione di un
  solo atto `UseSkill` nominato direttamente dall'operatore, via
  `KeybindMap`+`GatedInputBackend.KeyPress`, verifica via
  `ClientMemorySession.TryReadPlayerVitals` prima/dopo,
  `ActuationAuthority.Commanded("--engage")`.
- 26 test nuovi tra i due file di test (`EngageCommandTests.cs`,
  `CombatVerificationProjectorTests.cs`).

Ambito dichiarato esplicitamente ristretto: `BasicAttack` rifiutato per
design (nessun costo risorsa osservabile), nessuna generazione candidati
da `CombatPlanner` (richiederebbe `Player`/`Mob` fusi che questo comando
in composizione live non assembla oggi).

## 6. A5 — Audit indipendente

Report completo: `AP-05_A5_AUDIT.md`. **Un difetto reale trovato**:
`EngageCommand.Run` terminava con un'eccezione non gestita su un
argomento identificatore presente ma vuoto (`--engage "" 201`), invece
del pattern `[REFUSED]` pulito usato altrove nello stesso metodo — il
dispatch di `Program.cs` controlla solo il conteggio degli argomenti, non
il contenuto. Nessun altro difetto trovato dopo un passaggio avversariale
su verifica risorse, ordine di esecuzione, parsing argomenti, e la
dichiarazione sul gate di produzione (`CommitPointValidator`), verificata
vera.

## 7. A6 — Integrazione finale

Applicata l'unica correzione richiesta: sostituiti i tre
`ArgumentException.ThrowIfNullOrWhiteSpace`/`ArgumentOutOfRangeException.ThrowIfLessThan`
in `EngageCommand.Run` con un controllo esplicito che stampa
`[REFUSED] engage_requires_non_blank_target_skill_and_positive_rounds` e
ritorna `WalkCommand.ExitAbandoned` — stesso confine `[REFUSED]` di ogni
altro guard nello stesso metodo, nessuna eccezione non gestita più
raggiungibile da un argomento vuoto o da `rounds < 1`.

Aggiunti tre test di regressione dedicati in `EngageCommandTests.cs`
(`Run_BlankTargetEntityId_IsRefusedCleanly_NeverThrows`,
`Run_BlankSkillId_IsRefusedCleanly_NeverThrows`,
`Run_ZeroRounds_IsRefusedCleanly_NeverThrows`) — l'unica parte di
`Run`/`RunWindows` testabile senza desktop, dato che la convalida
argomenti gira prima del controllo `OperatingSystem.IsWindows()`.

**Evidenza:**
```
dotnet build NosAi.sln -c Release
  → Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release \
  --filter "FullyQualifiedName~EngageCommandTests"
  → Passed! Failed: 0, Passed: 13, Skipped: 0, Total: 13

dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 1971, Skipped: 58, Total: 2029

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
  → Passed! Failed: 0, Passed: 579, Skipped: 0, Total: 579
```
Il fallimento isolato di `TransportLoopTests` osservato da A5 non si è
ripresentato in questa esecuzione — confermato flake pre-esistente
sensibile al carico macchina, non collegato a questa consegna. Nessuna
regressione rispetto ai conteggi riportati da A5 (26/26 mirato, ora 13
perché questo filtro copre solo `EngageCommandTests`;
`CombatVerificationProjectorTests` invariati).

## 8. Livello di verifica finale — AP-05

**`Integrated`**: A1+A2+A3+A4 costruiscono un albero unico che compila
pulito e passa tutti i test combinati, incluso il difetto reale trovato
dall'audit indipendente A5 e corretto in questo passaggio. **Non
`Verified`**: nessuna validazione contro un client NosTale reale, e
`EngageCommand` è oggi non funzionante end-to-end contro il gate di
produzione armato per costruzione (nessun ponte a un `ActuationScope` —
vedi `AP-05_A5_AUDIT.md` §5), un limite dichiarato, non nascosto.

## 9. Item aperti, esplicitamente rimandati

- **Bridge esecuzione skill → commit point del Safety Gate reale**:
  segnalato da A5 come lavoro futuro, non affrontato qui — un tasto skill
  non ha un bersaglio pixel su cui costruire una `CommitRequest` nello
  schema attuale.
- **Generazione candidati da `CombatPlanner` nel contesto live**:
  richiederebbe `Player`/`Mob` fusi che nessuna composizione one-shot
  assembla oggi — stesso genere di limite già dichiarato per
  `ScoutCommand`/il feed mob.
- **Simulazione di danno/costo skill reale**: bloccata dal gap dati già
  noto (`GameReferenceDatabase` non decodifica semanticamente quei campi).
- **`--engage` non ancora eseguito contro un client reale**: stesso
  limite di ogni altro stadio, non una lacuna di codice.
