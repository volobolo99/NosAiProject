# NosTale world protocol — observed catalogue

**Source:** two real captures of a live session, 1 Sep 2026, server `79.110.84.175:4002`.
`data/nostale_01.noscap` (idle, 2490 packets) and `data/nostale_combat.noscap`
(combat, 8211 packets). Both decode to **100% printable ASCII** through
`NosTaleWorldDecoder`.

**Status of this document.** Everything here is *observed*, not specified. NosTale
publishes no protocol description, so each field below carries how strongly the
capture supports it:

| Mark | Meaning |
|---|---|
| **confirmed** | Cross-checked against something independent — the client's own HUD, or arithmetic that holds across the capture |
| **probable** | Consistent across every occurrence, and the reading is the only one that fits |
| **unknown** | Position is stable, meaning is not established. **Do not read these.** |

A field marked *unknown* that later turns out to be needed must be derived from a
new capture, not guessed from its neighbours. ADR-0014 is explicit that a decoded
value is `LIVE` only when the decoder verified its framing; a field nobody has
established is not a value, it is an offset with a number in it.

---

## Transport

- TCP, world channel. The port is per-session; `79.110.84.175:4002` in these captures.
- **Server → client** is what carries the world. It is decodable: see
  `NosTaleWorldDecoder`. Packets are terminated by `0xFF`.
- **Client → server** is *not decoded*. It uses a different, session-keyed
  encryption (observed: packets open `0xD5`/`0xD6` and end `0x4E`). Nothing in
  this document comes from it.

### What the wire cannot tell us

**The player's own position never arrives from the server.** Every `mv` in
117 KB of capture is entity type `3`; not one carries the player's id. Position
is client-authoritative — the client sends it, and that direction is encrypted.

This is the boundary where memory reads earn their place as the confirming
source: the network is authoritative for everything the *server* knows, and
silent about what only the client knows.

---

## Identities

