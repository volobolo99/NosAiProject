# Controllo del personaggio — attuazione e verifica

**Versione:** 2.1
**Data:** 1 settembre 2026
**Ruolo:** documento **operativo**. Principi e invarianti in
`CONTROLLO_PERSONAGGIO_ARCHITETTURA.md`, citati per identificatore `DOMAIN-xx`.
**Canale:** input del sistema operativo (ADR-0019).

---

## 1. Che cosa esiste già

Perché questo documento non riproponga lavoro fatto, l'inventario prima delle prescrizioni.

| Componente | Stato | Nota |
|---|---|---|
| `Win32InputBackend` | presente | `SendInput`, coordinate assolute normalizzate 0–65535. Nessun `PostMessage` in soluzione |
| `GatedInputBackend` | presente | Barriera al confine: la decisione è presa a ogni chiamata dalla policy viva, mai passata dal chiamante. Rifiuti contati e diagnosticabili |
| `InputEnvironmentProbe` | presente | Verifica che `SendInput` raggiunga davvero la coda di input di questo desktop |
| `ClientWindowLocator` | presente | `GetClientRect` + `ClientToScreen` |
| `ScreenProjectionCalibration` | presente | Trasformazione affine misurata `screen = A·Δmap + anchor`, tre campioni non collineari, rifiuto del fit quando l'ancora cade fuori finestra |
| `ScreenProjectionAutoCalibrator` | presente | Campiona da solo cliccando e rileggendo il quadrato che il client ha risolto |
| `ScreenProjectionWatcher` | presente | Campiona osservando i click dell'operatore |
| `NavigationPathfinding` | presente | A\* 2D, mappe di collisione, heatmap, portali, rilevazione di stallo. `TileType.Unobserved` non calpestabile |
| `NosArchive` | presente | Lettore degli archivi del client |
| `SafetyGate` | presente | Verdetto della guardia prima della policy; il rifiuto è motivato, non un `false` nudo |

**Quello che manca** è elencato in § 2, § 3 e § 4, e nient'altro in questo documento è nuovo.

---

## 2. I tre buchi dell'atto

### 2.1 Commit point

`GatedInputBackend` decide su ogni chiamata, ma decide sulla **policy**: se l'input live è
abilitato. Non rivalida nulla del **mondo fisico** nell'istante dell'emissione. Fra il
momento in cui la pipeline autorizza un click e il momento in cui il click parte, l'operatore
può aver spostato la finestra, portato in primo piano un'altra applicazione, o mosso il
mouse.

`SendInput` va a chi ha il focus. Un click autorizzato su coordinate corrette, emesso mezzo
secondo dopo che il browser è passato davanti, è un click nel browser.

Regola (`DOMAIN-17`): un programma d'input ha al massimo un passo irreversibile ed è
l'ultimo. Subito prima di quel passo:

```
COMMIT:
    epoca di geometria invariata dall'autorizzazione   altrimenti ABORT
  ∧ finestra in primo piano == finestra di sessione    altrimenti ABORT
  ∧ WindowFromPoint(p) risale alla finestra di sessione altrimenti ABORT
  ∧ nessun input umano nella finestra di cortesia      altrimenti ABORT
    ──────────────────────────────────────────────────
    EMETTI
```

Il ritardo fra l'ultima verifica e l'emissione è **misurato e registrato**. Se supera la
soglia dichiarata l'atto è abortito invece di essere emesso. Non esiste una finestra di
rischio nulla; deve esistere una finestra di rischio misurata.

L'abort è sempre sicuro: tasti virtuali rilasciati, nessun pulsante lasciato premuto,
evento con l'ultimo punto valido.

### 2.2 Occlusione

Meccanismo, non intenzione. Per il **punto esatto** dell'atto — non per l'area:

- `WindowFromPoint` seguito da `GetAncestor(GA_ROOT)` deve dare la finestra di sessione;
- `GetForegroundWindow` deve dare la finestra di sessione;
- la finestra non deve risultare nascosta secondo l'attributo DWM corrispondente.

Un controllo areale non basta: una finestra piccola sopra il punto di click supera un
controllo sull'area e intercetta comunque l'atto.

### 2.3 Precedenza dell'operatore

