# NosAiProject — Coda di lavoro DeepSeek

**Versione:** 1.0
**Data:** 2026-09-05
**Stato:** ATTIVO — DeepSeek sostituisce Cursor interamente, per ogni fase (`docs/agents/AGENT_COMMAND_REGISTRY.md` v1.1)

Questo file è il punto di ingresso unico per DeepSeek. Leggilo per intero prima
di aprire qualunque comando di fase. Non serve altro contesto per capire cosa
fare: le sezioni sotto dicono esattamente quali file sono pronti oggi, quali
arriveranno a breve e con quale ordine, e la regola sotto è assoluta —
nessuna eccezione, nessuna fase in cui non si applica.

---

## REGOLA ASSOLUTA — vale per sempre, su ogni file, senza eccezioni

> **Ogni file consegnato da DeepSeek deve essere completo al 100%, deve
> compilare, e non deve contenere nessuna forma di omissione o codice
> fittizio.**

Concretamente, è **vietato per sempre**, su qualunque file di produzione o di
test:

1. `// TODO`, `// FIXME`, `// da completare`, `// implementare dopo`, o
   equivalenti in qualunque lingua.
2. Pseudocodice, ellissi (`...`), metodi con corpo vuoto o che lanciano
   `NotImplementedException` come consegna finale.
3. Codice commentato "da sostituire poi" invece del codice vero.
4. File intenzionalmente rotti o parziali "tanto poi si sistema".
5. Dati finti, mock o stub al posto di un provider reale su un percorso di
   produzione/critico (i mock sono ammessi **solo** nei test isolati, mai nel
   codice che gira nel runtime reale).
6. Un file dichiarato pronto che in realtà non compila, o i cui test non
   passano.
7. Riscritture parziali quando il comando chiede il file completo: se un
   file esistente va riscritto, va consegnato per intero, non a frammenti.

**Se un file non può essere completato per una dipendenza mancante**: non si
consegna un file incompleto. Si ferma il lavoro su quel file, si scrive
esattamente cosa manca (quale tipo, quale contratto, quale file di un altro
agente) e si segnala — mai un placeholder al posto della dipendenza mancante.

**Se un file non può essere davvero implementato senza dati/hardware reali
che non esistono in questo repository** (es. un modello ONNX addestrato, un
dataset OCR): non si inventa un'implementazione finta che sembra funzionare.
Si dichiara esplicitamente `UNKNOWN`/fail-closed per quel caso, esattamente
come fa già il resto del codice in questo repository (vedi
`NullObjectDetector`, `ScreenVitalsCapture`, `MapReconstructionSource` per
l'esempio dello stile richiesto: ogni modo di fallimento produce un risultato
onesto, mai un dato inventato).

Questa regola non è specifica di una fase: si applica a ogni file che
DeepSeek scrive da oggi in poi, per tutta la durata del progetto.

---

## REGOLA ASSOLUTA #2 — DeepSeek scrive SOLO ED ESCLUSIVAMENTE codice

> **DeepSeek esiste in questo progetto per un solo motivo: scrivere,
> compilare e testare codice. Ogni token che non serve direttamente a
> questo è un token sprecato — non è ammesso, senza eccezioni.**

Concretamente, i token di DeepSeek vanno usati **solo** per:

1. Leggere i file che il comando di fase elenca esplicitamente (sezioni
   "Già costruito"/"OWN"/"MODIFY") — mai un'esplorazione libera del
   repository. Se serve leggere un file non citato per capire una
   dipendenza reale, va bene leggere *quel file*, non "guardare in giro".
2. Scrivere i file di codice e di test che il comando richiede.
3. Compilare ed eseguire i comandi `dotnet build`/`dotnet test` che il
   comando richiede.
4. Scrivere **esclusivamente** il report minimo di completamento che
   `CLAUDE.md` richiede (task, file toccati, comando eseguito e risultato
   esatto, livello di verifica, blocchi, handoff) — nessuna riga in più.
   **Sempre in italiano**: il report minimo e qualunque frase rivolta
   all'utente (anche fuori da questo file, es. nella chat di Cursor) sono
   sempre in italiano. Codice, identificatori, nomi di file/metodo/tipo e
   commenti nel codice sorgente restano in inglese standard, come in
   tutto il resto del repository — questa regola riguarda solo la
   comunicazione, mai il codice.

