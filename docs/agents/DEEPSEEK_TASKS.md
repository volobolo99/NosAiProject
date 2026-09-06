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

## Come usare questo file

1. Guarda la sezione **"Pronto ora"** qui sotto. Se è vuota, guarda
   **"Prossimo lotto"** per sapere cosa sta per arrivare e perché non è
   ancora pronto.
2. Per ogni file in **"Pronto ora"**, apri il comando di fase indicato
   (`docs/agents/phases/AP-XX/...`) e leggi *solo* quello più le dipendenze
   che elenca — non serve leggere l'intero repository.
3. Implementa, testa (`dotnet build`/`dotnet test` sui progetti toccati),
   poi segna il file come consegnato aggiornando
   `docs/agents/EXECUTION_QUEUE.md` (riga Q-NNN corrispondente) e riportando:
   file toccati, comando build/test eseguito, risultato esatto (non
   "funziona", il numero di test passati).
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

**Sì — AP-04/A2+A4, comando `--scout`.** Specifica completa e precisa:
`docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_scout_command.md`. Non è
un bridge verso `Gate3Runtime` (un'indagine ha confermato che quella
pipeline è chiusa/hardcoded e le manca comunque una predizione di
movimento reale — lavoro futuro di AP-08, non di AP-04): è un nuovo
comando operatore `--scout` che calcola il `NavigationPlan` (già reale,
Claude/A3) e lo esegue chiamando **direttamente** `WalkCommand.Execute`
(già reale, invariato) con `ActuationAuthority.Commanded("--scout")` —
stessa famiglia legittima di `--walk`, zero bypass di Guard/Trust/Safety.
File da creare: `MovementVerificationProjector.cs` (A2, piccolo,
meccanico) + `ScoutCommand.cs` (A4, il lotto pesante) + i rispettivi
test. Un solo file esistente da toccare, in modo additivo e minimo:
`WalkCommand.cs` (un parametro opzionale in più su `Execute`, dettagliato
nella specifica). Tutto il resto nella specifica stessa — seguila alla
lettera, non improvvisare la forma dei parametri.

**Nota sul nome:** il comando si chiama `--scout`, non `--explore` —
scelto apposta per non confonderlo con il namespace/concetto di dominio
`Exploration` (`ExplorationFootprint`, `ExplorationPlanner`, il nome
della fase AP-04 stessa), che restano invariati. Non chiamare nulla
`Explore*` in questo task.

---

## Prossimo lotto: AP-04 — Exploration & Navigation