`DOMAIN-16`. Hook di basso livello su mouse e tastiera che **scartano gli eventi marcati
come iniettati** e conservano solo il momento dell'ultimo evento umano. Se l'ultimo evento
umano è entro la finestra di cortesia — default 1500 ms — ogni atto è negato e l'azione in
corso è abortita.

`GetLastInputInfo` da solo non serve: conta anche l'input sintetico, quindi vedrebbe sempre
attività e non distinguerebbe mai la mano dell'operatore dalla propria.

Un comando esplicito di sospensione ferma tutto immediatamente, senza attendere alcun ciclo.

---

## 3. La griglia di mappa

Vedi `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md` § 5 per il perché e per la tabella dei bit.
Qui il come.

**Estrazione.** Una volta per build del client, dagli archivi che `NosArchive` sa aprire,
verso `<NOSAI-SSD>\NosAi\data\maps\<mapId>.grid`, con manifesto e hash per file.

**Formato.** `uint16` little-endian larghezza, `uint16` little-endian altezza, poi
larghezza × altezza byte.

**Contratto.**

```
IsWalkable(x, y)        →  false se fuori griglia, false se bit 0x01, true altrimenti
BlocksAttack(x, y)      →  true se fuori griglia, true se bit 0x02
HasLineOfSight(a, b)    →  traccia il segmento e nega al primo BlocksAttack
```

Fuori griglia non è « libero »: è bloccato. Una cella non rappresentata è sconosciuta e
sconosciuto non autorizza (`DOMAIN-10`).

**Innesto.** La griglia alimenta `TileType` in `NavigationPathfinding` per la geometria
statica; `Unobserved` resta agli ostacoli dinamici. Il percorso è rivalidato **prima di ogni
segmento**, non solo alla pianificazione: è la rivalidazione continua a costare, ed è per
questo che vale la pena valutare Jump Point Search sulla griglia a costo uniforme — stesso
percorso ottimale di A\*, molti meno nodi espansi. È un'ottimizzazione, non un prerequisito:
si affronta quando la rivalidazione continua misura un costo, non prima.

**Invalidazione.** L'hash dell'insieme delle griglie entra nell'identità della build. Build
diversa ⇒ griglie non caricate, pianificazione ferma, nessun valore prodotto.

### 3.1 Stato al 1 settembre 2026: contratto e semantica in codice, loader no

`NosAi.Runtime.Navigation` contiene il contratto e i test; il **loader del formato non è
scritto**, quindi nulla costruisce ancora una griglia e nulla nel runtime chiama questo
modulo (`ModuleReachability`: `Unreferenced`, dichiarato).

| Tipo | Ruolo |
|---|---|
| `MapCellFlags` | i cinque bit, nominati |
| `MapGrid` | `readonly struct`: larghezza, altezza, buffer. `IsWalkable`, `BlocksAttack`, `HasLineOfSight`, zero allocazioni |
| `DynamicOccupancy` | `Clear` / `Suspected` / `Occupied` |
| `StaticGeometryLayer` | la regola di composizione con `TileType` |
| `MapGridSetIdentity` | hash dell'insieme, confronto e rifiuto |
| `IMapGridLoader` + `MapGridFormat` | la forma che il loader deve avere e il vocabolario dei rifiuti |

**Due strati, composti in una sola direzione.** Lo strato statico (`MapGrid`) è autorevole e
*completo*: dentro un rettangolo caricato ogni cella ha una risposta, quindi lì non esiste
geometria ignota. Lo strato dinamico può solo **sottrarre** calpestabilità: può chiudere
terreno aperto, non può mai aprire terreno chiuso.

**Dove va `TileType.Unobserved`.** Smette di rappresentare geometria non letta — era un
segnaposto per un file che nessuno aveva aperto — e tiene due compiti, in entrambi bloccando:

1. **nessuna griglia caricata per questa mappa** (è ciò che produce di proposito il controllo
   di identità dopo una patch): geometria davvero ignota, pianificazione ferma;
2. **entità sospetta sulla cella** (`DynamicOccupancy.Suspected`): un'entità tracciata
   *potrebbe* essere lì e l'avvistamento è troppo vecchio per agire.

