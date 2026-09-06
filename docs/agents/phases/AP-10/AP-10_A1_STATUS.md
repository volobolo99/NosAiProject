# AP-10 / A1 — Contratti di certificazione + report onesto

## Perché questa fase è diversa da AP-00→AP-09

`docs/ROADMAP_ESECUTIVA.md` S:AP-10: "Scenario completo: startup →
attach → perception → map discovery → exploration → navigation → target
recognition → combat → loot/inventory → multi-step quest →
equipment/progression → recovery → persistence → evidence. DoD:
segmento autonomo senza gameplay commands umani, dati privilegiati o
hardware di automazione esterno; funzionamento entro i budget misurati
del laptop."

AP-10 non è una fase di costruzione, è la fase di **certificazione**.
Non produce contratti/algoritmi di dominio come AP-04→AP-09: certifica
se l'intera pipeline, end-to-end, funziona davvero. Questo ambiente
(container Linux, nessun client NosTale reale, nessun hardware ASUS
Nitro V16/RTX 5060, nessun telefono Guard AI) **non può eseguire quello
scenario**, per la stessa "Real-environment rule" di `CLAUDE.md` già
citata in ogni fase precedente: "Client integration, perception and
actuation require real target validation before Verified." Dichiarare
qui una certificazione superata sarebbe esattamente la violazione che
questo progetto vieta in modo più esplicito di ogni altra regola.

Quello che **è** onesto e utile fare in questo ambiente: dare per la
prima volta una forma tipizzata reale alla convenzione
`Present`/`Integrated`/`Done`/`Verified` (usata solo in prosa dalla fase
AP-00 in poi) e compilare, con citazioni esatte, il report reale di dove
sta oggi ciascuno dei 14 stadi della DoD — senza inventare un livello
più alto di quello che le fonti già scritte in questo repository
supportano.

## Consegnato

`src/NosAi.Core/WorldModel/Certification/CertificationContracts.cs`:

- `VerificationLevel` — `Present`/`Integrated`/`Done`/`Verified`, ora un
  tipo reale invece di sola prosa.
- `CertificationStage` — i 14 stadi esatti della DoD, nello stesso
  ordine.
- `CertificationStageResult` — livello + `Evidence` (citazione
  obbligatoria, mai vuota) + `Blockers`. Un vincolo reale nel
  costruttore: uno stadio `Verified` non può avere blocker (se qualcosa
  blocca ancora, non è Verified per definizione).
- `CertificationReport` — `OverallLevel` è il livello **più debole** tra
  tutti gli stadi (il principio "anello debole": uno scenario end-to-end
  vale quanto il suo stadio peggiore), `null` se non c'è nessuno stadio
  valutato (mai `Present` di default). `IsFullyCertified` richiede
  esattamente 14 stadi tutti `Verified`.

Test: `tests/NosAi.Core.Tests/WorldModel/Certification/CertificationContractsTests.cs`,
9 test, tutti verdi. `dotnet build NosAi.sln -c Release`: 0 errori (1
warning preesistente non collegato). `dotnet test
tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`: **564/564**,
0 falliti (555 precedenti + 9 nuovi, zero regressioni).

## Il report reale, oggi — con citazione per ogni riga

Non un `CertificationReport` costruito in codice (una certificazione non
è telemetria a runtime da serializzare, è una valutazione con fonti
citabili) — la tabella stessa, qui, è il contenuto reale che quel tipo
esiste per rappresentare.