**A1 fatto** (2026-09-05, in parallelo all'audit A5 di AP-03 — vedi
l'eccezione dichiarata in `docs/agents/EXECUTION_QUEUE.md`):
`src/NosAi.Core/WorldModel/Exploration/ExplorationContracts.cs` —
`ExplorationFootprint`, `FrontierCandidate`, `NavigationWaypoint`,
`NavigationPlan`. 12 test verdi, build pulita. Dettaglio completo in
`docs/agents/phases/AP-04/AP-04_A1_STATUS.md`.

**Indagine conclusa.** `NavigationPathfinding.cs`'s `WorldMapPortalRouter`
fa routing multi-mappa reale come algoritmo (Dijkstra su portali,
deterministico) ma su un grafo **hardcoded** (5 mappe/4 portali, fixture di
test) raggiungibile solo dalla propria suite di test — nessuna fonte dati
reale per i portali esiste da nessuna parte nel repository (`Portal` è
sempre vuoto in ogni `MapModel` prodotto oggi, confermato per grep). Non
c'è quindi nessun bridge/adapter possibile verso quel router: il dato reale
a cui fare da ponte non esiste, stesso blocco di AP-02 con OCR/ONNX (manca
il dato, non l'architettura).

**Ambito ristretto di conseguenza, onestamente**: il prossimo lotto di
AP-04 copre solo esplorazione/routing sulla stessa mappa (nessun portale).
Il routing multi-mappa via portali resta un candidato in "Candidati da
investigare" sotto, non uno spunto per DeepSeek adesso.

**A3 fatto** (2026-09-05): `ExplorationPlanner` (footprint, ranking
frontiera, `NavigationPlan` a singolo waypoint) + il contratto A1
mancante (`MovementExecutionEvidence`/`MovementExecutionResult`). 27 test
verdi. Dettaglio in `AP-04_A1_STATUS.md` §"A3 + contratto A1 mancante".

**A2+A4 pronto ora**, vedi "Pronto ora" in cima a questo file — non un
bridge verso `Gate3Runtime` (indagine conclusa: quella pipeline è
chiusa/hardcoded, le manca una predizione di movimento reale, e il suo
effettore per `MoveToPosition` è un click-teleport, non una camminata
verificata — tre pezzi mancanti che sono lavoro futuro di AP-08
"Strategic Autonomy + HTN", non di AP-04). Il ponte reale e costruibile
oggi è un nuovo comando operatore `--scout` che chiama direttamente
`WalkCommand.Execute` (già reale, Gate-1-verificato, invariato) con
un'authority `Commanded` — stessa famiglia legittima di `--walk`, nessun
bypass di Guard/Trust/Safety, nessuna authority "autonoma" inventata
(ADR-0020 ne vieta una terza).

Per studiare in anticipo il dominio (non per scrivere codice ancora):
`third_party/sources/ikpil/DotRecast/` (navmesh/pathfinding di riferimento),
`third_party/sources/ptrefall/FluidHTN/` (pianificazione gerarchica),
`src/NosAi.Core/Navigation/` e `src/NosAi.Runtime/Navigation/` (contratti e
codice di navigazione già esistenti, sistema Gate 1-6 pre-canonico, da non
confondere con i nuovi contratti AP-04).

---

## Candidati da investigare (non ancora specificati — non iniziare senza conferma)

Gap noti e reali, documentati dagli agenti precedenti, ma non ancora ridotti
a una specifica di file precisa. Vanno investigati prima di diventare un
task DeepSeek — se vuoi che Claude apra uno di questi come prossimo lotto
indipendente da AP-04, chiedilo esplicitamente.

- **Fonte dati reale per i portali** (identità, mappa sorgente/destinazione,
  posizione): non esiste nel repository. `NosAi.Navigation.Pathfinding.
  WorldMapPortalRouter` ha un grafo di routing multi-mappa reale come
  algoritmo ma su dati hardcoded/fixture. Blocca `NavigationPlan` con più
  di un waypoint (AP-04) finché non esiste. Possibile pista:
  `third_party/sources/taletool/` (parsing offline di dati client) o
  lettura da memoria client in stile `MapIdFinder`/`TargetIdFinder` — da
  verificare prima di specificarlo come task.
- **`TargetStateComposer` non cablato in `ScreenVitalsCapture`**
  (`docs/agents/phases/AP-02/AP-02_STATUS.md` §10): serve una
  `TargetRoiCalibration` calibrata. Esiste già un sistema di calibrazione
  schermo (`ScreenProjectionAutoCalibrator`/`ScreenProjectionCalibration`/
  `ScreenProjectionProbe`/`ScreenProjectionWatcher` in
  `src/NosAi.Runtime/Perception/`) che potrebbe non essere bloccato da
  asset ML mancanti — da verificare prima di specificarlo come task reale.
- **Lettura inventario/finestre di dialogo** (stesso documento): nessun
  codice di lettura esiste. Potenzialmente bloccato da OCR reale (asset ML
  mancante, vedi sotto) o parzialmente affrontabile senza OCR per gli slot
  con icone fisse — da verificare.
- **Statistiche reali per skill** (danno, costo risorsa, se richiede un
  target) — `docs/agents/phases/AP-05/AP-05_A1_STATUS.md` §"Gap dati
  reali": `Skill` (AP-01) porta solo `Id`/`Name`/`Level`/`IsUsable`.
  Blocca una simulazione di combattimento reale (AP-05) e la generazione
  di candidati self-cast/buff. Possibile pista: tabella statistiche skill
  dal client (stesso genere di lavoro di `MapGridExtractor` per la
  geometria) — da verificare prima di specificarlo come task.
- **Riconciliazione `KnowledgeScope`/lifecycle duplicati** —
  `docs/agents/phases/AP-09/AP-09_A1_STATUS.md`: `KnowledgeScope` è
  dichiarato identicamente sia in `NosAi.Core.Memory` che in
  `NosAi.Core.Knowledge` (con una funzione di mappatura manuale tra i
  due); il lifecycle è frammentato in due state machine diverse
  (`Memory.KnowledgeStatus` a 7 stati vs `Knowledge.KnowledgeLifecycle`
  a 7 stati diversi). Stesso genere del gap `DataSourceKind` già
  segnalato da AP-01/A5. Non risolto: sistemare due enum già in uso da
  codice e test reali richiede un comando dedicato con verifica di
  regressione completa, non un tocco a margine di un'altra fase.
- **Categoria/slot di equipaggiamento e statistiche reali per item** —
  `docs/agents/phases/AP-07/AP-07_A1_STATUS.md`: `InventoryItem` (AP-01)
  non porta a quale `EquipmentSlot` un item corrisponde, né statistiche
  di combattimento/costo upgrade. Blocca la generazione automatica di
  candidati Equip e il popolamento reale di `LoadoutEvaluation`. Stesso
  sospetto di AP-05: probabilmente la stessa fonte dati client
  (`GameReferenceDatabase`/`Item.dat`) non ancora decodificata
  semanticamente — da verificare prima di specificarlo come task.
- ~~**Esecuzione/verifica combattimento indipendente da `Gate3Runtime`**~~
  **— deciso e specificato (Q-037/Q-038/Q-039).** Decisione presa:
  percorso (a), verifica solo-vitali-player (onesta ma parziale — conferma
  il costo risorsa, non il colpo sul bersaglio; il percorso (b), chiudere
  prima il gap di fusione HP-mob in AP-02, resta bloccato sul gap OCR/ONNX
  indefinitamente). Contratto mancante `CombatExecutionEvidence`/
  `CombatExecutionResult` scritto e testato
  (`src/NosAi.Core/WorldModel/Combat/CombatExecutionContracts.cs`).
  Specifica DeepSeek pronta e precisa (task A2+A4, ora un task DeepSeek
  pronto, non più "da investigare"):
  `docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_engage_command.md` —
  comando operatore `--engage <targetEntityId> <skillId>`, esecuzione via
  `KeybindMap`+`GatedInputBackend.KeyPress` (stessa primitiva reale già
  usata da `WalkCommand`/`SingleStepExecutor`, indipendente da
  `Gate3Runtime`), verifica via `ClientMemorySession.TryReadPlayerVitals`
  prima/dopo (stessa catena `[LIVE]` già validata da `--player-vitals`).

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