**La decisione discutibile, dichiarata.** Terreno aperto senza nulla di osservato sopra è
`Walkable`. Non è « ignoto trattato come vuoto »: il runtime non osserva quasi nessuna cella
di una mappa, quindi se l'assenza di un avvistamento bloccasse, bloccherebbe *tutto*, la
pianificazione non produrrebbe mai nulla, e la pressione a indebolire la regola finirebbe
sulla garanzia geometrica — l'unica che non deve muoversi. I due ignoti sono proposizioni
diverse e solo uno è spaziale:

- ignota la **geometria** ⇒ è un fatto su un *luogo*, si risponde nello spazio, blocca,
  ed è assoluto;
- ignota l'**occupazione** ⇒ è un fatto su un *momento*; nessuna proprietà della cella lo
  risolve, perché ciò che c'è è arrivato e se ne andrà. Si risponde nel **tempo**: limite
  di età dell'osservazione già imposto dal ciclo prima di agire, e rivalidazione del
  percorso prima di ogni segmento (sopra).

> **Il limite di età non è imposto sul percorso dell'atto.** Verificato leggendo: l'unico
> confronto d'età nel runtime è in `TargetSelector` riga 161, e riguarda la scelta del
> bersaglio, non il movimento. Niente, sulla via verso un passo, chiede quanto è vecchia
> l'osservazione che copre la cella di destinazione, e niente distingue « osservata di
> recente, libera » da « mai guardata ». Finché è così, `Walkable` autorizza un piano **e**
> un atto, perché all'atto non c'è una seconda condizione: la distinzione piano/atto è la
> risposta giusta, ma è ancora solo scritta.
>
> È la stessa forma dell'epoca di geometria — una condizione che un documento dà per
> presente e che non ha niente dietro — e va chiusa in P4, dove nasce il primo atto. La
> condizione mancante: l'atto richiede un'osservazione che copra la cella di destinazione e
> non più vecchia della soglia dichiarata; assenza di osservazione e osservazione scaduta
> sono lo stesso ingresso per la guardia, e non sono « libero ».
>
> Nota minore, categoria nota: quel confronto d'età sottrae due orari di parete
> (`ObservedAtUtc`). Le durate si misurano con orologio monotono — è il censimento che WP0
> prevede in `PIANO_GATE_A` § 3, e questo è uno dei punti.

Quindi `Walkable` afferma esattamente ciò che può sostenere — *la geometria del client
permette questa cella e nulla di osservato ci sta sopra* — e l'affermazione che **non** fa,
che la cella sia ancora libera quando il personaggio arriva, è quella che la rivalidazione
rifà ogni volta che conta. Nulla qui autorizza un atto: autorizza un piano, e il piano è
ricontrollato prima di ogni suo segmento.

**Innesto su `MapGridData`.** `StaticGeometryLayer.Project` scrive la base geometrica e
*preserva* `SafeZoneTown` e `PortalEntrance`, che sono celle calpestabili con un significato
che i bit non sanno esprimere: sovrascriverle cancellerebbe la tabella dei portali dentro la
geometria. Dove i due sono in disaccordo vince la geometria e il conteggio è riportato
(`SemanticTilesOverruled`) — un portale dentro un muro è una discrepanza da guardare, non da
risolvere in silenzio. `WaterOrChasm` invece **non** è preservato: è una congettura osservata
sul terreno, cioè proprio ciò a cui il file risponde.

**Identità e invalidazione.** `MapGridSetIdentity.Compute(files, clientFingerprint)` piega
`FormatVersion` + impronta del client + le coppie `(mapId, sha256 del file)` ordinate per
`mapId`, con separatori non ambigui. `MayLoad(recorded, current, out reason)` rifiuta su
qualunque ambiguità: identità mancante da una delle due parti, client cambiato
(`client_build_changed:…`), insieme cambiato (`map_grid_set_changed:…`). Taglia in entrambi i
versi — modificare un `.grid` senza toccare il client invalida altrettanto. Un rifiuto lascia
`default(MapGrid)`, che blocca l'intera mappa: è per questo che `MapGrid` e
`StaticGeometryLayer` falliscono chiusi sull'istanza di default.

