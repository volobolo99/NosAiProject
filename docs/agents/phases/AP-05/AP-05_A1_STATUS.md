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

## A3 (parziale, onestamente limitato) — consegnato

`src/NosAi.Core/WorldModel/Combat/CombatPlanner.cs` — solo le due tappe
della pipeline che si possono costruire oggi **senza inventare dati**:

- `GenerateCandidates` — un `BasicAttack` per ogni mob ostile+vivo+
  posizionato entro `DefaultBasicAttackRange` (2.0), un `UseSkill` per
  ogni coppia (skill pronta, mob viable) entro `DefaultSkillRange` (6.0).
  Skill "pronta" = `IsUsable.Value == true` e nessun `Cooldown` attivo per
  quello `SkillId`. **Non genera candidati `UseSkill` senza target**
  (self-cast/buff): `Skill` (AP-01) non porta alcun fatto su se una skill
  richieda un target o sia auto/AoE-castabile — generare comunque un
  candidato self-cast per ogni skill pronta avrebbe indovinato una
  distinzione che il dato non fa. Vedi "Gap dati reali" sotto.
- `CheckHardConstraints` — range, validità target (ostile/vivo/
  posizionato), esistenza/prontezza skill. **Non controlla un costo in
  risorsa** (mana/stamina): `Skill` non porta alcun costo, quindi un
  controllo reale non è scrivibile. Nessun controllo fittizio aggiunto.

Test: `tests/NosAi.Core.Tests/WorldModel/Combat/CombatPlannerTests.cs`
(19 test, tutti verdi). `dotnet build NosAi.sln -c Release`: 0 errori (1
warning preesistente non collegato). `dotnet test .../NosAi.Core.Tests.csproj -c Release`:
**463/463**, 0 falliti (444 precedenti + 19 nuovi, zero regressioni).

## Gap dati reali — non affrontati qui, segnalati esplicitamente

`Skill` (AP-01, `src/NosAi.Core/WorldModel/StatusEffectContracts.cs`)
porta solo `Id`/`Name`/`Level`/`IsUsable` — **nessun danno, nessun costo
risorsa, nessuna indicazione se richiede un target**. Questo blocca,
onestamente, non per pigrizia:

