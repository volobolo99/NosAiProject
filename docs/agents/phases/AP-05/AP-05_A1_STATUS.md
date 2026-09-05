# AP-05 / A1 — Stato

**Avviato per la stessa eccezione dichiarata già usata per AP-04/A1**
(`docs/agents/EXECUTION_QUEUE.md` "Criterio di avanzamento fase"): questi
contratti dipendono solo da tipi AP-01 già `Integrated`
(`Skill`/`Cooldown`/`StatusEffect`/`Resource`/`CombatantStatus`/`Mob`/
`Player`/`EntityId`/`SkillId`/`ItemId`/`WorldPosition`), non da come AP-04
esegue realmente un `NavigationPlan` — le due fasi possono procedere in
parallelo (Claude su AP-05/A1, DeepSeek su AP-04/A2+A4).

## Ambito

`docs/ROADMAP_ESECUTIVA.md` S:AP-05: "Candidate generation → hard
constraints → short-horizon simulation → utility/risk → combo prefix →
execute → verify → learn." A1 definisce **solo le forme dei dati** per le
prime quattro tappe di questa pipeline (candidate generation, hard
constraints, simulazione, combo) — l'algoritmo di generazione/ranking
(candidate generation reale da `Player.Skills`/`Cooldowns` × `Mob` in
range, la simulazione stessa, la selezione del combo) resta compito di
AP-05/A3, esattamente come `FrontierCandidate` in AP-04/A1 non calcolava
il proprio punteggio.

## Consegnato

`src/NosAi.Core/WorldModel/Combat/CombatContracts.cs`:

- `CombatActionKind` — `BasicAttack`/`UseSkill`/`UseConsumable`/
  `Reposition`/`Flee`. Enum proprio, **non** un riuso di
  `NosAi.Runtime.Contracts.ActionType` (il tipo del sistema Gate 1-6
  preesistente, non ancora riconciliato con la nuova architettura — copre
  anche azioni non di combattimento come `CollectGroundItem`).
- `CombatActionCandidate` — un candidato atto di combattimento
  (`Kind` + `Target`/`Skill`/`Item`/`Destination` opzionali secondo
  `Kind`). Costruttore che valida esattamente la combinazione richiesta da
  ogni `Kind` (stesso trattamento del guard su `ResourceKind.Custom` in
  `Resource`), non un record "tutto opzionale" che lascerebbe combinazioni
  insensate costruibili.
- `CombatConstraintCheck` — esito del filtro "hard constraints" (cooldown,
  risorsa, range, validità target). **Non** è il Safety Guard di runtime:
  è un filtro tattico precedente, che tiene fuori dalla simulazione i
  candidati ovviamente inutilizzabili — il Guard/Trust/Safety reale resta
  a valle, sull'atto già selezionato (stesso principio già chiarito per
  AP-04: nessuna authority/bypass inventati qui).
- `CombatSimulationResult` — input grezzi della simulazione a breve
  orizzonte per un candidato (danno inflitto/subito, tempo di esecuzione,
  costo risorsa, rischio folla, probabilità di fuga, rilevanza missione,
  posizione prevista dopo l'atto). Stesso principio di
  `FrontierCandidate` in AP-04: input grezzi, non punteggio — il ranking
  resta ad A3.
- `ComboStep`/`ComboPlan` — sequenza breve e ordinata di atti scelta come
  unità ("combo prefix"), stesso stile di `NavigationPlan`
  (`Steps`/`IsViable`/`ObservedAtUtc`, fabbrica `Unviable` per "nessun
  combo utile trovato").

Test: `tests/NosAi.Core.Tests/WorldModel/Combat/CombatContractsTests.cs`
(25 test, tutti verdi). Build `NosAi.Core` pulita, 0 warning/0 errori.
`dotnet build NosAi.sln -c Release`: 0 errori (1 warning preesistente non
collegato). `dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release`:
**444/444**, 0 falliti (419 precedenti + 25 nuovi, zero regressioni). Una
prima esecuzione ha mostrato 1 fallimento in
`TransportLoopTests.OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget`
(budget di latenza p99, sensibile al carico macchina, non toccato da
questo lavoro) — riprodotto in isolamento (verde, <1ms) e su una seconda
esecuzione completa (verde): flake da contesa CPU sotto test paralleli,
non una regressione di questo commit.

## Deliberatamente non affrontato qui (segnalato, non ignorato)

- **"Storico per mob/build"** (l'ultima tappa della pipeline, "learn"):
  nessun contratto di persistenza cross-sessione è stato scritto in
  questo passaggio. La roadmap canonica assegna la memoria/apprendimento
  cross-sessione ad **AP-09 "Memory/Learning/Simulation"**, una fase
  successiva — inventare qui una forma di storico persistente
  rischierebbe di fissare una forma sbagliata prima che AP-09 la
  definisca. Un futuro "storico recente in-memoria" per polarizzare il
  replan nello stesso incontro (non cross-sessione) resta un'estensione
  additiva possibile per AP-05/A3, non un contratto A1 mancante.
- **`CombatDecision`/evidenza di esecuzione** (l'output finale scelto e
  il fatto canonico che riporta cosa è successo dopo l'esecuzione): non
  scritto ora, per lo stesso motivo per cui il contratto di evidenza di
  movimento di AP-04 è arrivato solo dopo A3 — la forma esatta dipende da
  come A3 seleziona un `ComboPlan` e da cosa l'esecuzione reale (A2/A4,
  bridge verso Guard/Trust/Safety) può davvero riportare indietro.
  Scriverlo ora significherebbe indovinare.

## Nota per A2/A4 (non ancora avviati)

Lo stesso vincolo trovato per AP-04 vale qui, probabilmente in modo
ancora più diretto: `Gate3Runtime.ActionPlanner` (sistema Gate 1-6
preesistente) genera già oggi candidati di combattimento reali
(HP-critico/contrattacco/skill/attacco) ma con generazione **chiusa e
hardcoded** dentro il file stesso, una `PredictedOutcome` che per il
movimento è un placeholder fisso (non verificato se lo stesso vale per le
azioni di combattimento — da investigare quando si arriva ad A2/A4, non
assunto). Non ripetere qui l'errore evitato in AP-04: **prima di
scrivere la specifica DeepSeek per AP-05/A2+A4, investigare se
`Gate3Runtime` produce già una simulazione di combattimento reale
riusabile, e se l'esecuzione delle sue azioni di combattimento passa già
per Guard/Trust/Safety in modo reale** (a differenza del movimento, il
combattimento potrebbe essere il caso in cui quella pipeline è più
matura, dato che `TryAuthorize`/`SafetyToken` sono già usati lì per
skill/attacchi). Non assumere: verificare con un'indagine mirata come
già fatto per AP-04.

**Livello di verifica:** `Present` — contratti scritti, testati,
compilano puliti; non ancora `Integrated` in nessun ciclo runtime, perché
niente li produce/consuma ancora.