**Cosa deve superare il loader.** `tests/NosAi.Runtime.Tests/MapGridLoaderContractTests.cs`,
classe astratta: il loader la attiva con una sottoclasse che restituisce la propria istanza.
Diciassette casi, fra cui ordine **row-major** verificato su un rettangolo non quadrato (un
indice trasposto passa qualunque fixture quadrata), header **little-endian**, payload
troncato e payload in eccesso entrambi rifiutati, dimensione zero rifiutata, e un rettangolo
dichiarato di 65535×65535 rifiutato **in aritmetica** e non in un'allocazione. Ogni rifiuto
lascia `default(MapGrid)` e porta un token di `MapGridFormat`, mai un'eccezione: un file
malformato è ciò che produce una patch del client e deve restare distinguibile da un bug.

---

## 4. Autorità d'input legata alla sessione

`InputEnvironmentProbe` verifica già che `SendInput` raggiunga il desktop. Manca il passo
successivo: **legarne l'esito allo stato della sessione** (`DOMAIN-15`).

Il caso che conta è documentato nel codice dell'auto-calibratore: il client gira a integrità
alta, e un processo a integrità media non può né leggerne la memoria né inviargli input.
`SendInput` in quel caso fallisce **senza segnalarlo** — né il valore di ritorno né l'ultimo
errore lo indicano.

All'apertura di ogni sessione e a ogni ripristino del primo piano:

1. confronto dei livelli di integrità;
2. atto innocuo e osservabile dentro il client — un movimento di puntatore di pochi pixel,
   senza pulsanti;
3. rilettura della posizione effettiva;
4. coincidenza entro tolleranza ⇒ sessione attuante; altrimenti **non attuante**, con il
   proprio codice di guasto, e **nessuna capacità di attuazione esposta al livello
   decisionale**.

Una sessione non attuante resta pienamente valida per l'osservazione: si continua a
raccogliere, non si agisce. La differenza che questo introduce è che il fallimento smette di
somigliare a « il gioco non risponde », che è la lettura sotto cui un ciclo di ritentativi
gira per sempre senza poter riuscire.

---

## 5. Verifica

L'invio di un input non è prova di successo (`DOMAIN-11`). Ogni capacità dichiara delta
atteso, finestra e **tolleranza**.

| Capacità | Delta atteso | Finestra | Tolleranza |
|---|---|---|---|
| movimento | posizione di griglia avanzata verso il nodo | 350 ms | ± 20 ms |
| uso di skill | cooldown attivo, MP decrementato | 250 ms | ± 20 ms |
| raccolta | oggetto rimosso dalla lista entità | 400 ms | ± 20 ms |
| selezione bersaglio | il riquadro del bersaglio compare | 180 ms | ± 20 ms |
| consumabile | quantità decrementata, HP o MP in aumento | 200 ms | ± 20 ms |

Il confronto del movimento è **griglia contro griglia** sulla posizione osservata, non pixel
contro pixel: è questo che rende 350 ms una soglia falsificabile invece di un numero.

> **Sulla tolleranza.** Le attese temporizzate su Windows hanno una granularità di default
> di circa 15,6 ms: una soglia di 250 ms misurata con un'attesa ordinaria vale in realtà
> 250–266 ms, e una soglia tarata su quel numero sbaglia sempre nella stessa direzione. Le
> finestre si **misurano** con l'orologio monotono, non si deducono dalla durata nominale
> dell'attesa.

Un atto non osservato non viene rinviato dal backend: torna al livello decisionale. Ripetere
alla cieca è il modo più diretto per costruire un ciclo che nessuna guardia ferma.

---

## 6. Da verificare prima di dichiarare chiuso

Punti su cui questo documento **non** afferma uno stato, perché non è stato misurato.
Il punto 1 è stato misurato ed è chiuso; resta qui con la risposta perché è la domanda che
ha trovato il difetto.

