# Tasti e bersaglio — come il runtime impara i propri comandi e sceglie cosa attaccare

**Versione:** 1.0
**Data:** 2 settembre 2026
**Ruolo:** normativo su due domande: **come il runtime arriva a sapere quale tasto fa
cosa**, e **come sceglie qualcosa da attaccare senza mai colpire un NPC**.
**Subordinato a:** `NOSAI_ARCHITECTURE_BASELINE.md`, `ADR-0002` (classificazione),
`ADR-0016` (un fatto sconosciuto non seleziona un ramo), `ADR-0017` (una sorgente
indipendente ne addestra un'altra), `ADR-0018` (il bersaglio dallo schermo), `ADR-0019`
(l'atto è input del sistema operativo).
**Ordine dei lavori:** `PIANO_CAPACITA.md` § 5, capacità `C1`, `C3`, `C4`, `C6`.

---

## 0. Perché le due domande stanno insieme

Hanno la stessa risposta: **il client lo sa già, e la risposta più economica e più
corretta è chiederglielo invece di dedurlo.**

È la ragione per cui `ADR-0019` ha scelto l'input del sistema operativo come canale:
un atto che passa dal codice del client eredita gratis tutto ciò che il client
verifica. Qui l'eredità non è un rifiuto, è una **conoscenza**: quali tasti esistono,
e che cosa è un mostro.

---

## 1. Il catalogo dei tasti predefiniti

### 1.1 Che cosa è, e che cosa non è

È una **ipotesi con provenienza**, esattamente come gli offset in
`NosTaleClientLayout`: quel file prende i suoi numeri da `NosSmooth.Local` e li tratta
« come ipotesi di partenza, non degni di fiducia per autorità », confermandoli con una
sorgente indipendente. Il catalogo dei tasti riceve lo stesso trattamento e vive nello
stesso modo — **in un tipo del runtime, non in `data/`**, perché `data/` è ignorato dal
repository e contiene ciò che appartiene a una macchina, mentre i tasti predefiniti
sono un fatto sul gioco.

### 1.2 Il catalogo

Raccolto il 2 settembre 2026 dalle guide pubbliche elencate in § 8. **Nessuna riga è
stata verificata contro il client di questa macchina**, ed è esattamente questo che
rende necessario il § 3.

| Tasto | Effetto dichiarato | Classe |
|---|---|---|
| `I` | inventario | interfaccia |
| `K` | finestra abilità | interfaccia |
| `P` | scheda personaggio | interfaccia |
| `O` | missioni | interfaccia |
| `L` | miniland | interfaccia |
| `N` | messaggistica / amici | interfaccia |
| `F12` | guida di gioco | interfaccia |
| `F6` | seleziona il **giocatore** successivo | selezione |
| `F7` | seleziona l'**NPC** successivo | selezione |
| `F8` | seleziona il **mostro** successivo | selezione |
| `Spazio` | seleziona il mostro successivo **e** attacca con l'attacco primario | selezione + atto |
| `Z` | come sopra, con l'attacco secondario | selezione + atto |
| `A` | manda il NosMate in un punto | compagno |
| `Tab` | passa all'altra barra rapida | barra |
| `1`…`0`, `Q`, `W`, `E`, `R`, `T` | slot rapidi | **vuoti per progetto** |

### 1.3 Tre classi, e solo la terza è un problema

- **Fissi di gioco** (`I`, `K`, `P`, `O`, `L`, `F6`/`F7`/`F8`, `Spazio`, `Z`, `Tab`):
  li definisce il client. Un operatore può cambiarli, raramente lo fa, e il § 3 se ne
  accorge comunque.
- **Slot rapidi** (`1`…`0`, `Q`, `W`, `E`, `R`, `T`): **il client li consegna vuoti.**
  Nessun catalogo al mondo può dire cosa contengono, perché il contenuto lo mette il
  giocatore. È qui che il runtime deve decidere, ed è il § 4.
- **Non assegnati**: restano non assegnati. Un tasto senza un intento non è un tasto
  libero da usare, è un tasto di cui non si sa nulla.

---

## 2. La regola che nessun catalogo può indebolire

> **Il runtime non preme mai un tasto il cui effetto non è stato confermato su questo
> client.**

`InputActionEffector` la applica già e la motiva così: « nessun tasto predefinito. "La
pozione è sull'1" premerebbe *un* tasto durante un combattimento vero ». Il catalogo
del § 1 **non** cambia questa regola: aggiunge un'ipotesi da confermare, non un
permesso.

Uno stato in più rende la cosa dicibile. Un intento è in uno di tre stati:

| Stato | Significato | Il combattimento lo usa |
|---|---|---|
| `unknown` | nessuno ha detto niente | no |
| `declared` | il catalogo o l'operatore lo affermano, nessuno l'ha verificato | **no** |
| `confirmed` | premuto, e l'effetto atteso è stato osservato | sì |

I motivi di rifiuto sono due, **e da oggi esistono entrambi**:
`keybind_not_configured:<intento>` e `keybind_not_confirmed:<intento>`, perché « non
lo so » e « lo credo ma non l'ho provato » sono due condizioni diverse e l'operatore
deve poterle distinguere.

Come sono fatti nel file, 2 settembre 2026: ogni bind porta `"confirmed"`, e
**assente vale `false`**. Il default sicuro è quello che non preme, quindi un file
scritto prima che il campo esistesse dichiara e non conferma. `"confirmed": "true"`
— la scrittura sbagliata più probabile — **rifiuta il file** invece di essere letta
come vera: interpretarla accenderebbe un tasto mai provato.

Ne segue una cosa che vale la pena dire per intero: **un bind dichiarato non copre il
suo intento.** `--keybinds-check` conta come copertura solo i bind confermati, perché
copertura significa « il runtime può agire », non « c'è una riga nel file »; il
pannello disegna il bind dichiarato come *dichiarato, non premerà*, con provenienza
`DERIVED` invece di `LIVE`. Le due letture passano dallo stesso lettore proprio
perché non possano dissentire su questo.

---

## 3. Come un tasto diventa `confirmed`: la conferma per effetto osservato

È `ADR-0017` eseguito di nuovo. Là il filo insegnava allo schermo perché era la
sorgente più forte; qui **l'osservazione insegna alla tastiera**, perché premere è
l'unica cosa che produce una prova.

Il runtime preme in condizioni sicure e guarda che cosa cambia. Ciò che cambia dice
che cos'era il tasto:

| Osservazione dopo la pressione | Conclusione | Sorgente |
|---|---|---|
| gli `MP` scendono | quello slot contiene una **abilità** | `stat` |
| `sr` nomina lo slot come entrato in cooldown | conferma indipendente della stessa cosa | `sr` |
| gli `HP` salgono | quello slot contiene una **cura** | `stat` |
| lo slot d'inventario decresce | quello slot contiene un **consumabile**, e quale | `ivn` |
| compare una finestra sullo schermo | **intento d'interfaccia** | schermo |
| **niente cambia** | lo slot è vuoto, o il tasto non fa ciò che il catalogo dice | — |

Tre condizioni perché la prova valga, e sono le stesse del catalogo delle
post-condizioni:

1. **Fuori dal combattimento.** Una prova non deve costare vita.
2. **L'osservazione è posteriore alla pressione** (`VER-03`).
3. **Niente cambia ≠ fallito.** Se nessuna sorgente ha guardato — `stat` non è
   arrivato, `ivn` non è decodificato — l'esito è `Unverified` e l'intento resta
   `declared`, mai promosso e mai declassato.

**Il risultato è che il runtime scopre i propri tasti invece di riceverli.** Un
comando d'operatore avvia il giro, lo mostra intento per intento, e scrive il file. È
lavoro di `C3`.

---

## 4. Chi decide cosa va negli slot vuoti

### 4.1 Il runtime propone, da ciò che ha davvero

Non da un'idea di come si gioca a NosTale. Da due inventari reali:

- **le abilità che il personaggio conosce** — la finestra `K`, e il catalogo di
  riferimento che ha già importato 1 958 abilità con la loro provenienza;
- **i consumabili che ha in borsa** — `ivn`, quando sarà decodificato (`C1-3`).

### 4.2 Il criterio di assegnazione

In quest'ordine, perché è l'ordine in cui le regole del pianificatore ne hanno
bisogno:

1. **Cura**, sullo slot più raggiungibile. È l'unica azione che le regole di
   sopravvivenza sanno già pianificare, ed è l'unica che serve **mentre** qualcosa va
   male.
2. **Attacco base e le abilità a costo minore**, in ordine di costo in MP crescente.
3. **Mobilità e fughe**, se esistono.
4. Il resto resta vuoto. **Uno slot vuoto è meglio di uno slot pieno di qualcosa che
   nessuna regola sa quando usare**: il secondo è un tasto che verrà premuto per caso.

Una assegnazione, una volta scritta, **è stabile**. Cambiarla fra due sessioni
invalida ogni conferma del § 3, e un layout che cambia da solo è indistinguibile da un
layout sbagliato.

### 4.3 Il limite onesto, e come si toglie

**Oggi il runtime non può riempire la barra da solo.** Trascinare un'icona
dall'inventario a uno slot è un drag dentro l'interfaccia del client: richiede la
proiezione schermo (`T-10`, non ancora calibrata) e le coordinate della griglia
d'inventario, che nessuno ha misurato.

Quindi il giro è: **il runtime propone, l'operatore applica una volta, il runtime
conferma premendo** (§ 3). Quando il drag diventerà possibile, la proposta è la stessa
e ad applicarla sarà il runtime: il progetto non cambia, cambia chi esegue l'ultimo
passo.

---

## 5. Riconoscere chi e cosa c'è attorno

### 5.1 Quello che arriva, e quello che viene buttato via

Il pacchetto `in` porta `tipo, vnum, id, x, y, direzione, HP%, MP%`. Il decoder legge
`fields[1]` (tipo), `fields[3]` (id), `fields[4]`, `fields[5]` (posizione) e
`fields[7]` (HP%).

**`fields[2]` è il vnum, e viene saltato.**

Il vnum è l'unico campo che dice *che cosa* è un'entità, invece di *dove* è. Senza di
esso un mostro e un mercante sono due id con una posizione. Il catalogo di riferimento
ha già importato **2 705 mostri** con nome e provenienza, e `GameReferenceDatabase`
espone già `Lookup(kind, vnum)`, `Exists(kind, vnum)` e `DisplayName(kind, vnum, lingua)`:
la risposta esiste, manca il campo per farle la domanda.

### 5.2 Il tipo del filo non basta, e va detto

`PROTOCOLLO_NOSTALE.md` è esplicito: **tipo `3` = mostro / NPC**, confermato, i due
insieme. Il filo **non distingue** un mostro da un NPC.

Quindi qualunque regola costruita sul solo tipo attaccherebbe i mercanti.

---

## 6. Scartare gli NPC senza doverli riconoscere

### 6.1 Il client ha già due tasti diversi

`F7` seleziona l'**NPC** successivo. `F8` seleziona il **mostro** successivo.

Il client sa che cosa è un mostro, e lo sa con la definizione del gioco invece che con
la nostra. **Chiedere `F8` è più corretto di qualunque classificazione che potremmo
dedurre**, ed è la stessa ragione per cui `ADR-0019` ha scelto questo canale.

### 6.2 La regola, e perché non può sbagliare nel verso pericoloso

> **Si attacca solo un'entità *stabilita* come attaccabile. Un'entità mai stabilita
> resta sconosciuta, e lo sconosciuto non autorizza un atto** (`ADR-0016`).

Le prove che stabiliscono, dalla più forte:

| Prova | Da dove |
|---|---|
| è stata **bersaglio** di un `su` | filo, confermato |
| ha **attaccato il giocatore** | filo, confermato — ed è anche il caso del contrattacco |
| è stata **selezionata da `F8`**, e la selezione è confermata | tasto + riquadro bersaglio |
| il suo vnum è nel catalogo dei mostri con una scheda | catalogo di riferimento |

**Un NPC non viene escluso perché è riconosciuto come NPC: viene escluso perché non è
mai stato stabilito come attaccabile.** È la stessa asimmetria di `ADR-0018`, e regge
anche quando la classificazione è impossibile — che è esattamente il caso del tipo `3`.

### 6.3 Chi è stato selezionato: due sorgenti, come sempre

Dopo `F8`, *qualcosa* è selezionato. Due domande diverse, due sorgenti:

- **se** c'è un bersaglio → lo schermo, il riquadro (`ADR-0018`, calibrazione `T-09`);
- **quale** entità → `ct`, che il catalogo del protocollo registra come « targeting fra
  due entità », 108 occorrenze, e che **il decoder non legge**. In assenza di `ct`, il
  primo `su` con il giocatore come attaccante lo nomina.

### 6.4 La conseguenza che sblocca il calendario

`F8` è **una pressione di tasto, non un clic**.

Quindi selezionare un bersaglio **non dipende dalla proiezione schermo** (`T-10`, cinque
tentativi falliti). Il combattimento smette di aspettare la calibrazione che ha
bloccato tutto: serve ancora per *camminare* in un punto, non per *scegliere chi
colpire*.

---

## 7. Che cosa manca, come lavoro

| Manca | Dove | Capacità | Chi |
|---|---|---|---|
| Il **vnum** letto da `in` e portato sull'avvistamento | `NosTaleWorldProtocolDecoder`, `EntitySighting` | `C1` | Claude, **dentro la sessione già in corso** |
| **`ct`** decodificato: quale entità è selezionata | stesso decoder | `C1` | Claude, stessa sessione |
| **`sr`**, **`ivn`** decodificati | stesso decoder | `C1-3` | Claude, stessa sessione |
| Il **catalogo dei tasti** come tipo del runtime, con provenienza | `LowLevel/` | `C3` | Claude, **dopo** che Cursor riporta |
| Lo stato `declared` / `confirmed` e `keybind_not_confirmed` | `KeybindMap` | `C3` | Claude, dopo Cursor |
| Il **giro di conferma** per effetto osservato, con il suo comando | `LowLevel/`, `Program.cs` | `C3` | Claude decide, Cursor caba |
| La **proposta di layout** degli slot | `Gate3/` o `GameData/` | `C3`, `C6` | Claude |
| La regola « si attacca solo ciò che è stabilito » | `Autonomy/TargetSelector`, planner | `C4`, `C6` | Claude |

---

## 8. Provenienza del catalogo, e il suo limite

Le righe del § 1.2 vengono da guide pubbliche di NosTale, raccolte il 2 settembre 2026:

- [NosTale PC Controls — Magic Game World](https://www.magicgameworld.com/nostale-pc-controls/)
- [Nostale Wiki — Inventory](https://nostale.fandom.com/wiki/Inventory)
- [Nostale Wiki — Options](https://nostale.fandom.com/wiki/Options)
- [NosTale game guide — Game interface](http://gameguide.nostale.co.uk/main/game_interface)

Sono fonti di terze parti, non documentazione del produttore, e **nessuna riga è stata
verificata contro il client di questa macchina**. Vanno trattate come
`NosTaleClientLayout` tratta gli offset di `NosSmooth`: un punto di partenza che il
runtime deve confermare da solo, mai un'autorità.

Il catalogo porta con sé la propria data e le proprie fonti, così una riga che si
rivela sbagliata si corregge sapendo da dove veniva.

---

## 9. Che cosa questo documento non decide

- **Non decide i tasti dell'operatore.** Propone e conferma; l'operatore resta libero
  di cambiarli, e il § 3 se ne accorge alla prossima conferma.
- **Non autorizza il drag dentro l'interfaccia del client.** È un atto come gli altri e
  passerà dalle stesse guardie, quando la proiezione esisterà.
- **Non cambia `ADR-0016` né `ADR-0018`.** Li applica: un fatto sconosciuto non
  seleziona un ramo, e lo schermo dice *se*, il filo dice *quale*.