| Concept | Observed |
|---|---|
| Entity type `2` | **NPC, pet e portali — letto dal 2026-09-08, come `Bystander`**: vedi sotto |
| Entity type `1` | **altro giocatore — `mv` letto dal 2026-09-08, `in` e `st` no**: vedi sotto. Player — **confirmed** (the session's own character id `3443217` appears as type 1 in `su`, `cond`, `sayi`) |
| Entity type `2` | not observed in these captures |
| Entity type `3` | monster / NPC — **confirmed** (all `mv`, `in`, `die`) |
| Entity id | stable per entity for its lifetime — **confirmed** (traced `313816` across `in`, `st`, `su`, `mv`, `die`) |

---

## Entity type `1` — si vede muovere, non si sa chi è

Il `mv` di un altro giocatore ha esattamente la forma degli altri:

```
mv 1 8309202 74 108 12      altro giocatore, velocità 12
mv 3 3023    51 162  5      mostro,          velocità 5
```

Sei token in entrambi, e gli id sono gli stessi che il pacchetto di comparsa
dichiara. Il passo mediano è **3,6 caselle** contro le 2,0 dei tipi 2 e 3 — e non
è un layout sbagliato, è la velocità che quegli stessi pacchetti riferiscono. Il
salto più grande misurato su 125 passi è **9,0 caselle**, su una mappa larga
oltre 160: campi letti nel posto sbagliato darebbero salti da un capo all'altro.

### Il suo `in` invece no

```
in 1 GaM1 - 8309204 76 121 2 0 0 1 7 3 -1.271.152.88…
     nome ^ id      x  y   dir
in 3 36   313826 109  63 2 …
     vnum id     x    y  dir
```

Il tipo 1 porta un **nome** dove gli altri portano il vnum, e un campo `-` in
più: id, x e y stanno una posizione più in là. Resta rifiutato, ed è per questo
che `IsReadableEntity` prende ora anche l'opcode — un solo predicato
costringerebbe a rifiutare la posizione di un altro giocatore per colpa di un
pacchetto diverso. Il suo `st` non è mai stato osservato.

### Conseguenza: si vede, non si nomina, non si attacca

Un altro giocatore appare come posizione, specie `Player`, **senza vnum** — e
senza vnum `TargetEstablishment` non stabilisce nulla, esattamente come per ogni
entità mai nominata. Non è servita una regola nuova.

**Il personaggio proprio non è mai un'entità del mondo.** Il server non manda mai
la propria posizione — zero occorrenze del proprio id fra i 20 876 `mv` di
`messaggi.noscap` — e il decoder non si fida di quel fatto: se un giorno la
mandasse, pubblicarla creerebbe un secondo sé stesso accanto a quello che `stat`
e `cond` già descrivono.

**Misurato**: «tipo entità non letto» è ora **zero su cinque registrazioni su
sei**; su `messaggi.noscap` restano 5 pacchetti, tutti `in` di tipo 1. Il World
Model di quella cattura vede **176 mostri, 33 astanti e 8 giocatori**.

---

## Entity type `2` — letto, e mai un bersaglio

Il decoder ha letto solo il tipo 3 fino al 2026-09-08. Il motivo dichiarato era
che il layout del tipo 2 non fosse stabilito; **lo è stato**, misurato su
`data/messaggi.noscap` (2718 `mv`, 14 `st`, 6 `in` di tipo 2, su 30 entità
distinte), e il tipo 2 è entrato — con la specie accanto.

### Le tre misure

| Misura | Tipo 2 | Tipo 3 |
|---|---|---|
| passi di `mv` osservati | 2 688 | 17 849 |
| mediana del passo | 2,0 caselle | 2,0 caselle |
| novantesimo percentile | 2,8 | 2,8 |
| entro tre caselle | 99,3% | 99,9% |

Campi letti nella posizione sbagliata darebbero salti casuali, non una
camminata. In più: tutti e quattro i vnum visti in `in` di tipo 2 esistono nella
stessa tabella `monster` del catalogo, e danno nomi sensati; e `st` di tipo 2 ha
gli stessi dodici campi del tipo 3, con valori che soddisfano ogni invariante di
plausibilità.

```
st 2 3102 92 0 100 100 100520 2881 100520 2881 0     livello 92, 100520/100520 HP
st 3 3205  8 0 100 100    310   52    310   52 0     livello 8,     310/310 HP
```

### Perché resta fuori

I nomi dicono cos'è il tipo 2:

| vnum | nome |
|---|---|
| 2362 | Caverna dei Conigli |
| 1494 | Pir |
| 1488 | Baby^panda |
| 2557 | Graham |

**Non sono mostri**: sono NPC, pet e portali. Ogni avvistamento che il decoder
pubblica è etichettato `"Monster"`, e quell'etichetta arriva al World Model.
Ammettere il tipo 2 così com'è metterebbe davanti al pianificatore un
negoziante — o il pet del giocatore stesso — come bersaglio.

### Come è entrato

L'avvistamento porta ora **la specie letta sul filo** invece di una costante:
`EntitySighting.MonsterKind` per il tipo 3, `BystanderKind` per il tipo 2. La
specie arriva fino a `SelectableEntity`, e `TargetEstablishment.Assess` rifiuta
gli astanti con `target_is_a_bystander`.

Il controllo sta **dopo** le due prove per evidenza: se un astante ci ha colpito,
o ci abbiamo agito sopra, quello che è successo pesa più di come è etichettato.
La stessa gerarchia che `Assess` applica già al vnum mai osservato.

Specie non dichiarata non è «astante» e non è «mostro»: la regola del vnum nullo
vale anche qui, e chi costruisce entità da una sorgente che la specie non la
porta non vede né tutto attaccabile né niente come astante.

**Effetto misurato**: «tipo entità non letto» passa da **431 a 0** su
`equip_test.noscap` e da **17 a 0** su `nostale_combat.noscap`. Su
`messaggi.noscap` restano 133 movimenti, tutti di **tipo 1** — l'altro giocatore,
l'unico tipo osservato di cui nessuna misura abbia mai mostrato dove stanno i
campi. Il World Model di quella cattura vede ora **33 astanti accanto a 176
mostri**, e `--world-replay` li distingue in elenco.

### E il catalogo non basta a distinguerle

Verrebbe da pensare che ci sia già una difesa: `TargetEstablishment` non si fida
della sola presenza in tabella, e scarta le voci **RaceType 8** di `monster.dat`
— «Special NPCs», cioè trappole, teletrasporti e NPC parlanti. Il commento di
quel campo porta però il proprio avvertimento, *«not yet cross-checked against a
real client capture»*, e il riscontro — fatto il 2026-09-07 — viene fuori
negativo:

| vnum | nome | RaceType |
|---|---|---:|
| 2362 | Caverna dei Conigli | 0 |
| 1494 | Pir | 3 |
| 1488 | Baby^panda | 2 |
| 2557 | Graham | 3 |

**Nessuno è 8.** Il catalogo chiamerebbe mostri tutte e quattro. Quindi la
garanzia che il pianificatore non prenda di mira un negoziante sta nel decoder
che rifiuta il tipo 2, **non** nel classificatore — ed è il contrario di come il
commento del RaceType si lascia leggere.

Il filo distingue queste quattro dai mostri; il catalogo no. Quando il tipo 2
entrerà, a decidere dovrà essere la specie letta sul filo.

---

## `stat` — the player's own vitals

```
stat 7288 7305 1420 1420 0 1184
     hp   maxHp mp  maxMp  ?   ?
```

| # | Field | Confidence |
|---|---|---|
| 1 | current HP | **confirmed** — matched the client's HUD reading 7305/7305 while idle, then moved across 33 distinct values during combat (7218…7305) |
| 2 | max HP | **confirmed** — constant 7305, matched the HUD |
| 3 | current MP | **confirmed** — matched the HUD reading 1420 |
| 4 | max MP | **confirmed** — matched the HUD |
| 5 | — | **unknown** — constant `0` throughout both captures |
| 6 | — | **unknown** — constant `1184`. Candidate: SP, given the HUD's third bar. Not established |

**This is the vitals source.** 62 packets during 90 s of combat, tracking damage
as it happened.

---

## `st` — another entity's vitals

```
st 3 313816 8 0 66 100 198 52 310 52 0
   ty id    lv ?  ?  ?   hp mp mxH mxM ?
```

| # | Field | Confidence |
|---|---|---|
| 1 | entity type | **confirmed** |
| 2 | entity id | **confirmed** |
| 3 | level | **probable** — `8` for the monsters fought, plausible and stable |
| 5 | HP percent | **probable, and inconsistent with fields 7/9** — see below |
| 6 | MP percent | **probable** — `100` throughout |
| 7 | current HP | **probable** |
| 8 | current MP | **probable** |
| 9 | max HP | **probable** |
| 10 | max MP | **probable** |

**Do not use field 5.** Checked arithmetically across the capture, `round(hp/maxHp*100)`
matches it in only 28 of 49 packets; where it disagrees it reads about two points
high (`198/310` = 64%, field says 66). The most likely explanation is that the
percentage is computed before the update the same packet reports, but that is not
established. **Use the absolute values (7 and 9).**

---

## `su` — a skill or attack resolving

Two shapes, by who attacks.

**Monster → player:**
```
su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305
   ty id     ty id     sk ?  ?  ?   ? ? ?  % ?  ? ? hp  maxHp
```

**Player → monster:**
```
su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310
   ty id     ty id    skill ?   ?  ?   ? ? ? ?  dmg ? ? ? maxHp
```

| Field | Confidence |
|---|---|
| attacker type, attacker id | **confirmed** |
| target type, target id | **confirmed** |
| skill vnum (field 5) | **probable** — `0` for a monster's basic attack, `226` for a player skill |
| damage (field 13) | **probable** — `698` against a monster whose max HP is 310, i.e. an overkill; matches the `die` that follows |
| **field 11** | **confirmed — bandiera di presenza** dei due campi finali |
| field 12 | **non decodificato**: sembra una percentuale e non lo è (con 475/888, cioè 53,5%, vale 59; il massimo osservato è 112) |
| **fields 16, 17** | **confirmed** — HP corrente e HP massimo del bersaglio, **letti dal decoder dal 2026-09-07** |

**Misurato su 212 pacchetti `su`** di quattro catture (`nostale_live` 13,
`equip_test` 2, `nostale_combat` 117, `certificazione` 80; `nostale_01` non ne
ha), tutti a 18 campi:

| Misura | Risultato |
|---|---|
| `fields[16] <= fields[17]` e `fields[17] > 0` | 212 su 212 |
| `fields[17]` costante per id bersaglio | 40 bersagli distinti, zero con due valori |
| `fields[11] == 1` ⟺ `fields[16] > 0` | 212 su 212, zero controesempi |

**La bandiera è il punto.** Quando `fields[11]` vale `0` — **40 pacchetti su 212,
il 18,9%** — `fields[16]` vale `0`, e quello zero significa *non riferito*, non
*morto*: nelle stesse catture ci sono **5** `die` in tutto. Un decoder che
leggesse la coppia senza guardare la bandiera pubblicherebbe quaranta morti
inventate. `NosTaleWorldProtocolDecoder.DecodeHit` legge i due campi solo con la
bandiera a 1, e mai per il personaggio controllato: su quella vita `stat` è già
la fonte, e due numeri diversi allo stesso istante sarebbero un conflitto
inventato da noi.

### Che cosa cambia davvero, misurato dopo

**Non sono entità nuove: è vita più fresca.** Rigiocando le catture attraverso il
World Model, gli id che `su` porta e `st` no sono **uno** in `nostale_combat` e
**uno** in `certificazione` — `st` copriva già quasi tutti i bersagli. Quello che
cambia è quante volte la vita di un bersaglio viene riletta:

| cattura | letture da `st` | letture da `su` |
|---|---:|---:|
| `nostale_combat` | 49 | **94** |
| `certificazione` | 66 | 69 |
| `nostale_live` | 19 | 9 |

Durante un combattimento `su` arriva a ogni colpo e `st` solo ogni tanto: nella
cattura più combattuta la vita del bersaglio si aggiorna quasi il doppio delle
volte. È un guadagno di **freschezza**, che è ciò che serve a decidere se
continuare o disingaggiare, e non un guadagno di copertura.

`su` is the per-hit event stream: who hit whom, with what, for how much, and the
target's resulting HP. It is the highest-value packet for combat reasoning after
`stat`.

---

## `sayi` — un messaggio con un argomento

```
sayi 1 3548294 12 975 2 8 1 0 0
     ty id      ?  id  ?  arg ? ? ?
```

| Campo | Confidenza |
|---|---|
| tipo, id del personaggio (1, 2) | **confermato** |
| campo 3 | **non decodificato.** Scritto qui il 2026-09-07 come «si muove sempre insieme al campo 4»: **falso**, e lo smentisce `nostale_combat`, dove il messaggio 975 esce sia con `11` sia con `12`. Non è funzione dell'id |
| **campo 4 — id del messaggio** | **confermato** |
| **campo 5 — tipo dell'argomento** | **confermato**: vale `2` quando il campo 6 è un vnum, e il campo 7 vale `1` esattamente allora — 20 pacchetti su 20 |
| **campo 6 — argomento** | **confermato**: è il vnum dell'oggetto quando il campo 5 vale 2 |

**Come è stato stabilito**, il 2026-09-07, con `data/messaggi.noscap` — 2657
pacchetti, registrati apposta. La cattura contiene l'evento intero:

```
drop 8 4867701 51 154 1 0 0      l'oggetto vnum 8 cade a terra
get  1 3548294 4867701 0         il personaggio lo raccoglie
sayi 1 3548294 12 975 2 8 1 0 0  il messaggio nomina 8
ivn  0 0.8.0.0.0.0.0             8 entra nell'inventario
```

Il messaggio **975** compare in due catture indipendenti con due argomenti
diversi — `8` qui, `2006` in `nostale_combat` — e in entrambe l'argomento è il
vnum dell'oggetto appena raccolto, corroborato da `drop`, `get` e `ivn`. Il vnum
risolve nel catalogo: 8 è «Fionda in legno», 13 «Uniforme da allenamento».

**Il campo 5 è il tipo dell'argomento.** Su tutti e 20 i pacchetti `sayi` delle
sei registrazioni, il campo 7 vale `1` esattamente quando il campo 5 vale `2`, e
quando vale 2 il campo 6 è un vnum già corroborato altrove. Con `4` (argomento
50) e con `3` (argomento 2) il campo 7 è `0`, e l'argomento non è un oggetto.

### Quello che resta aperto

**L'id del messaggio non indicizza il catalogo.** Cercato `zts975?e` — con il
carattere marcatore, la stessa regola che risolve i nomi dei mostri — in **tutte
e dodici** le tabelle di `NSlangData_IT.NOS`, per 975, 654, 697 e 2110: escono
solo voci scollegate («Kamil», «Tinta per capelli lilla»). Il testo dei messaggi
di sistema **non è in quell'archivio**. Il candidato non ancora aperto è
`NScliData_IT.NOS`, che l'importatore non legge.

Quindi: il legame **id → argomento → catalogo** è chiuso; il legame
**id → testo del messaggio** no, e ora si sa dove non cercarlo.

---

## `in` — an entity enters view

```
in 3 36 313826 109 63 2 100 100 0 0 0 -1 1 0 -1 - 0 -1 0 …
   ty vnum id  x   y  d hp% mp% …
```

| Field | Confidence |
|---|---|
| type, vnum, id | **confirmed** — vnum groups identical monsters (`36`, `45`, `9`, `96` seen) |
| x, y | **probable** — consistent with the `mv` that follows for the same id |
| direction (field 6) | **probable** |
| HP percent, MP percent | **probable** — `100 100` on spawn |
| remainder | **unknown** — long tail, mostly `0` and `-1`, one `-` (empty string field) |

Spawn is where an entity's **vnum** is learned; `mv` afterwards carries only the id.
Anything that needs to know *what* a monster is has to keep the `in` mapping.

---

## `mv` — an entity moved

```
mv 3 3194 121 110 5
   ty id   x   y   speed
```

All fields **confirmed** by consistency across 7685 packets and continuity with `in`.
**Never carries the player** — see *What the wire cannot tell us*.

---

## `lev` — the player's progression

```
lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0
    lv xp      jl jXp   xpMax    jXpMax rep   ?
```

| Field | Confidence |
|---|---|
| level, XP, job level, job XP | **probable** — XP rises monotonically across the capture while the others hold |
| XP max, job XP max | **probable** — constant, and larger than the running values |
| field 7 (`35106`) | **unknown** — candidate reputation |
| remainder | **unknown** |

---

## `cond` — movement and action state

```
cond 1 3443217 0 0 11
     ty id     ? ? speed
```

| Field | Confidence |
|---|---|
| type, id | **confirmed** |
| fields 3, 4 | **probable** — candidates: cannot-attack, cannot-move. Both `0` throughout, so never observed asserted |
| speed (field 5) | **probable** — `11`, plausible for a level 56 character. **Unit unknown**: see below |

Il valore di `speed` resta decodificato e non usato finché non se ne conosce
l'unità. L'uso ovvio sarebbe riconoscere uno spostamento impossibile fra due
posizioni osservate — quindi una lettura corrotta — ma `11` senza scala non si
converte in una distanza per unità di tempo, e sceglierne una a caso
trasformerebbe un controllo di plausibilità in una sorgente di falsi allarmi.

Per determinarla basta una cattura sola: il personaggio cammina fra due punti
noti, `cond` dichiara la velocità, `mv` dà posizioni e istanti, e il rapporto
fra distanza percorsa e tempo trascorso dà la scala.

Da non confondere con la velocità istantanea: quella di `cond` è una
statistica del personaggio, e per questo il commento di
`TemporalBelief.EstimateVelocity` — «velocity is never itself observed» —
resta vero anche ora che questo campo è decodificato.

---

## La prova che `eq`, `equip` e `ivn` si confermano a vicenda

`data/equip_test.noscap` e' una sessione registrata mentre si equipaggiava e
disequipaggiava, e la sua evidenza non ha bisogno di nessuna fonte esterna: **tre
vnum escono da uno slot di `equip` ed entrano in uno slot di `ivn`, dentro la
stessa cattura**.

| vnum | esce da | entra in |
|---|---|---|
| `309` | slot equip 6 (presente nel 1º pacchetto, assente dal 2º) | `ivn 0 15.309.…` |
| `518` | compare in slot equip 8 (3º pacchetto) | `ivn 0 16.518.…` |
| `284` | slot equip 11 (assente dal 5º pacchetto) | `ivn 0 18.284.…` |

Un decoder che leggesse male uno dei due opcode romperebbe la corrispondenza, e
un test la fissa. E' il tipo di riscontro che questo documento preferisce a
qualunque tabella di terze parti: due letture indipendenti degli stessi byte che
devono raccontare la stessa storia.

`--world-replay data/equip_test.noscap` riporta l'unione degli slot visti nella
sessione — `0 2 4 5 6 8 9 10 11 12` — accanto all'ultimo insieme noto, proprio
perche' gli slot che sono andati e tornati sono l'informazione, non il rumore.

---

## Misurato invece che letto — `--wire-inspect` (2026-09-07)

Le confidenze di questo documento sono state assegnate leggendo i pacchetti a
mano. Da oggi c'è un comando che le misura:
`--wire-inspect <file.noscap>` stampa, per ogni opcode, quante volte compare,
quanti campi porta e — per ogni posizione — se il valore è **sempre lo stesso**
o quanti distinti ne ha assunti. È la regola di `CLAUDE.md` § *External reference
data* resa meccanica: un campo che non è mai cambiato non si distingue da una
costante che il server manda sempre, quindi la cattura non può confermargli
alcun significato.

Eseguito su `data/nostale_combat.noscap`, conferma tre affermazioni che questo
documento faceva a mano, e ne aggiunge due che nessuno aveva notato:

| Riga di questo documento | Cosa dice il censimento |
|---|---|
| `cond` campi 3 e 4 — «Both `0` throughout, so never observed asserted» | `3=0 4=0` su tutti e 72 i pacchetti: **confermato meccanicamente** |
| `lev` — XP e job XP salgono, gli altri tengono | `2:23var 4:23var`, tutti gli altri costanti: **confermato** |
| `guri`, `icon`, `delay`, `cancel`, `ms_c` — **unknown** | ogni campo costante su tutte le occorrenze: **inconfermabili per misura**, non per pigrizia |
| — | `stat` campo 2 = `7305` e campo 4 = `1420` costanti in 62 letture: sono gli **estremi**, e restano fermi mentre i campi 1 e 3 variano |
| — | `in` campo 16 è `-`, l'unico campo **non numerico** dei 31; `delay` campo 3 è `#guri^400^3324`, l'unico altro payload testuale della cattura |

`sayi` è il solo opcode non letto con più campi variabili (`3:3var 4:2var 5:2var
6:3var 7:2var`): è quindi il primo candidato ragionevole se qualcuno vorrà
chiudere un altro buco di decodifica, e l'unico su cui una cattura sola dica
abbastanza per provarci.

---

## Events

| Opcode | Seen | Shape | Reading |
|---|---:|---|---|
| `die` | 4 | `die 3 313820 3 313820` | An entity died — **confirmed**, each followed the `su` that overkilled it |
| `drop` | 3 | `drop 2006 1092257 110 63 1 0 3443217` | vnum, drop id, x, y, amount, ?, owner id — **probable** |
| `get` | 2 | `get 1 3443217 1092257 0` | Picked up: taker type/id, drop id — **probable**, ids match a preceding `drop` |
| `ivn` | 3 | `ivn 2 34.2006.1.0` | Inventory slot: `slot.vnum.amount.rarity` — **probable**, vnum `2006` matches the `drop` |
| `eq` | 6 | `eq 3443217 0 0 1 2 1 221.-1.262.157.224.279.-1.-1.-1.-1.-1 25 0 100` | Cio' che il personaggio indossa, in un gruppo puntato di **undici** posizioni, `-1` per lo slot non occupato — **probable**. Cinque occupate in questa cattura |
| `equip` | 6 | `equip 25 0 0.262.5.2.0.0.0 2.221.0.0.0.0.0 …` | Lo stesso insieme, per slot, con dettaglio: `slot.vnum.<altri cinque>` — **probable**. Su sei pacchetti l'insieme **cambia**, ed e' cio' che rende questa cattura piu' di un documento |
| `eff` | 6 | `eff 3 313909 5000` | Visual effect on an entity — **probable** |
| `sr` | 17 | `sr 0`, `sr 2`, `sr 6` | Skill ready / cooldown ended, by skill slot — **probable** |
| `ski` | 0 | — | Elenco delle abilita' del personaggio — **mai osservato**. Censito il 2026-09-07 su tutte e cinque le catture (27 726 messaggi inbound): zero occorrenze. Non e' assenza dal protocollo ma assenza dalle *nostre* registrazioni: e' inviato una volta al caricamento del personaggio, e ogni cattura di questo repository comincia a client gia' in gioco. OpenNos lo descrive come `ski {skibase}{elenco vnum}` — pista non riscontrata, nessun valore osservato con cui confrontarla |
| `ct` | 108 | `ct 3 313816 1 3443217 -1 -1 0` | Targeting between two entities — **probable** |
| `sayi`, `msgi` | 18 | `sayi 1 3443217 12 975 2 2006 1 0 0` | Id di messaggio e **argomento**: campo 4 l'id, campo 6 l'argomento — vedi sotto |
| `guri` | 6 | `guri 2 1 3443217 0` | **unknown** |
| `icon` | 2 | `icon 1 3443217 1 2006` | **unknown** |
| `delay` | 6 | `delay 4000 4 #guri^400^3324` | Timed action, ms + a callback string — **probable**; the only packet with non-numeric payload |
| `cancel` | 1 | `cancel 1 1092257 -1` | **unknown** |
| `ms_c` | 2 | `ms_c 0` | **unknown** |

---

## What this gives the runtime today

Directly available, per ADR-0014's `LIVE` bar, through `NosTaleWorldFramer` +
`NosTaleWorldProtocolDecoder` + `NetworkGameplayProvider`:

- **Own vitals** — HP, max HP, MP from `stat`, updating per hit.
  Published `LIVE` when the capture itself is live. Max MP is confirmed on the
  packet, used to reject a malformed `stat`, e ora pubblicato sullo snapshot
  come `maxMp` (F4-1b). HasTarget and InCombat are **not** read from `stat` (fields 5
  and 6 are unknown); `HasTarget` is established from the screen instead
  (ADR-0018, below), and `InCombat` stays `UNKNOWN`.
- **Target vitals** — absolute HP and max HP of any entity in view, from `st`
  (fields 7 and 9; field 5 is ignored) e, **dal 2026-09-07, anche da `su`** (campi 16
  e 17, letti solo con la bandiera del campo 11 a 1 — sezione `su` sopra). In
  combattimento `su` è la fonte più fresca: in `nostale_combat` 94 letture contro le
  49 di `st`.
- **Combat events** — every hit with attacker, target, skill and damage, from `su`.
- **Entities in view** — spawn with vnum and position from `in`, tracked by `mv`,
  removed by `die`. `mv` pubblica la posizione **anche senza vita nota**: in quel caso
  l'avvistamento non porta HP (mai uno zero); quando la vita è già nota da
  `in`/`st`/`su` la porta con l'istante in cui fu letta, marcata stale
  (`NosTaleWorldProtocolDecoder.DecodeMove`).
- **Progression** — level, XP, job level, job XP and the two maxima, from `lev`.
  **Published dal 2026-09-07**: `NosTaleWorldProtocolDecoder` legge i primi sei
  campi, `DecodedObservations.Progression` li porta, e `--world-replay` li
  stampa. I campi dal settimo in poi (`35106 7 0 0 1 0`) restano **non
  decodificati** e non per pigrizia: sono identici in tutti e 38 i pacchetti
  delle tre registrazioni che ne portano, mentre XP e job XP si muovono in
  ognuna — un valore che non e' mai cambiato non si distingue da una costante
  che il server manda sempre. Il censimento della cattura di combattimento e'
  salito da 8147/8211 a **8170/8211**.
- **Drops and inventory** — `drop`, `get`, `ivn`, **pubblicati** come `GroundItem`, `ItemPickup` e `InventorySlotReading`.
- **Equipaggiamento indossato** — `eq` ed `equip`. **Published dal 2026-09-07**:
  `WornEquipment` porta slot e vnum come il filo li dichiara, senza mapparli su
  `EquipmentSlot` (quella corrispondenza ha bisogno di `Item.dat` e di
  un'evidenza propria). Su `equip_test.noscap` il censimento e' salito da
  3541/3584 a **3553/3584**, con 12 letture.

Not available from the server, and needing the confirming source:

- **The player's own position.**
- **Anything the client decides locally** before telling the server.
- **Whether the player has a target, and whether the player is in combat.** No
  packet in either capture establishes either. `ct` carries targeting between two
  entities and `su` carries every hit, but neither has an observed "target
  cleared" counterpart, so a flag derived from them would be sticky and wrong in
  a way nothing on the wire would correct.
  [ADR-0016](adr/ADR-0016-planning-and-acting-on-partial-observation.md) makes
  the planner skip the rules that read them instead of blocking every rule that
  does not.

  `HasTarget` now has a source, and it is not this one:
  [ADR-0018](adr/ADR-0018-establishing-the-target-from-the-screen.md) has the
  screen establish it, because the target frame disappears and the screen is
  therefore the only source that can say *no*. The wire's contribution is a
  `su` in which the player is the attacker — attacker type `1`, the
  player-attacks shape above — which **contradicts** a screen that saw no frame
  and never establishes the fact by itself. Until the operator calibrates the
  target ROI against a real client, `HasTarget` stays UNKNOWN with the reason
  `target_roi_not_calibrated`. `InCombat` is still unsourced.

## What the runtime does not read

Replaying the combat capture through the shipping decoder
(`WinDivertProbe.exe --world data/nostale_combat.noscap`) reports 7942 of 8211
packets carrying an opcode it reads, and 7741 sightings across 164 distinct
entities, with 287 packets producing no observation at all. The same replay
reported 629 packets producing an observation while a sighting had to carry
health; letting a sighting state a position without one is what closed the gap.
What is left out is left out on purpose, and it is worth stating so nobody
chases it:

- **`mv` dominates the wire and carries no health.** 7685 of 8211 packets are
  movements. `EntitySighting.HpRatio` is nullable, so a movement now produces a
  sighting that says where the entity is and says nothing about its health;
  filling that in with full health, or with a zero, would have been an invented
  observation, and dropping the packet threw away the position along with it.
  Health still comes only from `in` or `st`, and a capture that starts
  mid-session has 25 `in` and 49 `st` against those 7685 `mv` — so most entities
  are located long before their health is ever known.
  `EntitySighting.ToDetection()` returns null for such a sighting rather than a
  `Detection` at zero HP, because zero HP is a dead mob to the world model.
- **Quali tipi di entità si leggono** (aggiornato il 2026-09-08; questo punto
  diceva «solo il 3», ed era vero fino al giorno prima). Il tipo **3** e il tipo
  **2** si leggono in `in`, `mv` e `st`; il tipo **1** solo in `mv`. Il `in` del
  tipo 1 porta un **nome** dove gli altri portano il vnum, quindi id, x e y stanno
  un campo più in là e leggerlo alle posizioni degli altri prenderebbe una
  coordinata da qualcos'altro; il suo `st` non è mai stato osservato. Vedi le due
  sezioni dedicate qui sopra per le misure.
- **Opcodes marked unknown are not read at all**, which is 269 packets here:
  `ct`, `cond`, `lev`, `sr`, `sayi`, `eff`, `delay`, `guri`, `msgi`, `drop`,
  `ivn`, `get`, `icon`, `ms_c`, `cancel`.