| # | Stadio | Livello | Evidenza citata | Blocco principale |
|---|---|---|---|---|
| 1 | Startup | `Verified`* | `docs/STATO_IMPLEMENTAZIONE.md`: "Il primo circuito reale PC↔NosTale↔smartphone è stato chiuso e verificato, e con esso il Gate 1." — bootstrap `Gate1BootstrapHost` fa parte di quel circuito. | — |
| 2 | Attach | `Verified`* | Stessa fonte — l'attach al processo client è precondizione del circuito Gate 1 verificato. | — |
| 3 | Perception | `Integrated` | `AP-02_STATUS.md` §9: "Integrated... ambito vitali." | Mob/NPC/oggetti da visione bloccati da OCR/ONNX non addestrato (stesso §10). |
| 4 | Map discovery | `Integrated` | `AP-03_STATUS.md` §8: "Integrated". | Portali multi-mappa: nessuna fonte dati reale (`AP-04_A1_STATUS.md`). |
| 5 | Exploration | `Present` | `AP-04_A1_STATUS.md`: A1+A3 `Present`. `EXECUTION_QUEUE.md` Q-028/Q-029: `ScoutCommand`/`MovementVerificationProjector` consegnati da DeepSeek il 2026-09-06, build/test verdi. | Consegnato ma non ancora eseguito contro un client reale — resta `Present`, non `Integrated` (nessun ciclo runtime lo richiama ancora automaticamente) né `Verified`. |
| 6 | Navigation | `Present` | Il cammino cella-per-cella (`PathWalkController`/`WalkCommand`) è reale e Gate-1-verificato come **meccanismo**. La catena "il World Model sceglie dove andare → si cammina davvero" ora esiste (`ScoutCommand`, DeepSeek, Q-028/Q-029) e passa build/test in questo ambiente Linux. | Non ancora eseguita contro un client NosTale reale/Windows — stesso limite di ogni altro stadio, non una lacuna di codice. |
| 7 | Target recognition | `Present` | Entità da rete (pacchetti reali) fluiscono in `Mob`/`Player` (AP-01 `Integrated`); riconoscimento **visivo** bloccato da OCR/ONNX come sopra. | Nessuna classificazione visiva reale di mob/NPC. |
| 8 | Combat | `Present` | `AP-05_A1_STATUS.md`: A1+A3 parziale `Present`. | Nessun dato reale di danno/costo skill; A2+A4 non specificato, in attesa di una decisione (verifica solo-vitali-player vs chiudere il gap HP-mob). |
| 9 | Loot/inventory | `Present` | `AP-07_A1_STATUS.md`: A1+A3 parziale `Present`. | Generazione candidati Equip bloccata (nessun dato categoria item). |
| 10 | Multi-step quest | `Present` | `AP-06_A1_STATUS.md`: A1+A3 `Present`. | Semantic extraction OCR/UI bloccata (stesso gap ML). |
| 11 | Equipment/progression | `Present` | `AP-07_A1_STATUS.md` (equip) + `AP-09_A1_STATUS.md`/`NosAi.Core.Progression` (progressione, preesistente). | Statistiche reali per item mancanti. |
| 12 | Recovery | `Present`, parziale | `AP-08_A1_STATUS.md`: nessun segnale strategico "Recovery" valutato (serve un fatto reale "sono in combattimento adesso"). Il recovery di **sicurezza/esecuzione** (`RecoveryController`, sistema Gate 1-6 preesistente) è invece reale e testato, ma è un concetto diverso (fault recovery, non "riposare per curarsi"). | Nessun segnale di recovery gameplay-level; non riverificato in questa sessione lato Gate 1-6. |
| 13 | Persistence | `Integrated`, parziale | `MapModelStore` (AP-03) reale, SQLite WAL, `Integrated`. Il ledger di AP-09 (`ActionOutcomeLedgerEntry`) è invece solo `Present`: A2+A4 specificato (`AP-09_A2A4_DEEPSEEK_ledger_wiring.md`, `ActionOutcomeLedgerStore` stile `MapModelStore`), non ancora consegnato da DeepSeek. | Ledger AP-09 non cablato a una persistenza reale finché Q-061 non è consegnato. |
| 14 | Evidence | `Integrated` (verificato in questa sessione, vedi Aggiornamento 2 sotto) | `SqliteEventJournal`/`IEventJournal` (`src/NosAi.Storage/`): hash-chain SHA256 reale, cablata nel vero composition root Gate 1 (`NosAiHost`, `src/NosAi.Host/NosAiHost.cs:100,124-459`) — ogni attach/handshake/frame/capability/disconnect viene giornalato. Rilevazione manomissione, policy WAL/FULL/busy_timeout e ripresa sequenza dopo riapertura verificate con test rieseguiti freschi in questa sessione (`SqliteEventJournalTests` 6/6, `NosAiHostTests` 2/2, entrambi 0 falliti). | Non `Verified`: nessuna sessione hardware reale di questa sessione ha prodotto e ri-verificato un journal fisico — coerente con la classificazione già data a questo stesso sottosistema da `docs/STATO_IMPLEMENTAZIONE.md` ("🟢 Present o Integrated a livello di codice"), mai promossa a `Verified` nemmeno lì. |