**Vietato sempre, senza eccezioni:**

- Scrivere qualunque file `.md`, riepilogo, analisi, proposta alternativa,
  spiegazione architetturale non richiesta, o commento esteso al di fuori
  del codice stesso e del report minimo del punto 4.
- Aggiornare `docs/agents/EXECUTION_QUEUE.md`, `docs/agents/DEEPSEEK_TASKS.md`
  o qualunque documento di stato/fase — **non è più compito di DeepSeek**:
  DeepSeek riporta (punto 4), Claude aggiorna la coda leggendo quel report.
  Se le istruzioni più sotto in questo file dicono il contrario in un punto
  più vecchio, questa regola vince: è più recente e più stretta.
- Discutere nel testo di risposta, proporre design alternativi, o motivare
  scelte oltre a quanto serve a un handoff minimo. Se il comando è chiaro,
  si esegue senza commento. Se blocca su una dipendenza mancante, si
  applica solo la REGOLA ASSOLUTA sopra (si ferma, si dice in poche righe
  cosa manca) e nient'altro.
- Qualunque azione, lettura o output che non porti direttamente a un file
  di codice scritto/compilato/testato o al report minimo del punto 4.

Il criterio unico: **se un token non sta scrivendo codice, compilando
codice, testando codice, o riportando l'esito minimo richiesto, quel
token è sprecato e quell'azione non va fatta.**

---

## Come usare questo file

1. Guarda la sezione **"Pronto ora"** qui sotto. Se è vuota, guarda
   **"Prossimo lotto"** per sapere cosa sta per arrivare e perché non è
   ancora pronto.
2. Per ogni file in **"Pronto ora"**, apri il comando di fase indicato
   (`docs/agents/phases/AP-XX/...`) e leggi *solo* quello più le dipendenze
   che elenca — non serve leggere l'intero repository.
3. Implementa, testa (`dotnet build`/`dotnet test` sui progetti toccati),
   poi riporta solo: file toccati, comando build/test eseguito, risultato
   esatto (non "funziona", il numero di test passati). **Non aggiornare
   `docs/agents/EXECUTION_QUEUE.md` né alcun altro file di stato** — vedi
   REGOLA ASSOLUTA #2 sopra: quello resta compito di Claude, a valle del
   report.
4. Non toccare mai un file di proprietà di un altro agente (Claude possiede
   sempre A1/A3/A5/A6 — contratti, algoritmi, test/doc, integrazione). Vedi
   `docs/agents/FILE_OWNERSHIP_MATRIX.md` e
   `docs/agents/AGENT_WORK_PROTOCOL.md` per le regole complete di
   sincronizzazione multi-agente, se serve il dettaglio.

---

## Ruolo di DeepSeek nel progetto

