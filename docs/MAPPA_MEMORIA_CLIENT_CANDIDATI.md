# Mappa di memoria del client — offset candidati da fonte terza

**Stato:** ipotesi in gran parte non provate. Il 3 settembre 2026 il gruppo statistiche
(§ 4.2) è stato messo alla prova su un client reale: metà ha retto, metà è stata
confutata — § 7. Tutto il resto resta non provato. Nessun numero di questo documento
è `LIVE`.
**Data:** 2 settembre 2026, aggiornato il 3 settembre 2026
**Si appoggia a:** [ADR-0014](adr/ADR-0014-operator-chooses-the-data-path.md),
[ADR-0012](adr/ADR-0012-gameplay-observation-source.md),
`src/NosAi.Runtime/LiveIntegration/NosTaleClientLayout.cs`

## 1. Che cosa è questo documento e che cosa non è

È un elenco di **candidati**: offset e catene di puntatori estratti da un bot NosTale
di terze parti (DLL C/C++ iniettata, con la relativa tabella Cheat Engine), riferiti a
una build del client **ignota e quasi certamente diversa** da quella corrente.

Non è una mappa da cablare nel codice. ADR-0012 ha stabilito il motivo, e ADR-0014 non
lo ha revocato: *un offset sbagliato non fallisce, restituisce un numero plausibile*.
Un numero copiato da un bot altrui è esattamente la forma di errore che questo progetto
rifiuta ovunque — funziona finché non funziona, e quando non funziona non lo dice.

Il valore di questo elenco è duplice e limitato:

1. **Corroborazione.** Dove una fonte indipendente concorda con quello che
   `NosTaleClientLayout` ha già stabilito con i propri oracoli, la concordanza vale
   come seconda fonte nel senso che ADR-0014 chiede.
2. **Ipotesi di partenza.** Dove descrive campi che il layout attuale non legge —
   nomi delle entità, HP/MP, cooldown — indica *dove guardare*, non *cosa credere*.
   Il percorso da ipotesi a lettura è descritto in
   [SPEC_ESTENSIONE_LAYOUT_MEMORIA.md](SPEC_ESTENSIONE_LAYOUT_MEMORIA.md).

## 2. Convenzione della fonte

La fonte risolve le catene così, e ogni offset qui sotto va letto con questa regola:

```text
addr = moduleBase + rva
addr = *addr
per i in 0 .. n-2:   addr = *(addr + offsets[i])
addr = addr + offsets[n-1]          // indirizzo finale, NON dereferenziato
```

Nota: con un solo offset il ciclo non gira e l'offset viene sommato dopo la prima
dereferenza. Con lista vuota la fonte va in undefined behavior; non replicarlo.

## 3. Classe A — concordanze con quanto già stabilito

Queste **non sono novità**: sono conferme. Il layout del repo le ha ricavate per conto
proprio, con oracoli comportamentali e verifica al riavvio. Che una fonte indipendente,
scritta contro un'altra build, descriva gli stessi campi negli stessi punti è la cosa
più vicina a una prova che si possa avere senza il sorgente del client.

| Campo | Fonte terza | `NosTaleClientLayout` | Esito |
|---|---|---|---|
| Entity id | `+0x08` sull'oggetto entità | `EntityIdOffset = 0x08` | concorda |
| Posizione entità | `+0x0C` X int16, `+0x0E` Y int16 | `PositionOffset = 0x0C`, letto come un `uint32` con X nella metà bassa e Y nell'alta | concorda: sono la stessa cosa descritta in due modi |
| Impacchettamento coordinate | le call del client vogliono `Y * 65536 + X` | il layout decodifica `x = packed & 0xFFFF`, `y = packed >> 16` | concorda |
| Array delle liste | offset intermedio `0x04` nelle catene lista | `ListArrayOffset = 0x04` | concorda |

La terza riga merita una nota: la fonte usa quell'impacchettamento in **scrittura**
(è il parametro che passa alle funzioni di movimento del client), il repo lo trova in
**lettura**. Due usi opposti dello stesso formato sono un indizio più forte di due
letture concordi, perché un errore di lettura ripetuto identico è possibile, un errore
che sopravvive anche all'andata è molto meno probabile.

## 4. Classe B — campi nuovi, plausibili, non verificati

Nessuno di questi esiste oggi in `NosTaleClientLayout`. Sono le ipotesi utili.

### 4.1 Nome delle entità

| Entità | Catena dall'oggetto entità |
|---|---|
| Mostro | `+0x1BC` → `+0x04` = `char*` ANSI |
| Item a terra | `+0xC4` → `+0x38` = `char*` ANSI |