\* **Nota importante sugli stadi 1-2 e sul recovery di sicurezza**: la
loro classificazione `Verified` viene da documentazione preesistente
(`docs/STATO_IMPLEMENTAZIONE.md`), **non da una riverifica hardware
eseguita in questa sessione** (questo ambiente non ha un client NosTale
reale né l'hardware target). Citata come fonte secondaria dichiarata,
non come osservazione diretta di questa fase — la distinzione stessa che
CLAUDE.md richiede.

## `OverallLevel` risultante

Il livello più debole tra i 14 stadi è `Present` (stadi 5-11). **Il
progetto non è certificabile end-to-end in questo momento, in questo
ambiente, e questa fase non lo dichiara.** Nessuno stadio raggiunge
`Verified` per lavoro di questa sessione; gli unici due (`Startup`/
`Attach`) marcati `Verified` lo sono per eredità documentata del sistema
Gate 1-6 preesistente, esplicitamente segnalata come non riverificata
qui.

## Deliberatamente non affrontato qui

- **Esecuzione reale dello scenario end-to-end**: richiede Windows, un
  client NosTale reale, l'hardware target (ASUS Nitro V16/RTX 5060) e un
  telefono Guard AI — nessuno disponibile in questo ambiente di sviluppo.
  Non simulabile senza violare la Real-environment rule.
- **Chiusura dei gap già segnalati** (OCR/ONNX, dati skill/item reali,
  fonte portali, riconciliazione `KnowledgeScope`/`DataSourceKind`):
  elencati per riferimento, non richiusi da questa fase — sono il lavoro
  reale che separa il progetto da una certificazione vera, non un
  dettaglio di reportistica.

**Livello di verifica per il contenuto di questa fase (i contratti
stessi):** `Present` — scritti, testati, compilano puliti. **Il report
compilato sopra non è una certificazione**: è la fotografia onesta,
citata, di quanto lontano il progetto sia da poterne ricevere una.

## Aggiornamento — verifica post AP-05/--recover e AP-08/--autoplay (2026-09-06)

Verificato direttamente il codice sorgente (non per assunzione) dopo
l'arrivo di `--recover` (AP-05 §8bis, commit `219225b`, nessun difetto
trovato da `AP-05_A5_AUDIT.md` §7) e `--autoplay` (AP-08, commit
`34a8b54`, un difetto — footprint non propagato — trovato e corretto da
`507c476`/`f07158c`, livello finale `Integrated`).