**Principio (istruzione esplicita dell'utente, 2026-09-05): Claude gestisce
il lavoro — quindi decide ownership, scrive i contratti fondanti, fa audit e
integrazione — ma continua anche lei a programmare, su compiti più piccoli;
DeepSeek riceve il carico più pesante di sviluppo/compilazione file. I due
lavorano in parallelo, non in sequenza rigida, per finire prima.** Non è "Claude
scrive le specifiche, DeepSeek scrive il codice": Claude scrive codice reale
anche lei, ogni fase, insieme a DeepSeek — solo che la porzione più grande e
più pesante va sempre a DeepSeek.

In pratica, dentro la topologia a 6 agenti (`docs/agents/AGENT_WORK_PROTOCOL.md`):

- **A1** (contratti) e **A3** (algoritmi puri) restano di Claude — sono
  tipicamente i file più piccoli e devono esistere prima che il resto possa
  partire, quindi Claude li scrive per primi e in fretta, non perché siano
  "il suo lavoro riservato".
- **A2** (adapter di osservazione/estrazione) e **A4** (wiring
  runtime/integrazione) — che finora, in questo progetto, sono anche stati
  i blocchi di lavoro più grandi (in AP-03, A4 da solo: 8 file, 1421 righe,
  contro le ~250 di A1+A2+A3 insieme) — vanno a DeepSeek.
- **A5** (test/audit indipendente) e **A6** (integrazione finale) restano di
  Claude: richiedono di vedere l'intero risultato combinato.
- **Appena A1 esiste** (spesso nel giro di poco, essendo il pezzo più
  piccolo), A2 e A4 partono per DeepSeek **subito**, in parallelo — non si
  aspetta che Claude finisca anche A3 prima di far partire DeepSeek, a meno
  che A2/A4 dipendano davvero da A3 (da dichiarare esplicitamente nel
  comando se succede). L'obiettivo è avere sempre sia Claude sia DeepSeek al
  lavoro nello stesso momento sulla stessa fase.

---

## Pronto ora

**CONSEGNATI E INTEGRATI** (`--scout` AP-04 Q-029/043/044, `--engage`
AP-05 Q-039/045, `--collect` AP-06 Q-041/047/048, ledger AP-09
Q-061/063), più **consegnati livello `Present`** (build+test verdi,
nessuna regressione, nessun client reale disponibile per `Verified`):
`TargetStateComposer` in `ScreenVitalsCapture` (AP-02, Q-067/068),
wiring `PortalCrossingDetector` in `--scout`/`--autoplay` (AP-04,
Q-071/072), rilevamento presenza/assenza finestra di dialogo cablato in
`ScreenVitalsCapture` (AP-02, Q-075/076). Nessuno di questi è un bridge
verso `Gate3Runtime` (indagini concluse: quella pipeline è chiusa/
hardcoded — lavoro futuro di AP-08).

**CONSEGNATO E INTEGRATO** (2026-09-06): `--route <mapId> <x> <y>`
(Q-078/Q-079) — enumerazione mappe persistite + comando diagnostico
read-only sui `Portal` reali via `MultiMapRoutePlanner` (Q-077). Nessun
difetto trovato in audit.

**Nessun task DeepSeek pronto in questo momento** — AP-04/AP-05/AP-06/
AP-08 sono tutte `Integrated`; AP-07 (equip/unequip/upgrade) resta
genuinamente bloccato (vedi "Candidati da investigare" sotto); i gap
residui su AP-09/AP-10 restano OCR/ONNX o dati item non decodificati
semanticamente, non chiudibili scrivendo altro codice di fase. Vedi
`docs/agents/EXECUTION_QUEUE.md` per lo storico completo dei Q-number.

---

## AP-04 — chiuso

`Integrated` (Q-029/043/044/077/078/079). Storico completo in
`docs/agents/EXECUTION_QUEUE.md` e `docs/agents/phases/AP-04/AP-04_A1_STATUS.md`.
Il routing multi-mappa via portali resta un candidato futuro, non un
task pronto (vedi "Candidati da investigare").

---

## Candidati da investigare (non ancora specificati — non iniziare senza conferma)

Gap noti e reali, documentati dagli agenti precedenti, ma non ancora ridotti
a una specifica di file precisa. Vanno investigati prima di diventare un
task DeepSeek — se vuoi che Claude apra uno di questi come prossimo lotto
indipendente da AP-04, chiedilo esplicitamente.

- ~~**Fonte dati reale per i portali**~~ — **verificato e specificato
  (Q-071)**: la pista file client (`taletool`, solo `UPSTREAM.md` di
  riferimento, non integrabile) resta bloccata, ma "osserva mentre
  attraversi" da memoria (`ClientMemorySession.TryReadMapId`, offset già
  provato e wired) è reale. Algoritmo puro già scritto e integrato
  (`PortalCrossingDetector`, Claude); wiring A2+A4 specificato per
  DeepSeek in `docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_portal_crossing_wiring.md`.
- ~~**`TargetStateComposer` non cablato in `ScreenVitalsCapture`**~~ —
  **verificato e specificato (Q-067)**: non bloccato da OCR/ONNX,
  `TargetRoiCalibration`/`ScreenTargetFrameSource`/`TargetStateComposer`
  già reali e già usati lato wire da `TargetAwareGameplayProvider`. Vedi
  `docs/agents/phases/AP-02/AP-02_A2A4_DEEPSEEK_target_state_wiring.md`.