- **`CombatSimulationResult`/`ComboPlan` reali** ("short-horizon
  simulation" + "combo prefix" della DoD di AP-05): senza un danno/tempo
  di cast/costo reali per skill, qualunque "simulazione" sarebbe una
  formula inventata spacciata per predizione — esattamente il tipo di
  dato simulato etichettato come reale che l'architettura vieta. Serve
  prima una fonte dati reale (tabella statistiche skill dal client, sullo
  stesso modello di `MapGridExtractor` per la geometria mappe) oppure uno
  storico osservato (danno HP-delta osservato dopo l'uso di una skill,
  correlato nel tempo — la tappa "learn" della DoD, che la roadmap
  canonica assegna comunque ad **AP-09 Memory/Learning/Simulation**, non
  ad AP-05). Non si inventa una formula qui.
- **Candidati `UseSkill` self-cast/buff**: bloccato dalla stessa assenza
  di un fatto "questa skill richiede un target?" su `Skill`.

Questi sono candidati per "Candidati da investigare" in
`docs/agents/DEEPSEEK_TASKS.md`, non un blocco per il resto di AP-05: la
generazione candidati + i vincoli reali restano utili così come sono
(es. per un futuro comando operatore `--engage` nello stile di
`--scout`, che userebbe `BasicAttack`/`UseSkill` verso mob senza
bisogno di una simulazione di danno per decidere *se* attaccare, solo
*se è lecito* farlo).

**Livello di verifica A3 (parziale):** `Present`, stesso motivo di sopra.

## Indagine su Gate3Runtime per skill/attacco — conclusa

Stessa domanda già fatta per il movimento in AP-04, questa volta per
`UseSkill`/`UseBasicAttack`/contrattacco. Un agente read-only ha
verificato riga per riga `Gate3Runtime.cs`, `SimulationEngine.Simulate`,
`GameReferenceDatabase`, `InputActionEffector.cs`, `PostConditions.cs`.
**Stessa classe di problema del movimento, non un caso più maturo:**

1. **Generazione candidati** (`ActionPlanner.Plan`, righe ~346-397):
   reale come *meccanismo* ma con scelta della skill **letterale e
   hardcoded** — `SkillOrItemId = 201` compare esattamente una volta in
   tutto il file, nessuna logica di selezione ("quale skill è la
   migliore ora"), nessuna lettura di `Cooldown`/risorsa reale (grep su
   `Cooldown` in `Gate3Runtime.cs`: zero risultati).
2. **`SimulationEngine.Simulate`** per `UseSkill`/`UseBasicAttack`: stessa
   natura di placeholder fisso già trovata per `MoveToPosition`
   (`hpDelta=-15`/`mpDelta=-35`/`timeMs` letterali, mai letto
   `candidate.SkillOrItemId` — quindi la "predizione" è identica per
   *qualunque* skill).
3. **Nessun dato reale di danno/costo/cast-time/range per skill esiste
   nel repository**: `GameReferenceDatabase` (importato da `Skill.dat`
   del client) esiste ed è reale, ma **dichiara esplicitamente** di non
   decodificare semanticamente quei campi ("quale slot di `ATTRIB` sia
   l'elemento... indovinarlo qui metterebbe un numero che nessuno ha
   verificato dentro un calcolo di danno"). Un vero tracker di cooldown
   da rete esiste (`SkillCooldownTracker`, decodifica pacchetto `sr`) ma
   **non è cablato da nessuna parte** fuori dal proprio file/test.
4. **Verifica post-azione, l'unico punto realmente più maturo del
   movimento**: `UseBasicAttackPostCondition`/`UseSkillPostCondition`
   (`PostConditions.cs`) controllano davvero HP/MP osservati da rete
   (direzione, non magnitudine) — non un placeholder. **Ma** sono
   strettamente accoppiate all'infrastruttura privata di
   `Gate3Runtime` stesso (`Gate3WorldState`, `ReadBackAsync` — privato,
   istanza —, `CollectSightings` — privato, statico): non riusabili da
   un comando indipendente senza duplicare quella lettura, a differenza
   di `WalkCommand.Execute` che per il movimento era già un blocco
   indipendente e completo.
5. Il gate di autorizzazione tattica (`GuardPolicyEngine.Evaluate`,
   soglia `RiskScore > 0.75f`) decide sì/no leggendo esattamente i numeri
   fabbricati del punto 2 — usarlo per candidati AP-05 vorrebbe dire far
   decidere Guard su dati finti travestiti da predizione reale.

**Conclusione, coerente con AP-04:** nessun bridge verso
`Gate3Runtime.ActionPlanner`/`SimulationEngine`/`GuardPolicyEngine` per
le stesse ragioni già scritte per il movimento (X1/X2/X3 dei documenti
precedenti, qui confermati identici per il combattimento). **A differenza
del movimento**, non esiste un equivalente di `WalkCommand.Execute` già
reale e indipendente da riusare per l'esecuzione — andrebbe scritto da
zero, e la sua verifica onesta ha un vincolo in più: `Mob.Status.Resources`
(HP del bersaglio nel World Model canonico) non è mai popolato oggi — la
fusione entità Mob/Npc resta esplicitamente rimandata da AP-02
(`AP-02_STATUS.md`, stesso blocco ML/OCR). Verificare "l'MP del player è
sceso dopo una skill" sarebbe onestamente costruibile oggi (i vitali del
player sono già fusi, AP-02); verificare "l'HP del mob bersaglio è sceso
dopo un attacco" **non lo è**, per lo stesso gap di percezione, non per un
problema di questa fase.

**Decisione: AP-05/A2+A4 non è ancora pronto per una specifica precisa
come `--scout`.** Serve prima una decisione esplicita su una di due
strade, non un'altra indagine a sorpresa dopo aver già scritto la
specifica:

- (a) costruire una verifica combattimento indipendente da
  `Gate3Runtime`, basata solo sui vitali del player già fusi (onesta ma
  parziale: conferma "l'atto ha avuto un costo", non "ha colpito il
  bersaglio"), oppure
- (b) prima chiudere il gap di fusione HP-mob in AP-02 (dipendenza da
  OCR/detection già segnalata come bloccata), poi tornare su AP-05/A2+A4
  con una verifica completa.

Nessuna delle due è iniziata. Segnalato in `docs/agents/DEEPSEEK_TASKS.md`
come candidato da investigare/decidere con l'utente, non come task
DeepSeek pronto.