1. ~~Il ritorno da uno stato di arresto usa una finestra scorrevole o un contatore?~~
   **Risposto e corretto il 1 settembre 2026. Era un contatore.**

   `RecoveryController` contava i fallimenti **consecutivi**
   (`AutonomyPipeline.cs`, campo `_consecutiveFailures`) e la via di ritorno era
   `ResetFailures()`, che azzerava il conteggio. I due gate poi si assegnavano da soli
   `RuntimeMode.Normal` su qualunque ciclo confermato — `Gate3Runtime.cs` § ciclo, ramo
   `verification.IsConfirmed`, e `Gate6Runtime.cs` sul ramo `verif.IsSuccess`. Lo scenario
   descritto qui sopra era quindi peggiore del previsto: con dieci successi alternati a nove
   fallimenti la scala **non saliva nemmeno il primo gradino**, perché due fallimenti non
   erano mai adiacenti e il contatore tornava a zero diciannove volte.

   **Sostituito con:**

   - **finestra scorrevole** di 20 esiti; i gradini si leggono da quanti degli ultimi
     tentativi sono falliti. Un successo non cancella la storia, la fa solo scorrere di uno.
     I gradini restano quelli di prima (`maxRetries`+1 degrada, oltre arresta): è cambiato
     *che cosa* si conta, non *quanto*.
   - **stato di prova a una azione per volta** (`RecoveryState.Probing`): scaduto il
     cooldown si ammette **un solo** atto e nessun secondo finché quello non è risolto.
     Si torna a piena velocità solo dopo 3 successi consecutivi di prova; un fallimento in
     prova riarresta e la prova ricomincia da zero.
   - **cooldown esponenziale**: 5 s al primo arresto, raddoppio a ogni arresto successivo,
     tetto a 5 minuti. I fallimenti che arrivano *mentre* è già arrestato non allungano
     l'attesa, altrimenti una raffica la comporrebbe in un valore che nessuno ha scelto.
   - **controllo d'ammissione** (`TryBeginAction`): prima mancava del tutto, quindi un
     runtime «degradato» continuava ad agire alla stessa frequenza. Ora un rifiuto è
     riportato come blocco e **non** viene ricontato come fallimento.

   **Ciò che non cambia:** `TrustBoundary` resta a senso unico. Chiudere il breaker
   ripristina il `RuntimeMode` e nient'altro; se l'arresto aveva portato la fiducia a
   `Tier0_ReadOnly`, il `SafetyGate` continua a rifiutare finché non interviene chi
   sorveglia. Il controller non ha, e non deve avere, alcun metodo che possa essere scambiato
   per una promozione — la suite di Gate 3 lo verifica per riflessione.

   Test: `tests/NosAi.Runtime.Tests/RecoveryCircuitBreakerTests.cs`, a partire da
   `TenSuccessesAlternatingWithNineFailuresDoNotReturnTheRuntimeToFullSpeed`.
2. ~~Il processo dichiara la consapevolezza DPI per monitor nel manifest?~~
   **Chiuso il 1 settembre 2026: no, e nemmeno a runtime.** `NosAi.Runtime` — il processo
   che chiama `GetClientRect`, calibra la proiezione ed emette l'input — non ha
   `ApplicationManifest`, non ha un `app.manifest`, e in `src/` non compare nessuna API di
   consapevolezza DPI. La dichiara solo `NosAi.ControlPanel`, che è l'unico processo a cui
   non serve.

   Conseguenza. Per un processo non consapevole Windows **virtualizza** le coordinate:
   `GetClientRect` e `ClientToScreen` rispondono nello spazio logico a 96 DPI. La cattura
   dello schermo, invece, non è virtualizzata: arriva in pixel fisici. Su uno schermo al
   125 % le due unità differiscono di un quarto, e ogni ritaglio dell'HUD calcolato come
   frazione del client rect cade sui pixel sbagliati. Non fallisce: **misura davvero i pixel
   sbagliati**, che è la distinzione su cui ADR-0018 è costruito, e la calibrazione affine
   ci si adatterebbe sopra restituendo un residuo buono su una trasformazione senza
   contenuto — l'errore che `ScreenProjectionCalibration` documenta per la forma assoluta.

   Perché allora T-03 ha confermato la lettura dell'HUD contro un client reale? Quasi
   certamente perché lo schermo dell'operatore è al 100 %, dove virtuale e fisico
   coincidono. È un'assunzione d'ambiente su cui poggia tutta la percezione, che nessuna
   riga di codice verifica e nessun test dichiara.

   La correzione ha una proprietà utile: dichiarare la consapevolezza per monitor **non
   cambia nulla al 100 %**, quindi si può fare subito senza rompere ciò che oggi funziona,
   e diventa corretta appena lo schermo cambia. Ha però una conseguenza obbligatoria: le
   calibrazioni già memorizzate sono state stimate nell'unità vecchia e vanno invalidate,
   non riusate.

   **Applicata il 1 settembre 2026** — `app.manifest` con `PerMonitorV2` su `NosAi.Runtime`,
   più `ClientWindowDpiProbe` e il comando `--window-probe`. Due cose emerse applicandola,
   entrambe da chiudere in P2a:

   - **Il regime di consapevolezza dipende da come si avvia il processo.** Il manifest è
     incorporato nell'apphost: `NosAi.Runtime.exe` riporta `PerMonitorV2`, ma
     `dotnet exec NosAi.Runtime.dll` gira sotto l'host `dotnet` e riporta `PerMonitor`.
     Nessuno dei due è *unaware*, quindi l'unità di coordinate non cambia e non c'è un
     danno immediato — ma il regime sotto cui una calibrazione è stata stimata è ora una
     funzione del comando usato per lanciare, e niente la registra. **La calibrazione deve
     portare con sé il regime sotto cui è stata stimata e rifiutarsi di essere riusata sotto
     un altro.** Costa poco adesso e non si retrofitta dopo che le calibrazioni si sono
     accumulate.
   - **Il controllo che oggi invalida non può esprimere questo caso.**
     `CalibratedScreenProjection` confronta larghezza e altezza del client
     (`screen_projection_client_size_changed`). Su scala diversa dal 100 % il passaggio da
     non consapevole a consapevole cambia quelle dimensioni, e il controllo scatta: è la
     ragione per cui il cambio di manifest non produce silenziosamente una calibrazione
     sbagliata. **Al 100 % le dimensioni non cambiano**, quindi una calibrazione stimata
     prima del manifest verrebbe accettata dopo. È innocua solo perché al 100 % le due
     unità coincidono: siamo protetti da una coincidenza, non da un controllo.

