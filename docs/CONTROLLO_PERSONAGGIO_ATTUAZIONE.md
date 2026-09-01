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

1. Il ritorno da uno stato di arresto usa una finestra scorrevole o un contatore? Con un
   contatore, dieci successi intervallati da nove fallimenti riportano il sistema a piena
   velocità, che è lo scenario che l'arresto esiste per impedire. Se è un contatore, va
   sostituito con una finestra e uno stato di prova a una azione per volta.
2. Il processo dichiara la consapevolezza DPI per monitor nel manifest? Se non la dichiara,
   `GetClientRect` restituisce coordinate virtualizzate e l'intera calibrazione misura la
   finestra sbagliata su schermi con scala diversa da 100 %.
3. L'epoca di geometria incrementa anche al cambio di DPI e al cambio di monitor, o solo su
   spostamento e ridimensionamento?
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
