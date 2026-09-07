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

**Contesto d'uso (2026-09-06): DeepSeek gira via API dentro una chat di
Cursor, non come sessione persistente.** Ogni messaggio di quella chat ha
un costo diretto e non condivide contesto con questo repository se non
tramite i file che legge — quindi vale ancora di più, non di meno, la
regola sopra: niente testo nella chat di Cursor oltre a (a) la conferma
di aver letto il comando di fase citato, (b) il report minimo finale del
punto 4. Concretamente, nella chat di Cursor sono **sempre vietati**:

- Preamboli ("Certo, procedo a implementare...", "Ho capito il task,
  ora...", "Fammi analizzare...") prima di agire.
- Ripetere o riassumere il comando di fase appena letto — chi legge il
  report ha già quel file, ripeterlo è puro spreco.
- Chiedere conferma su qualcosa che il comando di fase ha già deciso
  esplicitamente. Una domanda è ammessa **solo** quando il comando manca
  di un'informazione necessaria per procedere (dipendenza non ancora
  scritta, ambiguità reale tra due letture del testo) — e in quel caso va
  posta una volta sola, in una riga, non come discussione.
- Narrare i passaggi intermedi ("ora scrivo il file X", "ora eseguo la
  build") invece di eseguirli e riportare solo l'esito.

---

## REGOLA ASSOLUTA #3 — Correttezza verificata, mai copiata per fiducia

> **Ogni riga di codice, commento o test consegnato deve essere vero
> perché DeepSeek lo ha verificato contro il codice reale — mai perché il
> comando di fase lo affermava.** Un comando di fase è scritto da Claude
> prima di vedere il codice che DeepSeek scriverà: può contenere un errore.
> Copiarlo alla cieca in un commento o in un test lo trasforma da errore
> di specifica a bug consegnato.

Incidente reale che ha reso necessaria questa regola (AP-08/A2A4,
2026-09-06 — non un'ipotesi): la specifica
`AP-08_A2A4_DEEPSEEK_recovery_signal.md` affermava che due segnali
(`recovery`/`survival`) "by construction they are never both non-null at
once". È falso — verificabile leggendo `StrategyPlanner.cs` in meno di un
minuto: fuori combattimento con HP nota sotto il massimo, **entrambi**
tornano non-null con la stessa urgenza, ed è il caso comune, non un caso
limite. DeepSeek ha copiato la frase falsa parola per parola come
commento dentro `AutoplayCommand.cs`, consegnandola come fatto accertato
nel codice di produzione. Un audit indipendente successivo l'ha trovata;
Claude l'ha corretta. Il codice funzionava comunque correttamente — il
danno è il commento falso lasciato per i prossimi che leggeranno quel
file.

Concretamente, prima di consegnare:

1. Ogni affermazione di fatto che finisce in un commento di codice
   (perché un ordine è scelto, perché due rami non si sovrappongono,
   perché un valore non può essere nullo) va **riverificata leggendo il
   codice reale che la rende vera**, non assunta dal testo del comando di
   fase. Se non si riesce a verificarla, il commento non si scrive, o si
   scrive come "vedi la spec di fase per il rationale" invece di
   ripeterla come fatto.
2. Ogni test pre-esistente nel file toccato o in file dello stesso
   progetto va **eseguito per intero**, non solo i test nuovi — un test
   che passava prima e fallisce dopo la modifica non è "un problema di
   Claude da segnalare", è la prova che un'assunzione codificata in un
   test vecchio è cambiata: va corretta l'assunzione stantia nel test
   (mai cancellare il test, mai "aggiustare" il codice nuovo per farlo
   quadrare con un'assunzione ormai falsa), e il fix va incluso nella
   stessa consegna.
3. Il numero di test riportato nel completamento (REGOLA ASSOLUTA #2,
   punto 4) è sempre quello dell'**intera suite del progetto toccato**
   dopo la modifica, non solo dei test nuovi — "16 nuovi test verdi" non
   basta se non dice anche se gli altri 2210 lo sono rimasti.

---

## Cartella di lavoro — una sola, sempre

`C:\Users\volob\Desktop\NosAiProject` è l'unica copia di lavoro del progetto.
Apri sempre questa. `git pull` prima di ogni task.

**Due cose sono cambiate il 2026-09-07 e questa sezione diceva il falso su
entrambe.**

**(1) La cartella è un'altra.** Fino a oggi qui c'era scritto
`C:\Users\volob\Desktop\nos\NosAiProject`. Quella cartella **non esiste più**:
era un secondo clone, ed è stata rimossa nel consolidamento chiesto dall'utente.
Prima di rimuoverla sono stati recuperati i suoi file ignorati, che esistevano
solo lì — le cinque catture `.noscap`, le calibrazioni di `data/perception/`,
`keybinds.json`, i file dello scan di memoria e `tools/windivert/`. Ora sono
tutti nella cartella qui sopra.

**(2) Il push automatico non esiste.** Qui c'era scritto che ogni commit viene
pushato su `origin/main` da un hook `post-commit`, «quindi GitHub non resta mai
indietro rispetto al disco». Verificato: `.git/hooks/` di questa cartella **non
contiene alcun hook** — solo i `.sample` di git. Quell'hook viveva nel clone
rimosso. **Devi pushare tu, esplicitamente, alla fine di ogni task**, e dire nel
report di completamento che l'hai fatto.

**Sui 46 commit solo-locali** che questa sezione segnalava come non presenti su
nessun ref remoto: la situazione è risolta. Sono su `origin` sotto i dodici rami
`archivio/*`, ognuno verificato contenuto in un ref remoto prima che il ramo
locale corrispondente venisse cancellato. In locale resta il solo `main`, e i
comandi di quella vecchia procedura non servono più.

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

**INDIPENDENTE DA TUTTI, ANCHE IN PARALLELO** (2026-09-07): **i rifiuti che
nessuno ha mai provato.** src/NosAi.Runtime dichiara **190** costanti
`public const string ...Reason`: sono il vocabolario con cui il runtime rifiuta
per nome invece che con un booleano, e quei nomi finiscono sotto gli occhi
dell'operatore. Confrontate con tutto `tests/`, per nome della costante e per
valore della stringa: **48 sono prodotte e nessun test le verifica**, e **4 non
sono prodotte da nulla** — compaiono una volta sola, nella loro stessa
dichiarazione. Nel gruppo mai verificato ci sono i rifiuti del confine di
attuazione (`authority_not_verified`, `token_integrity_unreadable`,
`actuation_scope_aborted`, i quattro `*_input_backend_not_gated`): i percorsi
che tengono chiusa la porta, e nessuno ha mai controllato che si chiudano. Il
task costruisce un registro nello stile di `DuplicateTypeNameTests` (elenco
dichiarato, un motivo per voce, rosso sia su una voce nuova sia su una diventata
stantia) e copre per intero il gruppo dell'attuazione. **Non tocca un solo file
di `src/`**: se per coprire un rifiuto servisse toccarlo, la specifica dice di
fermarsi e riferire. Specifica:
`docs/agents/phases/AP-10/AP-10_A2A4_DEEPSEEK_refusal_reason_coverage.md`.

**INDIPENDENTE** (2026-09-07): **il registro degli esiti si scrive e non si
legge.** `ActionOutcomeLedgerStore` e' un registro durevole append-only su
SQLite, e quattro comandi ci scrivono davvero — `--scout`, `--autoplay`,
`--engage`, `--recover`: ogni atto reale lascia una riga. Espone una sola
lettura, `LoadByContext`, e **nessun chiamante di produzione**: il runtime
scrive la propria storia e non ha modo di rileggerla. E' la capacita' che
CLAUDE.md chiama «learn from failures» con la meta' di scrittura completa e
quella di lettura assente. Il rapporto nuovo deve anche saper distinguere per
nome quattro situazioni che da fuori si somigliano — volume assente, file
assente, store vuoto, store con righe — perche' su questa macchina il volume
`NOSAI-SSD` non esiste e il registro e' vuoto per costruzione. Non tocca i
quattro comandi che scrivono. **Non in parallelo con `wire_inspect`**: entrambi
toccano `Program.cs`. Specifica:
`docs/agents/phases/AP-09/AP-09_A2A4_DEEPSEEK_outcome_report.md`.

**INDIPENDENTE** (2026-09-07): **`--decide-replay` non arriva mai al
pianificatore, e non lo dice.** Misurato: su 8211 pacchetti reali produce sette
cicli, cinque dei quali si fermano a `player_vitals_stale`. La causa non e' un
difetto — `GameplayProvider.cs:555` sceglie di misurare la freschezza sull'ora
del filo, e su una registrazione ogni lettura e' vecchia di mesi: alla domanda
«questa lettura e' attuale?» la risposta giusta e' sempre no (ADR-0016). Il
difetto e' che l'uscita non lo dice, quindi chi la legge conclude che la
registrazione e' povera o che il pianificatore e' rotto. Il task **non allenta
quella regola**: le mette accanto una seconda domanda dichiarata,
`--as-of-capture`, con un orologio che avanza sui timestamp della cattura — e
tre test fissano che provenienza resta `Cached`, `Acting enabled` resta
`False`, e la modalita' vale solo su file. Il seme esiste gia':
`NetworkGameplayProvider` prende gia' un `TimeProvider`. Non tocca
`Program.cs` ne' i file degli altri task. Specifica:
`docs/agents/phases/AP-09/AP-09_A2A4_DEEPSEEK_decide_replay_as_of_capture.md`.

**INDIPENDENTE, PRENDIBILE ANCHE IN PARALLELO** (2026-09-07): **misurare un
opcode invece di indovinarlo** — comando `--wire-inspect`. Per scrivere le due
specifiche qui sotto e' servito sapere cosa contengono davvero i pacchetti
`lev` e `eq`, e non esiste un modo di saperlo: `--world-replay` da' un
censimento ma non una riga, `--live-decode` stampa le righe ma vuole il driver e
una sessione viva. L'unico modo e' stato scrivere un test usa-e-getta e
cancellarlo, tre volte in un giorno. Meta' del lavoro e' gia' fatto e la
specifica lo dice: `LiveWireMonitor.Monitor` e' gia' puro e prende un
`IPacketSource`, che `CaptureFile.Open` restituisce — va collegato, non
riscritto. La meta' nuova e' il censimento delle forme: per ogni campo di ogni
opcode, se e' sempre lo stesso valore o quanti distinti ne ha assunti. E' la
regola di `CLAUDE.md` sulle fonti esterne resa meccanica: un campo mai cambiato
non si distingue da una costante, quindi la cattura non puo' confermargli un
significato. Non tocca nessuno dei quattro file dei due task qui sotto.
Specifica: `docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_wire_inspect.md`.

**IN CODA, DOPO QUELLO SOPRA** (2026-09-07): **leggere dal filo cosa il
personaggio indossa** — gli opcode `eq` e `equip`. ADR-0027 ha misurato che
**nessun sito di produzione popola `Player.Equipment`**: ogni snapshot mai
prodotto afferma che il personaggio non indossa nulla, e LoadoutPlanner pianifica
su quello. Q-091 aveva concluso che serviva un operatore a lanciare un test e
mandare le righe reali: quel test era già registrato — `data/equip_test.noscap`
è una sessione catturata mentre si equipaggiava e disequipaggiava, e i 43
pacchetti che il decoder scarta sono esattamente quel canale. La verifica non
dipende da alcuna fonte esterna: tre vnum (309, 518, 284) escono da uno slot di
`equip` ed entrano in uno slot di `ivn` dentro la stessa cattura, quindi i due
opcode si confermano a vicenda. Specifica:
`docs/agents/phases/AP-07/AP-07_A2A4_DEEPSEEK_worn_equipment_from_wire.md`.
**Non prenderlo insieme al task sopra**: modificano gli stessi quattro file.

**PRONTO ORA** (2026-09-07): **pubblicare la progressione che il filo già
porta** — l'opcode `lev`. `docs/PROTOCOLLO_NOSTALE.md:236` lo dice in una riga:
«Progression — level and XP from `lev` (catalogued, not yet published)».
L'opcode è censito, i suoi campi sono classificati per confidenza, le
registrazioni ne portano 23 — e `NosTaleWorldProtocolDecoder` non lo legge, per
cui il censimento di `--world-replay` conta quei pacchetti fra quelli buttati
(`8147/8211`). Task interamente offline: le due registrazioni sono su disco,
nessun client reale serve, e nulla di quanto chiesto può attuare alcunché.
Specifica completa, con tutte e 23 le righe reali misurate il 2026-09-07:
`docs/agents/phases/AP-08/AP-08_A2A4_DEEPSEEK_progression_from_lev.md`.

**CONSEGNATO** (Q-084, 2026-09-06): `--calibrate-inventory-panel` —
seconda indagine AP-07/A2+A4 ha trovato un varco reale (non tutto il
blocco): `InventoryKind` ora documentato da fonte esterna citata, e la
calibrazione screen-space del pannello equipaggiamento (il vero collo di
bottiglia rimasto) è specificabile con lo stesso schema già usato per
`TargetRoiCalibration`/`DialogRoiCalibration`. Specifica completa in
`docs/agents/phases/AP-07/AP-07_A2A4_DEEPSEEK_inventory_panel_calibration.md`.
Solo calibrazione — `--equip`/`--unequip` restano non specificabili
finché questa non è confermata da un operatore su un client reale.

**CONSEGNATO E INTEGRATO** (Q-086, 2026-09-06, livello `Present`):
`--calibrate-inventory-panel` — `InventoryPanelCalibrationProbe`
(`TryParseSlots` su un token per valore dichiarato di `EquipmentSlot`,
conta derivata da `Enum.GetValues`; comando console che rispecchia
`HudProbe`: attach client → frame DXGI → area client → preview
`inventory_panel_latest.bmp` → `Confirmed`+`Save` solo su conferma
operatore) + wiring `Program.cs`/`KnownProbeFlags` + preview additiva in
`HudCropWriter`. In integrazione è stata applicata la nota Q-085: la
prima stesura era contro la specifica v1 a 8 slot (pre-correzione
dell'enum) ed è stata riallineata ai 18 valori reali NosTale — la guida
operatore stampa i nomi dall'enum e i test sono scritti su
`Enum.GetValues`, così nessuna assunzione sul numero di slot può
rientrare. **T-12 (calibrazione) confermato dall'operatore il 2026-09-06**
su client reale — `InventoryPanelRoiCalibration` scritta con successo,
vedi Q-089 in `EXECUTION_QUEUE.md`. Resta aperta solo la seconda metà di
T-12 (conferma `InventoryKind` per un equip reale) prima di poter
specificare `--equip`/`--unequip`.

**ATTENZIONE INTEGRAZIONE (Q-085, 2026-09-06)**: dopo l'avvio di questo
task, `EquipmentSlot` è stato corretto (era un segnaposto sbagliato a 8
valori, ora sono i 18 valori reali di NosTale — vedi Q-085 in
`EXECUTION_QUEUE.md`) e la specifica sopra è stata aggiornata di
conseguenza (18 slot/18 token, non più 8). Se questa consegna era già
in corso sulla versione precedente dello spec, in A5/A6 verificare che
non referenzi `EquipmentSlot.Shield`/`.Helmet`/`.Accessory1`/
`.Accessory2` (rimossi) né assuma un conteggio fisso di 8 token/ritagli
invece di 18 — se lo fa, va corretto in integrazione, non rispedito
indietro.

**CONSEGNATO E INTEGRATO** (Q-092, 2026-09-06, livello `Present`): **AP-08 —
segnale `Recovery` in `--autoplay`.** `AutoplayCommand.ExecuteOneCycle`
dispatcha `Recovery` esattamente come `Survival` (stesso slot consumabile,
via `DispatchRecovery` condiviso); `RunWindows` traccia
`CombatRecencyTracker.State` per ciclo per derivare il fatto "in
combattimento adesso" da un calo HP recente. In integrazione sono stati
corretti 2 difetti reali della consegna (vedi REGOLA ASSOLUTA #3 sopra,
introdotta per questo stesso incidente): un test pre-esistente non
aggiornato (`AutoplayCommandTests.cs` elencava ancora `Recovery` come
kind non-dispatchabile) e un commento falso copiato dalla specifica
(vedi Q-092 in `EXECUTION_QUEUE.md` per i dettagli esatti). Build 0
errori/0 warning, Core 654/654, Runtime 2150/2150.

**CONSEGNATO E INTEGRATO** (Q-094, 2026-09-07, commit `349eab1`): **AP-07 —
`--loadout-report`.** Non prenderlo: e' fatto. Audit
indipendente trova che `LoadoutPlanner`'s "nothing decodes an item's
equipment category yet" è falso da quando `ItemReferenceDecoder` (Q-088)
esiste. Lato `NosAi.Core` (Claude, già consegnato e testato):
`LoadoutPlanner.GenerateEquipCandidates(Player, Func<ItemId, EquipmentSlot?>)`
(nuovo, puro) produce un candidato Equip solo quando il lookup risolve
davvero uno slot — mai indovinato. Resta il collegamento meccanico: un
comando **read-only** nuovo che legge inventario/equipaggiamento reali e
risolve gli slot dal catalogo reale su disco. Nessuna esecuzione
equip/unequip (resta fuori scope). Specifica completa in
`docs/agents/phases/AP-07/AP-07_A2A4_DEEPSEEK_loadout_report.md` —
leggila per intero prima di scrivere codice, cita ogni API reale con
file/riga esatti.

Oltre a questo, nessun altro task pronto: AP-04/AP-05/AP-06 sono
`Integrated`; i gap residui su AP-09/AP-10 restano OCR/ONNX o dati
item non decodificati semanticamente, non chiudibili scrivendo altro
codice di fase. Vedi `docs/agents/EXECUTION_QUEUE.md` per lo storico
completo dei Q-number.

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
  dipendenza AGPL come processo esterno). **`quest.dat`/`qstprize.dat`/
  `npctalk.dat`/`tutorial.dat` verificati esistenti** (repository pubblici
  indipendenti `NosWings/ON.NosWings.Parsing`, `BlowaXD/nostale-parsing`),
  ma **nessuna fonte esterna trovata che ne documenti il formato interno**
  (`nt-research.github.io`, unica fonte già verificata per Skill/Item/
  BCard, non li copre). Non promosso a task: resta un'annotazione non
  confermata finché non emerge un parser reale ispezionabile — nessun
  formato inventato.
- ~~**Riconciliazione `KnowledgeScope`/lifecycle duplicati**~~ —
  **risolto** (Q-064/Q-065/Q-066, su richiesta esplicita dell'utente):
  `KnowledgeScope` unificato su `Memory.KnowledgeScope`;
  `KnowledgeStatus`/`KnowledgeLifecycle` confermati concetti distinti,
  non fusi, ma la proiezione tra i due ora è totale ed esplicita
  (`KnowledgeLifecycleProjection`, corregge un collasso silenzioso reale
  su `Candidate`); `DataSourceKind` confermato duplicazione intenzionale
  per bounded context, chiuso con `docs/adr/ADR-0026-datasourcekind-intentional-bounded-context-duplication.md`
  invece che con codice. Vedi `AP-09_A1_STATUS.md`.
- **Categoria/slot di equipaggiamento e statistiche reali per item** —
  **nuova pista reale trovata (2026-09-06), non ancora verificata
  abbastanza per uno unlock**: il sito Itempicker
  (`https://itempicker.atlagaming.eu/`, registrato in
  `docs/research/NOSTALE_COMMUNITY_KNOWLEDGE_2026-09-05.md`) espone
  un'API REST reale e senza autenticazione (`GET /api/items/data/{vnum}`)
  che restituisce `itemType`/`itemSubType`/`equipmentSlot`/
  `inventoryType`/`class` come interi grezzi per ogni item — esattamente
  il campo mancante. Itempicker stesso non documenta il significato di
  quegli interi. OpenNos (GPL, già vaulted) `OpenNos.Domain/ItemType.cs`
  documenta un `ItemType` enum con `Weapon=0` — coerente con l'esempio
  osservato (vnum 1 "Holzstock"/bastone di legno, `itemType:0`), ma è un
  riscontro per nome, non un incrocio con un valore già noto di questo
  repository come richiesto per fidarsene su un percorso non
  diagnostico. **Non ancora specificabile**: serve prima verificare
  anche l'`EquipmentType`/slot enum di OpenNos contro `equipmentSlot`, e
  un incrocio più solido di almeno un valore. Prossimo passo naturale
  per Claude (A1+A3), stesso schema di `SkillReferenceDecoder`.
- **AP-07/A2+A4 — esecuzione/verifica equip/unequip/upgrade** —
  **ri-verificato (2026-09-06), un varco reale trovato** (`AP-07_A1_STATUS.md`
  §"seconda indagine"): il canale di verifica non è più genuinamente
  bloccato quanto si credeva — `InventoryKind` ora documentato da fonte
  esterna citata (OpenNos/GPL), un valore incrociato con una cattura
  reale di questo repository. La calibrazione screen-space del pannello
  resta il vero collo di bottiglia, ma è ora specificabile con lo stesso
  schema già reale di `TargetRoiCalibration`/`DialogRoiCalibration` —
  **task pronto**: vedi "Pronto ora" in cima a questo file (Q-084,
  `--calibrate-inventory-panel`, solo calibrazione). `--equip`/`--unequip`
  restano non specificabili finché quella calibrazione non è confermata
  da un operatore reale e una cattura dedicata non conferma quale
  `InventoryKind` corrisponde a "equipaggiato".
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