**Stadio 8 (Combat) — riga da rivedere.** `--recover` vive nello stesso
namespace/contratto di `--engage`: `RecoverCommand.cs:14-16` lo dichiara
esplicitamente "AP-05 'Combat Intelligence', execute → verify — the
recovery counterpart of EngageCommand", e
`CombatExecutionContracts.cs:38-49` aggiunge `ResourceGainConfirmed`
come specchio di `ResourceCostConfirmed` nello stesso enum
`CombatExecutionResult`. Ma il blocco che la riga cita — "nessun dato
reale di danno/costo skill" nel senso di confermare un colpo sul
bersaglio — resta intatto: `CombatExecutionContracts.cs:59-62` dichiara
ancora oggi che `Mob.Status.Resources` non è popolato (stesso gap
OCR/ONNX di AP-02); né `--engage` né `--recover` osservano mai il
bersaglio, solo le risorse del player (`RecoverCommand.cs:161-166`
rifiuta ogni `CombatActionKind` diverso da `UseConsumable`). Indipendente
da `--recover`: la riga cita ancora `AP-05_A1_STATUS.md` ("A2+A4 non
specificato, in attesa di una decisione") — citazione già superata da
`--engage` stesso (commit `6e9b45d`, deciso in `fd9473b`), consegnato
*prima* che questo aggiornamento venisse scritto ma mai riportato in
questa tabella. `AP-05_STATUS.md` §8 dichiara il livello finale di AP-05
`Integrated` (A1+A2+A3+A4, un difetto reale trovato e corretto da A5/A6).
Per lo stesso criterio già usato alla riga 3 (Perception: `Integrated`
sull'ambito vitali, bloccato sull'ambito visivo), lo stadio 8 va
aggiornato a `Integrated, parziale`: l'ambito risorse-proprie
(costo/guadagno via player vitals, due atti gemelli auditati) è
`Integrated`; l'ambito bersaglio (danno confermato su un mob) resta
bloccato, invariato.

**Stadio 12 (Recovery) — riga da rivedere.** La frase esatta già in
tabella, "nessun segnale strategico 'Recovery' valutato", resta vera
oggi alla lettera: `StrategyPlanner.cs:23-26` porta lo stesso commento
di AP-08 invariato ("`StrategicGoalKind.Recovery` needs a real
'currently in combat' signal... not yet assessed"), e
`AutoplayCommand.cs:45-47` conferma che `Recovery`/`Progression`/
`Farming`/`Optimization` "have no assessor at all". `StrategyPlanner`
non guadagna un `AssessRecoveryUrgency`: solo `Survival` (HP fraction),
`QuestUrgency`, `Exploration` sono valutati, esattamente come ad AP-08.
Ma sotto il nome `Survival` esiste ora un atto di recupero gameplay
reale, automatico ed end-to-end (per quanto il termine "end-to-end" possa
valere in questo ambiente): `AutoplayCommand.ExecuteOneCycle`
(`AutoplayCommand.cs:219-241`, caso `StrategicGoalKind.Survival`)
dispatcha `RecoverCommand.ExecuteOneRound` quando la Survival urgency è
la più alta, senza che l'operatore nomini l'atto al momento
dell'invocazione — lo stesso enum `AutoplayDispatch` chiama l'esito
`Recovered` (`AutoplayCommand.cs:99-100`), non "SurvivalHandled". Questo
è meccanicamente un comportamento di recovery reale, verificato
(HP prima/dopo), integrato (`AP-08_STATUS.md`: livello finale
`Integrated`) — ma resta guidato dalla sola soglia HP di `Survival`, mai
da un segnale distinto "sono in combattimento adesso" che differenzi
recovery da mera sopravvivenza generica. Stadio 12 va aggiornato a
`Integrated, parziale`: il meccanismo di cura via consumabile innescato
da soglia HP è `Integrated`; un segnale strategico `Recovery` distinto
da `Survival` resta non valutato, invariato da `AP-08_A1_STATUS.md`.

**Stadi 5-6 (Exploration/Navigation) — nessuna revisione di livello.**
`AutoplayCommand.ExecuteOneCycle` dispatcha anche
`ScoutCommand.ExecuteOneRound` (`AutoplayCommand.cs:192-217`) sotto
`StrategicGoalKind.Exploration`, ma è lo stesso meccanismo già `Present`
in tabella con un chiamante in più, non nuova evidenza di esecuzione
contro un client reale — restano `Present`, invariati.

**Stadi 1-4, 7, 9-11, 13-14.** Nessuna delle due consegne li tocca:
`--recover`/`--autoplay` non scrivono su `ActionOutcomeLedgerEntry` né
su `RuntimeEvent`/`SqliteEventJournal` (verificato: nessuna occorrenza
in `RecoverCommand.cs`/`AutoplayCommand.cs`/`EngageCommand.cs`) — righe
13 (Persistence) e 14 (Evidence) restano quelle già scritte. Le righe
1-4, 7, 9-11 sono fuori dall'ambito di entrambe le consegne — invariate.

**`OverallLevel`: invariato, resta `Present`.** Il livello più debole
non cambia: gli stadi 5, 6, 7, 9, 10, 11 restano `Present` a prescindere
dalla revisione di 8 e 12 — l'"anello debole" si sposta da otto stadi a
sei, non sopra `Present`.

**Prossimo passo A2+A4 per AP-10: verificato, resta assente — per un
motivo più preciso di quello originale.** Il candidato ovvio che questo
compito chiedeva di indagare — "uno script/comando che incateni
davvero i comandi già consegnati in uno scenario end-to-end eseguibile
in questo ambiente" — **esiste già**, non come lavoro AP-10 ma come
`AutoplayCommand` stessa (AP-08): incatena `ScoutCommand`/`RecoverCommand`
scelti da `StrategyPlanner` in un ciclo, esattamente la forma che un
ipotetico A2+A4 di AP-10 costruirebbe. Scriverne un secondo dentro AP-10
non produrrebbe certificazione aggiuntiva, solo un duplicato: il limite
che impedisce la certificazione non è l'assenza di un orchestratore (che
ora c'è), è che nessun ramo di `AutoplayCommand.RunWindows` gira senza
`OperatingSystem.IsWindows()` vero, un processo client reale
(`TryFindWindow`, `AutoplayCommand.cs:294`) e un `ClientMemorySession`
attaccato (`AutoplayCommand.cs:300`) — tutti assenti in questo
container Linux. Costruire un doppio con dipendenze finte violerebbe la
Real-environment rule esattamente come l'esecuzione simulata che questa
fase rifiuta dal §1. La conclusione originale di AP-10 tiene, confermata
da un fatto nuovo (l'orchestratore esiste già altrove) invece che per
sola assunzione: AP-10 resta senza un secondo passo di costruzione
proprio, perché non è una fase di costruzione — e il primo candidato
naturale a quel ruolo è già stato consegnato da AP-08.

## Aggiornamento 2 — verifica dedicata dello stadio 14 (Evidence)

L'Aggiornamento precedente segnalava lo stadio 14 come "non riaudito con
evidenza fresca... da confermare con una verifica dedicata, non assunto
per memoria". Fatto ora, per lettura diretta del codice e riesecuzione
dei test, non per memoria:

**Meccanismo, verificato reale**: `src/NosAi.Storage/SqliteEventJournal.cs`
implementa `IEventJournal` con una catena hash reale —
`ComputeChainHash` (righe 170-182) calcola
`SHA256(previousHash ‖ sequence ‖ unixMillis ‖ stage ‖ payload)`,
`VerifyChain` (righe 105-137) la ricalcola da un punto di partenza già
committato (mai un hash fornito dal chiamante, riga 107) e confronta con
`CryptographicOperations.FixedTimeEquals`, non `==`. WAL/FULL/
busy_timeout sono applicati e poi **riletti e verificati** (righe
232-248), esattamente come `MapModelStore` (AP-03).

**Cablaggio reale, non isolato**: `src/NosAi.Host/NosAiHost.cs` — il vero
composition root Gate 1, lo stesso il cui circuito PC↔NosTale↔smartphone
è dichiarato `Verified` in `STATO_IMPLEMENTAZIONE.md` — apre il journal
da `SqliteEventJournal.OpenFromVolume` (riga 100) e chiama
`_journal.Append` per ogni evento reale del ciclo di vita: esito attach
(riga 124), stato handshake (righe 212-218), fault di decodifica frame
(riga 250), pacchetto di replay respinto (riga 257), esito capability
(riga 288), heartbeat (riga 309), disconnessione (riga 332). Non è una
libreria scritta e mai collegata.

**Test rieseguiti in questa sessione, non citati a memoria**:
```
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release \
  --filter "FullyQualifiedName~SqliteEventJournalTests"
  → Passed: 6, Failed: 0, Total: 6
    (include VerifyChainDetectsATamperedRecordAtTheCorrectSequence,
    VerifyChainIsValidAcrossTenThousandRecords,
    ReopeningTheSameDatabaseResumesTheSequenceAndPreservesTheChain,
    JournalAppliesAndVerifiesTheWalFullSynchronousBusyTimeoutPolicy)

dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release \
  --filter "FullyQualifiedName~NosAiHostTests"
  → Passed: 2, Failed: 0, Total: 2
    (RunAsyncJournalsTheAttachOutcomeAndPublishesTelemetry,
    SequentialRunsOnTheSameHostAppendSuccessiveJournalSequences —
    NosAiHost reale con journal SQLite su file temporaneo reale, mai
    :memory: né un journal finto)
```

**Un secondo meccanismo correlato, non ri-auditato in dettaglio qui**:
`src/NosAi.Runtime/Gate2/Gate2Runtime.cs` definisce anche un proprio
`RuntimeEvent`/`BoundedEventBus` (righe 63-64 e seguenti) — un bus
eventi Gate 2 distinto dalla catena hash di Gate 1, non lo stesso
meccanismo. È il "Registro eventi durevole e riproducibile (M075-M076)"
che `docs/STATO_IMPLEMENTAZIONE.md` elenca **nella stessa sezione**
("🟢 Present o Integrated a livello di codice") della catena hash —
citato qui per completezza, non riaudito riga per riga: la fonte
canonica già lo classifica allo stesso livello, e ririderivare
manualmente la stessa conclusione non aggiungerebbe evidenza.

**Perché `Integrated` e non `Verified`**: il meccanismo è reale, cablato
nel vero Gate 1, e i suoi test (incluso il rilevamento di manomissione)
sono passati freschi in questo ambiente — ma nessuna sessione hardware
reale di *questa* fase ha prodotto un journal fisico e poi ri-verificato
la sua catena con `VerifyChain` contro un file realmente scritto durante
un run PC↔NosTale↔telefono. `docs/STATO_IMPLEMENTAZIONE.md` stesso non
promuove mai questo sottosistema oltre "Present o Integrated a livello
di codice", nemmeno descrivendo il circuito Gate 1 come complessivamente
`Verified` — la stessa distinzione che questa fase applica ovunque tra
un meccanismo provato corretto e una sessione reale che lo ha esercitato.

**`OverallLevel`: invariato, resta `Present`.** Gli stadi 5, 6, 7, 9, 10,
11 restano `Present`, sotto `Integrated` — promuovere lo stadio 14 non
sposta l'anello debole.

**Stadi 1-2 (Startup/Attach), nota\* del §"Nota importante"**: restano
non riverificabili in questo ambiente (nessun client NosTale reale,
nessun hardware target) — non un lavoro rimandato per scelta, un limite
dichiarato dalla Real-environment rule che nessuna indagine da questa
sessione può aggirare.