3. ~~L'epoca di geometria incrementa anche al cambio di DPI e al cambio di monitor?~~
   **Chiuso il 1 settembre 2026: l'epoca non esiste.** `ClientWindowLocator` è statico e
   senza stato: rilegge e restituisce un rect nuovo a ogni chiamata, senza conservare il
   precedente. `CalibratedScreenProjection` confronta solo larghezza e altezza e ignora
   deliberatamente lo spostamento — il che è giusto per la *forma* della trasformazione, ma
   significa che un cambio di DPI, che per un processo non consapevole non cambia le
   dimensioni virtualizzate, non viene visto da nessuno. `Win32ProcessAdapter` è l'unico che
   conserva la geometria, in un campo scritto all'attach e **mai aggiornato**.

   Conseguenza. `DOMAIN-08` e `DOMAIN-19` oggi non sono applicabili: non c'è niente contro
   cui far decadere una calibrazione, e la prima condizione del commit point — « epoca di
   geometria invariata dall'autorizzazione » — non ha un valore da confrontare. **L'epoca è
   quindi un prerequisito di P2, non un dettaglio di P2.**
4. ~~La conversione a coordinate assolute normalizzate copre il desktop virtuale o solo il
   monitor primario?~~ **Chiuso il 1 settembre 2026: copre il desktop virtuale.**
   `Win32InputBackend.MoveAbsolute` prende origine ed estensione da
   `SM_XVIRTUALSCREEN` / `SM_YVIRTUALSCREEN` / `SM_CXVIRTUALSCREEN` / `SM_CYVIRTUALSCREEN`,
   normalizza con `(x − originX)` e passa `MOUSEEVENTF_VIRTUALDESK`. Un punto su un monitor
   secondario a coordinate negative rientra correttamente nel campo 0–65535. Non usa le
   metriche del solo monitor primario.

   **Difetto trovato leggendo, da correggere in P2.** La stessa funzione chiude con
   `Math.Clamp(normalised, 0, 65535)`. Un punto fuori dal desktop virtuale non è un punto da
   riportare al bordo: è un punto impossibile, e riportarlo al bordo lo trasforma in un click
   reale sul bordo dello schermo. È la forma esatta dell'errore che il progetto vieta
   altrove — sconosciuto non diventa un valore plausibile, e una sorgente che fallisce lo
   dice. Deve restituire `false` con il proprio codice di guasto. Oggi non morde perché le
   guardie a monte rifiutano i punti fuori dal client, ma è l'ultima difesa convertita in una
   correzione silenziosa, ed è l'unico punto del percorso dove un errore di coordinate
   diventa un atto invece di un rifiuto.