Le due catene sono diverse fra mostri e item: non assumere simmetria. La tabella Cheat
Engine etichetta indipendentemente `+0xC4 → DoNazwy` sulla struttura "Monsters" e
`+0x38 → Nazwa Przedmiotu` sull'item, il che suggerisce che esistano più percorsi al
nome e che quello sopra sia solo uno dei due.

### 4.2 Statistiche del personaggio

Catena della fonte: RVA `0x004F4BA8`, offset `{0xE4, 0x100, 0x4C8, 0x8B8}`.

| Campo | Offset dal blocco | Esito sulla build corrente |
|---|---|---|
| MaxMP | `+0x00` (uint32) | **confutato** — § 7.3 |
| MP | `+0x04` (uint32) | **confutato** — § 7.3 |
| MaxHP | `+0xF0` (uint32) | **confermato per la sola parte verificabile**: MaxHP sta nei quattro byte immediatamente prima di HP — § 7.2 |
| HP | `+0xF4` (uint32) | idem |

Questo gruppo non è più un'ipotesi intera, ed è l'unico della Classe B su cui esista una
misura. Provato il 3 settembre 2026 contro un client reale si è rotto a metà:
l'adiacenza fra `MaxHP` e `HP` c'è, il blocco unico che dovrebbe contenere anche `MaxMP`
e `MP` no. Quello che `+0xF0`/`+0xF4` descrivono correttamente è una
distanza di quattro byte fra un massimo e il suo corrente; quello che descrivono male è
dove sta l'altra coppia, che sulla build misurata non è a `-0xF4` da HP ma `0x78` più
avanti, nel record successivo (§ 7.4).

È il campo più interessante che manca al runtime: ADR-0012 indicava HP/MP come primo
provider di gameplay, e finora non c'è. **Non usare l'RVA, e non usare gli offset
assoluti dal blocco:** il blocco che li rende sensati non è stato trovato. La strada che
ha funzionato non parte da nessuno dei due — parte dai quattro interi che il filo dà su
`stat` e li cerca in memoria; vedi la § 5 della spec di estensione.

### 4.3 Cooldown delle abilità

| Campo | Catena | Nota |
|---|---|---|
| Abilità 1-4 | RVA `0x004F4DD0`, `{0x158, 0x4, 0x4, 0x0, 0x24}` | passo `(n-1) * 0x48`, valore `0` = pronta |
| Abilità 5+ | RVA `0x004F4CDC`, `{0x20, 0x4, 0x88, 0xE28, 0x24}` | stesso passo |
| Numero abilità | RVA `0x004F4C70`, `{0x3EC, 0x768}` | |

Due tabelle distinte per due intervalli di abilità è una struttura strana abbastanza da
essere probabilmente vera: nessuno la inventerebbe. La tabella Cheat Engine riporta per
`Skill1` una catena leggermente diversa (`{0x158,0x4,0x4,0x0,0x8,0x14}`) da quella nel
codice (`{0x158,0x4,0x4,0x0,0x24}`): **le due fonti non concordano con se stesse**, il
che declassa ulteriormente questo gruppo.

### 4.4 Item a terra — campi oltre la posizione

| Campo | Offset |
|---|---|
| Id item | `+0x08` |
| Quantità | `+0x20` (uint32) |
| Flag consumabile | `+0x0C` |

Attenzione: `+0x08` come "id item" confligge con `EntityIdOffset = 0x08` già stabilito.
O è lo stesso campo con due nomi, o una delle due letture è sbagliata. Va risolto prima
di usare uno dei due su un item.

### 4.5 Altri

| Campo | Catena | Nota |
|---|---|---|
| Portata d'attacco | RVA `0x004F4904`, `{0x68}`, BYTE | la fonte la corregge a mano (`-3`, e `+1` per il corpo a corpo): sospetto che non sia una portata pura |
| Partner | RVA `0x004F4908`, `{0x4, 0x0}` | |
| Pet | RVA `0x004F4908`, `{0x4, 0x4}` | |
| Entità selezionata | RVA `0x004F4DC0`, `{0x1D0,0x20,0x4,0xC,0x9A0}` | il repo ha già `TargetPointerOffset = 0x44` sul manager, trovato per oracolo: **percorso diverso, non usare questo** |

## 5. Classe C — inutilizzabile

Tutti gli RVA di base (`0x004F4904`, `0x003566D8`, `0x003582C0`, `0x00360E7C`, …) e le
catene di navigazione verso le liste entità (`{0xEA4,0x4,0x5E4,0x0}` per i mostri,
`{0xEB0,0x4,0x5C4,0x0}` per gli item). Motivi, in ordine di gravità:

1. Appartengono a un'altra build. Un RVA è un fatto su un binario, non sul gioco.
2. Il repo raggiunge le stesse liste da un'altra parte — scene manager per signature,
   liste a `+0x0C/+0x10/+0x14/+0x18` — e quel percorso è già verificato. Sostituirlo
   con questo sarebbe una regressione, non un'aggiunta.
3. La fonte itera `monsterCount - 1`. Non è chiaro se sia un fuori-di-uno voluto o un
   bug; in entrambi i casi non è un conteggio di cui fidarsi.

## 6. Errori noti nella fonte

Registrati perché qualcuno, prima o poi, riaprirà il codice originale.

- Precedenza sbagliata: `skill - 1 * 0x48` invece di `(skill - 1) * 0x48`. Presente in
  un ramo e corretto in un altro dello stesso file.
- Condizione di prossimità con `&&` dove serve `||`: la raccolta si ferma appena il
  personaggio è allineato su un asse.
- Nessuna validazione dei puntatori prima della dereferenza. La fonte gira **dentro** il
  processo del client, quindi una catena rotta lo fa crashare. Noi leggiamo da fuori:
  lo stesso errore da noi produce `UNKNOWN`, che è il motivo per cui leggiamo da fuori.

## 7. Prima prova su client reale — 3 settembre 2026

Fino a questa data nessuna riga della Classe B era stata messa alla prova: il documento
elencava ipotesi e diceva che erano ipotesi. Il gruppo statistiche (§ 4.2) è stato
provato, e l'esito è misto. Va registrato qui, perché la parte confutata non si
distingue in nessun altro modo da quella confermata: entrambe hanno l'aria di un offset
scritto da qualcuno che aveva il client davanti.

**Sessione.** Client NosTale reale, personaggio `3443217`. Seconda fonte indipendente:
il pacchetto `stat` sul filo, che porta `hp maxHp mp maxMp` come interi assoluti
(`docs/PROTOCOLLO_NOSTALE.md`) e che non vede la memoria del client. Strumenti:
`--memory-scan` / `--memory-narrow` / `--memory-dump` nella prima sessione, e in una
seconda sessione — a PC riavviato — la calibrazione automatica `--calibrate-vitals`.

### 7.1 Esito per candidato

| § | Candidato | Esito |
|---|---|---|
| 3 | Classe A — id entità, posizione, impacchettamento, array delle liste | non toccato da questa sessione; restano le concordanze già registrate lì |
| 4.1 | Nome delle entità | **non provato** — la lettura esiste, la concordanza con `in` su sessione reale non è ancora registrata |
| 4.2 | `MaxHP` e `HP` adiacenti (`+0xF0`, `+0xF4`) | **confermato** — § 7.2 |
| 4.2 | `MaxMP` e `MP` nello stesso blocco (`+0x00`, `+0x04`) | **confutato** — § 7.3 |
| 4.3 | Cooldown delle abilità | **non provato** |
| 4.4 | Item a terra — id, quantità, flag consumabile | **non provato**; il conflitto fra `+0x08` e `EntityIdOffset` resta aperto |
| 4.5 | Portata d'attacco, partner, pet, entità selezionata | **non provato** |
| 5 | Classe C — RVA di base e catene di navigazione | **non provato**, e non c'è ragione di provarlo: vale ancora quanto detto lì |

«Non provato» vuol dire non provato. Non è un mezzo esito, non è un indizio a favore, e
il fatto che una riga confinante abbia retto non dice niente sulle altre — § 4.2 è
proprio il caso in cui due righe adiacenti dello stesso gruppo hanno avuto esiti opposti.

### 7.2 Confermato — MaxHP sta nei quattro byte immediatamente prima di HP

Una scansione differenziale sull'intero processo (`--memory-scan` seguito da
`--memory-narrow`, tre passaggi, con HP che saliva da 6891 a 7060 e tornava a scendere)
ha identificato HP a `0x1F7AEC7C`. Il dump attorno a quell'indirizzo:

```text
0x1F7AEC78  -004   7305   0x00001C89   <- MaxHP
0x1F7AEC7C  +000   7305   0x00001C89   <- HP
```

Il filo riportava per lo stesso personaggio `maxHp = 7305`. Le due parole adiacenti
portano quel numero, e lo dice anche una sorgente che la memoria del client non la vede:
è l'adiacenza che la fonte terza descrive con i suoi `+0xF0` e `+0xF4`.