- ~~**Lettura inventario/finestre di dialogo**~~ — **verificato e
  diviso**: inventario da schermo **non vale la pena** (il canale di rete
  `ivn`/`get`/`drop`, già usato da `--collect`, fornisce già conteggi
  esatti); contenuto testuale dei pannelli di dialogo resta bloccato da
  OCR/ML (nessun opcode NosTale per dialogo/quest text in questo
  repository); **presenza/assenza** di un pannello aperto costruita
  (Q-073, A1+A3 Claude). Vedi `docs/agents/phases/AP-02/AP-02_STATUS.md`
  §12.
- ~~**Statistiche reali per skill**~~ — **parzialmente sbloccato**
  (Q-080, Claude, A1+A3): `SkillReferenceDecoder` promuove a colonne
  tipizzate `COST`/`LEVEL`/`TARGET` per intero e i soli campi `DATA`
  risolti con sicurezza (`CastTime`/`Cooldown`/`MpCost`), più i
  riferimenti d'effetto `BASIC`→`BCardApplication` (struttura, non
  interpretazione). Non ancora `Verified`: nessun valore incrociato
  contro un client reale. Restano onestamente non decodificati i campi
  codificati di cui non esiste una mappatura verificata (elemento, tipo
  d'attacco, arma secondaria, ...) e l'interpretazione semantica di
  `BCardApplication` (serve il catalogo `BCard.dat` e, per alcune
  varianti, altre tabelle) — nessuno di questi è indovinato. **Esteso**
  (Q-081, Claude, A1+A3): `TYPE` per intero e `DATA` per intero
  (DashSpeed/RequiredItemVnum/DataRange/DataTargetRange oltre a
  CastTime/Cooldown/MpCost), mappatura da https://nt-research.github.io/
  (istruzione esplicita dell'utente: citare questa volta). Ancora non
  `Verified`: nessun valore incrociato contro il client reale.
  **Rilevamento aggiornamenti client** (Q-082, Claude): nuovo comando
  `--client-updates`, `ClientDirectoryScanner` nativo (non `taletool`,
  valutato e scartato su istruzione esplicita dell'utente per evitare la
  dipendenza AGPL come processo esterno). Il parsing semantico di
  `quest.dat`/`qstprize.dat`/`npctalk.dat`/`tutorial.dat` per il gap
  missioni segnalato in `AP-06_A1_STATUS.md` resta da fare, nativamente.
- ~~**Riconciliazione `KnowledgeScope`/lifecycle duplicati**~~ —
  **risolto** (Q-064/Q-065/Q-066, su richiesta esplicita dell'utente):
  `KnowledgeScope` unificato su `Memory.KnowledgeScope`;
  `KnowledgeStatus`/`KnowledgeLifecycle` confermati concetti distinti,
  non fusi, ma la proiezione tra i due ora è totale ed esplicita
  (`KnowledgeLifecycleProjection`, corregge un collasso silenzioso reale
  su `Candidate`); `DataSourceKind` confermato duplicazione intenzionale
  per bounded context, chiuso con `docs/adr/ADR-0026-datasourcekind-intentional-bounded-context-duplication.md`
  invece che con codice. Vedi `AP-09_A1_STATUS.md`.
- ~~**Categoria/slot di equipaggiamento e statistiche reali per item**~~ —
  **verificato, genuinamente bloccato**, stessa ragione delle statistiche
  skill: nessun campo tipo/sottotipo item decodificato semanticamente in
  `GameReferenceDatabase`, nessuna mappa dichiarata verso
  `EquipmentSlot`. Non specificabile oggi.
- **AP-07/A2+A4 — esecuzione/verifica equip/unequip/upgrade, genuinamente
  bloccato, indagine conclusa** (`AP-07_A1_STATUS.md` §"AP-07/A2+A4"): a
  differenza di AP-05/AP-06, qui nessun percorso onesto parziale esiste.
  Confermato per ispezione, non assunto: (1) nessuna primitiva di
  esecuzione in nessuno dei due sistemi — `ActionType` (Gate 1-6) non ha
  nemmeno una voce Equip/Unequip/Upgrade, a differenza di
  `CollectGroundItem` che almeno esiste dichiarata-ma-non-implementata;
  (2) equipaggiare è un'interazione UI (drag/doppio-click), non una
  hotkey — nessuna calibrazione screen-space del pannello
  inventario/equipaggiamento esiste (`ScreenProjectionCalibration`
  proietta coordinate di mondo di gioco, non un pannello UI fisso, un
  problema diverso mai affrontato); (3) nessun canale di verifica —
  nessun opcode equip mai identificato in `docs/PROTOCOLLO_NOSTALE.md`,
  e il canale già reale (`InventorySlotReading`, usato per `--collect`)
  non distingue equipaggiato da zaino (`InventoryKind` dichiarato privo
  di significato noto). **Non un task DeepSeek pronto**: serve prima una
  calibrazione UI pannello (nuova infrastruttura, non un tocco a
  margine) o l'identificazione di un opcode di rete equip mai cercato —
  da investigare con una cattura dedicata prima di specificare
  qualunque comando `--equip`/`--upgrade`.
- ~~**Esecuzione/verifica combattimento indipendente da `Gate3Runtime`**~~
  **— deciso, specificato e CONSEGNATO (Q-037/Q-038/Q-039).** Decisione
  presa: percorso (a), verifica solo-vitali-player (onesta ma parziale —
  conferma il costo risorsa, non il colpo sul bersaglio; il percorso (b),
  chiudere prima il gap di fusione HP-mob in AP-02, resta bloccato sul gap
  OCR/ONNX indefinitamente). Contratto mancante `CombatExecutionEvidence`/
  `CombatExecutionResult` scritto e testato
  (`src/NosAi.Core/WorldModel/Combat/CombatExecutionContracts.cs`).
  **A2+A4 eseguiti da DeepSeek il 2026-09-06, livello `Present`:**
  `docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_engage_command.md` —
  `CombatVerificationProjector` (A2) + comando operatore
  `--engage <targetEntityId> <skillId>` (A4), esecuzione via
  `KeybindMap`+`GatedInputBackend.KeyPress` (stessa primitiva reale già
  usata da `WalkCommand`/`SingleStepExecutor`, indipendente da
  `Gate3Runtime`), verifica via `ClientMemorySession.TryReadPlayerVitals`
  prima/dopo (stessa catena `[LIVE]` già validata da `--player-vitals`).
- **AP-06/A4 — comando operatore `--collect`, CONSEGNATO (Q-041).** Indagine
  mirata su AP-06/A2 ("semantic extraction OCR/UI/network"): a
  differenza di ogni altro obiettivo quest, `Collect` ha un canale
  network già reale e già fuso, non bloccato dal gap OCR/ML —
  `ivn`/`get`/`drop` sono decodificati (`GameTrafficObserver.cs`) e
  `GameplayObservationProjector` (AP-01/A2, già `Integrated`) li fonde
  già in `Player.Inventory`/`WorldModelSnapshot.Drops`.
  `QuestGraphPlanner.AssessCollectProgress` (Claude, Q-040, già scritto e
  testato) chiude la parte A2 direttamente in `NosAi.Core`, senza alcun
  lavoro DeepSeek. **A4 eseguito da DeepSeek il 2026-09-06, livello
  `Present`:** specifica in
  `docs/agents/phases/AP-06/AP-06_A2A4_DEEPSEEK_collect_command.md` —
  `CollectCommand`, comando `--collect <x> <y> <vnum> [<requiredCount>]`,
  cammina via `WalkCommand.Execute` (riusato invariato), verifica via
  `LiveObservationGateway.Capture()` + `GameplayObservationProjector` +
  `AssessCollectProgress`, prima/dopo la camminata. 5 test verdi, nessuna
  regressione; limite dichiarato: nessuna scoperta automatica di ground item.

**Esplicitamente fuori portata per DeepSeek** (non richiederli, non sono un
problema di codice mancante):

- OCR reale e decoder ONNX addestrato (`AP-02_STATUS.md` §10) — manca il
  modello/dataset addestrato, non l'architettura. Nessun file può risolverlo.
- I task di validazione reale T-05...T-10
  (`docs/STATO_IMPLEMENTAZIONE.md`) — richiedono un client NosTale live e
  hardware Windows reale, non sono compilabili/verificabili in un ambiente
  di sola scrittura di codice.

---

## Mappa strutturale AP-04 → AP-10 (visibilità completa, non ancora compilabile)

Da `docs/agents/AGENT_COMMAND_REGISTRY.md` v1.1. Questa è la sequenza
**completa** di ogni fase futura e di cosa possiederà DeepSeek (A2 + A4) in
ciascuna — a livello di dominio, non di file esatti, perché i contratti A1
di ognuna non esistono ancora. Ogni riga diventerà una sezione "Pronto ora"
precisa quando la fase corrispondente parte (mai prima che la precedente sia
`Integrated`).

| Fase | A2 DeepSeek (osservazione/estrazione) | A4 DeepSeek (runtime/integrazione) |
|---|---|---|
| AP-04 Exploration/Navigation | adapter di osservazione/evidenza di movimento | ciclo di navigazione a runtime, verifica del movimento |
| AP-05 Combat Intelligence | adapter di osservazione combattimento, evidenza target/stato | orchestrazione azioni a runtime attraverso Guard/Trust/Safety esistenti |
| AP-06 Quest Intelligence | adapter osservazione quest e UI/eventi | integrazione stato quest a runtime |
| AP-07 Character/Inventory/Equipment | adapter osservazione inventario/personaggio client-osservabile | integrazione controllo personaggio a runtime e verifica |
| AP-08 Strategic Autonomy | adapter di aggregazione attenzione/stato | integrazione orchestratore/ciclo di vita a runtime — **include** i tre pezzi mancanti trovati dall'indagine AP-04 (`AP-04_A1_STATUS.md`): un `Goal`/sorgente-candidati non legata alla caccia dentro `Gate3Runtime.ActionPlanner`, una `PredictedOutcome` reale per `MoveToPosition` (oggi placeholder fisso), un effettore che guidi `PathWalkController` invece di un click-teleport |
| AP-09 Memory/Learning/Simulation | adapter di persistenza/runtime | bridge memoria runtime e integrazione osservabilità |
| AP-10 Autonomous Certification | adapter di osservazione certificazione | integrazione runtime/test-center |

Ognuna di queste diventa un task reale, con file/firme esatti, solo quando:
(a) la fase precedente è `Integrated`, e (b) Claude ha scritto il relativo
A1. Non iniziare a scrivere codice per una riga di questa tabella prima che
compaia in "Pronto ora" con una specifica precisa — il rischio è scrivere
codice contro una forma che cambia, esattamente il tipo di lavoro da
disfare che questo progetto evita da sempre.

---

## Riferimenti

- `CLAUDE.md` — protocollo generale del progetto (leggilo comunque, la
  regola assoluta sopra ne è un'applicazione diretta ma non l'unica cosa che
  conta: niente stub anche fuori da questa lista, mai bypassare Safety, mai
  dichiarare `Verified` senza evidenza reale).
- `docs/agents/AGENT_WORK_PROTOCOL.md` — topologia a 6 agenti, regole di
  sincronizzazione.
- `docs/agents/AGENT_COMMAND_REGISTRY.md` — dominio/percorsi di proprietà
  per fase (v1.1: DeepSeek al posto di Cursor ovunque).
- `docs/agents/FILE_OWNERSHIP_MATRIX.md` — proprietà per dominio.
- `docs/agents/EXECUTION_QUEUE.md` — fonte di verità su cosa è
  `DONE`/`IN_PROGRESS`/`PENDING`/`BLOCKED` in questo momento.
- `docs/agents/phases/AP-03/AP-03_A4_CLAUDE_persistence_and_wiring.md` —
  esempio del livello di dettaglio/precisione che ogni futuro comando
  DeepSeek avrà.