Due precisazioni, perché questo dump da solo non dice tutto. Le due parole hanno qui lo
stesso valore, quindi non è questo dump a stabilire *quale* delle due sia il massimo:
lo stabilisce la calibrazione della § 7.4, dove differiscono e il massimo sta
all'indirizzo più basso. E ciò che è confermato è **l'adiacenza**, non gli offset:
`0xF0` e `0xF4` sono distanze dall'inizio di un blocco che su questa build non esiste.

### 7.3 Confutato — il blocco unico `{MaxMP, MP, MaxHP, HP}` non esiste su questa build

La fonte mette `MaxMP` a `HP - 0xF4` e `MP` a `HP - 0xF0`. Con HP a `0x1F7AEC7C` il
blocco comincerebbe a `0x1F7AEB88`. Il dump lì riporta `0` a `+0x00` e `107` a `+0x04`,
dove il filo dava `maxMp = 1420`.

Non è uno sfasamento di qualche byte: in 128 byte attorno a HP il valore 1420 non
compare da nessuna parte. Le due coppie non stanno nella stessa struttura, e la catena
della fonte non descrive questa build per la parte MP — indipendentemente dal fatto che
la descriva per la parte HP.

### 7.4 Misurato — i record distano `0x78`, e HP e MP sono lo stesso campo di due record consecutivi

In una sessione successiva, dopo un riavvio completo del PC e con un processo del client
nuovo, la calibrazione automatica `--calibrate-vitals` — due giri contro il filo — ha
stabilito:

```text
MaxHP/HP  0x1F52EC78   4117/7305
MaxMP/MP  0x1F52ECF0   1334/1420
differenza = 0x78
```

Il valore stampato è `corrente/massimo`, e il massimo sta all'indirizzo indicato con il
corrente quattro byte dopo: HP 4117 su 7305, MP 1334 su 1420, entrambi i massimi uguali
a quelli che il filo dava. Le due coppie hanno la stessa forma e distano `0x78`.

Un dump della sessione precedente mostrava, per conto suo, una struttura che si ripete
ogni `0x78` byte, con puntatori dentro l'intervallo dei moduli (`0x0049DD84`,
`0x00738754`) nella stessa posizione di ogni record — la forma di un array di oggetti
che portano ciascuno una vtable.

La lettura che ne segue è che HP e MP non siano due campi di una struttura
«statistiche», ma lo **stesso** campo di due record consecutivi. È una descrizione più
semplice di quella della fonte e spiega perché la sua non ha retto. Non è ancora una
spiegazione provata: che cosa siano quei record, e quale oggetto sia il primo e quale il
secondo, non è noto.

### 7.5 Misurato — del vecchio indirizzo sono sopravvissuti al riavvio i 16 bit bassi

Prima sessione: `0x1F7AEC78`. Seconda sessione, PC riavviato e processo nuovo:
`0x1F52EC78`. Si è mossa solo la metà alta.

Il fatto misurato è questo e nulla di più: due sessioni, la stessa metà bassa. Suggerisce
che lo scostamento dentro l'allocazione che contiene il record sia stabile mentre la base
dell'allocazione non lo è. Due campioni non sono una regola: non è noto se la metà bassa
regga a un terzo avvio, e non è noto perché regga.

### 7.6 Aperto — l'indirizzo è heap e non ha ancora un'ancora

Quello che la sessione consegna è un indirizzo assoluto in memoria heap. Il riavvio ne
ha ucciso uno, il che chiude la domanda se un'ancora serva: serve. Esprimere la coppia
come distanza da una base risolta a ogni aggancio è lavoro non fatto, non una
conclusione, e finché non è fatto ciò che esiste è una calibrazione da rifare a ogni
sessione — non una lettura.

## 8. Che cosa fare con questo documento

Niente, direttamente. È materiale in ingresso per
[SPEC_ESTENSIONE_LAYOUT_MEMORIA.md](SPEC_ESTENSIONE_LAYOUT_MEMORIA.md), che descrive
come un candidato diventa una lettura. Un offset di qui che comparisse in `src/` senza
essere passato da quella procedura è un difetto, non una scorciatoia.

Vale anche per la § 7. Una conferma su una sessione è corroborazione — la stessa cosa
che la § 1 dice di questo elenco — e non autorizza a cablare un numero.

Una conseguenza per chi legge il codice, e che nessuno ha ancora tratto: gli offset
`0x00/0x04/0xF0/0xF4` sono costanti in `PlayerVitalsBlock`, che descrive il blocco della
fonte come se esistesse, e la sonda che li usa cerca perciò una struttura che la § 7.3
dice non esserci su questa build. Le costanti non sono un indirizzo cablato e nessuna
lettura viene classificata su di esse, quindi non è il difetto di cui parla il capoverso
qui sopra; è però una forma cercata che, misurata, non c'è.
