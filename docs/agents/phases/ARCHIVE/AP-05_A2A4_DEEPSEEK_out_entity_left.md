# AP-05 / A2+A4 — DeepSeek — `out`: l'entità esce, e uscire non è morire

**Nessun task in coda: prendibile subito.** Tocca
`src/NosAi.Runtime/Perception/Network/` (decoder + contratto evento),
`src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`,
`src/NosAi.Runtime/Observability/WireInspectCommand.cs` e file di test nuovi.
**Non tocca** la calibrazione, il pannello di controllo, né `Program.cs` oltre la
registrazione dell'opzione nuova.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/PROTOCOLLO_NOSTALE.md` § `in`, § `die`, e la tabella dei tipi entità in
   testa — i tipi 1, 2, 3 sono tutti letti dal 2026-09-08.
3. `NosTaleWorldProtocolDecoder.DecodeDeath` (righe 281-294) — **il modello
   esatto da seguire**: legge il vnum *prima* di rimuovere, perché dopo la
   rimozione la specie non è più recuperabile.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**
(non c'è hook).

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci.

---

## Il fatto, misurato

Il decoder legge quindici opcode. `out` non è fra questi, e viene scartato in
silenzio dal `_ => DecodedObservations.Empty` finale.

Misurato il 2026-09-08 con `--wire-inspect` sulle sei catture di `data/`:

| Misura | Risultato |
|---|---|
| catture che contengono `out` | **1 su 6** — solo `data/messaggi.noscap` |
| pacchetti `out` | **18** |
| numero di campi | **2 su 18 su 18** — nessuna variante |
| valori del campo 1 (tipo entità) | **1** (×7), **2** (×5), **3** (×6) |
| id distinti al campo 2 | **15** |
| di quei 15, quanti compaiono anche in `in`/`mv`/`st`/`su`/`ct`/`die` della stessa cattura | **13** |
| i due che non compaiono | `7845062` (tipo 1) e `2460985` (tipo 2) |

Le diciotto righe, per intero:

```
out 3 3013     out 1 8309204   out 1 8309202   out 1 8314067
out 3 3013     out 1 7845062   out 2 2328704   out 2 2328703
out 3 3012     out 3 3013      out 1 7716392   out 2 2460985
out 1 8313531  out 3 3012      out 1 7954171   out 2 2333761
out 2 2333760  out 3 3003
```

La forma è `out <tipo entità> <id entità>` — **gli stessi due campi con cui `in`
comincia**, nello stesso ordine. Tredici id su quindici li avevamo già visti
arrivare o muoversi: `out` nomina entità che conoscevamo.

## Perché conta, misurato anche questo

`GameplayProvider` tiene le entità in `_entities` e le lascia cadere per
**scadenza a tempo**: `Expire` (riga 806) rimuove ciò che non è stato nominato
entro `MaxEntityRetention`, che vale **60 secondi**
(`DefaultMaxEntityRetention`, riga 471). Finché quel minuto non passa, un'entità
che se n'è andata resta un bersaglio candidato.

`out` sostituisce quel minuto di attesa con **un'affermazione del server**. Non è
la differenza fra ricordare e dimenticare: è la differenza fra dedurre e sapere.

---

## Cosa fare

### Parte 1 — un genere di evento nuovo, e non `EntityDeath`

`GameEventKind` (in `GameTrafficObserver.cs`) ha oggi cinque valori:
`EntitySighting=0, CombatHit=1, EntityDeath=2, ChatMessage=3, Unknown=4`.

**Aggiungi un valore nuovo** per l'entità che esce dalla vista — il nome lo
scegli tu, purché dica «se n'è andata» e non «è morta». Assegnagli il numero
successivo libero e **non rinumerare i valori esistenti**: `Unknown` resta dov'è,
perché quel byte compare in dati serializzati.

**Il punto del task è qui**: `out` **non deve mai** produrre un `EntityDeath`.
Nelle stesse diciotto occorrenze la cattura ha **due** `die` in tutto; se `out`
diventasse morte, quella cattura riporterebbe **venti** uccisioni invece di due.
Un componente che conti le uccisioni — o un obiettivo di quest `Kill` — leggerebbe
diciotto vittorie mai avvenute.

### Parte 2 — decodificare

Aggiungi `"out" => …` al dispatch di `NosTaleWorldProtocolDecoder.Decode`
(righe 115-130), con lo **stesso schema di `DecodeDeath`**:

- rifiuta il pacchetto se ha meno campi del necessario o se l'id non è un numero
  — `return DecodedObservations.Empty`, mai un evento a metà;
- **leggi il vnum da `_entities` prima di rimuovere l'entrata**, per la stessa
  ragione scritta nel commento di `DecodeDeath`: dopo la rimozione la specie non
  si recupera più, e chi legge l'evento ha diritto di sapere *che cosa* se n'è
  andato;
- rimuovi l'entrata da `_entities`;
- emetti **un solo** evento, con l'opcode `"out"` come testo, la sorgente e
  l'istante del pacchetto, come fa `DecodeDeath`.

**Il campo 1 è il tipo entità.** Il decoder ha già `KindOf` e `IsReadableEntity`
per i tipi 1/2/3: usali invece di riscriverli. Un tipo che non riconosci **non è
un motivo per inventare un default** — se decidi di scartare quei pacchetti,
scrivi nel codice perché.

### Parte 3 — il World Model se ne accorge

`GameplayProvider` (righe 697-701) rimuove oggi da `_entities` solo su
`EntityDeath`. Aggiungi il genere nuovo **accanto** a quello, non al posto suo.
Non toccare `Expire`: la scadenza a tempo resta, e serve per tutto ciò che il
server non annuncia.

### Parte 4 — il buco dello strumento

`--wire-inspect <file>` stampa il censimento dei campi; `--opcode <op>` stampa i
pacchetti di **un** opcode. Non esiste un modo di vedere i pacchetti **in ordine
cronologico attraverso opcode diversi**, e per questo la domanda «un'entità
uscita riappare dopo?» non è misurabile oggi con gli strumenti del repository.

È lo stesso genere di buco che Q-131 ha chiuso aggiungendo `--fields`, e per la
stessa ragione: la misura era stata fatta in `awk` per il terzo task di fila.

**Aggiungi un'opzione** a `WireInspectCommand` che stampi i pacchetti nell'ordine
in cui la cattura li contiene, con il loro numero progressivo e l'istante, e che
si possa restringere a un insieme di opcode (perché una cattura da 20 876 `mv`
altrimenti è illeggibile). Nome e forma esatta li scegli tu; documentali
nell'`Usage` che il comando già stampa.

**L'opzione sconosciuta va rifiutata**, come `--summary` viene rifiutato oggi
(`[REFUSED] unknown_option:--summary`): quel comportamento esiste da Q-131, non
regredirlo.

### Parte 5 — la misura che il task deve produrre

Con lo strumento della Parte 4, misura su `data/messaggi.noscap` e **riporta il
numero nel report**:

> Per ognuno dei 18 `out`, l'id nominato ricompare in un `in`, `mv` o `st`
> **successivo** nella stessa cattura? Quanti sì, quanti no.

**Non decidere il comportamento in base alla risposta**: rimuovere è corretto in
entrambi i casi, perché un'entità che ricompare viene reinserita dal primo
pacchetto che la nomina. Serve a sapere **che cosa `out` significa davvero** —
«sparita per sempre» o «uscita dalla vista» — e a scriverlo nel codice invece di
supporlo. Se la risposta è «alcune ricompaiono», dillo: è un fatto, non un
fallimento.

---

## Test (file nuovi in `tests/NosAi.Runtime.Tests/`)

1. `out 3 3013` produce **un** evento del genere nuovo e **nessun**
   `EntityDeath`. Il test che vale il task.
2. Un'entità entrata con `in` e poi uscita con `out` **non compare più** fra gli
   avvistamenti pubblicati dai pacchetti successivi che non la nominano.
3. Il vnum sopravvive all'uscita: entità entrata con `in 3 45 3205 …`, poi
   `out 3 3205`, e l'evento porta ancora `45`.
4. Un `out` malformato (campi mancanti, id non numerico) non produce niente e non
   lancia.
5. Tutti e tre i tipi entità (1, 2, 3) si decodificano — sono i tre che le
   diciotto righe contengono.
6. Con `[RecordedCaptureFact("messaggi.noscap")]`: la cattura reale produce
   **esattamente 18** eventi del genere nuovo e **2** `EntityDeath`. È il numero
   che separa questo task dal difetto che evita.
7. `GameplayProvider`: un'entità rimossa per uscita non è più fra le
   `SelectableEntity`, **prima** che i 60 secondi di `MaxEntityRetention` siano
   passati.
8. Per l'opzione della Parte 4: l'ordine è quello della cattura, il filtro per
   opcode funziona, e un'opzione sconosciuta è rifiutata.

## Fuori scope

- **Nessuna modifica a `Expire` o a `MaxEntityRetention`.**
- **Nessun consumatore nuovo**: non collegare l'evento alla pianificazione, agli
  obiettivi di quest o al Safety Gate. Promuovere «è uscito» a decisione è
  un'altra scelta, e non è questa.
- **Nessun aggiornamento ai documenti** — REGOLA #2. `PROTOCOLLO_NOSTALE.md`,
  `EXECUTION_QUEUE.md` e `STATO_IMPLEMENTAZIONE.md` li aggiorna Claude con i
  numeri del tuo report.
- **Non toccare `SyntheticProtocolDecoder`**: è il decoder finto dei test di
  infrastruttura, e non ha niente a che vedere con il protocollo reale.

## Limite da dichiarare nel report

`out` compare in **una sola** delle sei catture. Diciotto pacchetti sono pochi, e
il report deve dirlo: la forma è confermata (18 su 18, due campi, tre tipi), il
*significato* poggia sui tredici id su quindici che avevamo già visti altrove.

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests` 0 falliti, totale riportato.
- Il numero della Parte 5, misurato e riportato.
- Il conteggio del test 6 riprodotto nel report: 18 uscite, 2 morti.
- Livello: **Integrated** (`Verified` vorrebbe un operatore che vede un mostro
  uscire dallo schermo mentre la cattura gira).

---

## Nota su chi tocca cosa

Claude **non** tocca `NosTaleWorldProtocolDecoder.cs`, `GameTrafficObserver.cs`,
`GameplayProvider.cs` né `WireInspectCommand.cs` finché questo task è aperto.
Lavora in parallelo su documentazione, pannello di controllo e test di altre
aree. Se ti serve un file fuori da questa lista, **fermati e riferisci** invece
di prenderlo.

---

# Addendum del 2026-09-08 — la skill si legge, e il catalogo lo conferma

**Stesso task, stesso file, secondo pezzo.** Sta qui e non in un comando a parte
perché tocca lo stesso `NosTaleWorldProtocolDecoder.cs`: due agenti su quel file
non ci vanno.

## Il fatto

`docs/PROTOCOLLO_NOSTALE.md` dà il campo 5 di `su` come **skill vnum**, con
confidenza **probable** — mai incrociato con niente. Ora è incrociato, e regge.

Misurato su **282 pacchetti `su`** di tutte e otto le catture (`nostale_01` non
ne ha), e su **272 pacchetti `ct`**:

### Il campo 5 di `su`, separato per tipo di attaccante

| tipo attaccante | pacchetti | valori distinti al campo 5 |
|---|---:|---|
| **1** (giocatore) | 118 | **7**: `220`×63, `200`×34, `226`×13, `222`×3, `224`×2, `228`×2, `223`×1 |
| **2** (astante) | 5 | **1**: `0`×5 |
| **3** (mostro) | 159 | **1**: `0`×159, **zero eccezioni** |

### Il riscontro sul catalogo del client

Tutti e sette i valori del giocatore esistono nella tabella `skill` del catalogo
importato dai file del client (`entity` con `kind='skill'`, 1958 righe):

| campo 5 | nome IT | posizione nella classe | livello |
|---:|---|---:|---:|
| 200 | Ritmo | 0 | 0 |
| 220 | Colpo di base | 0 | 0 |
| 222 | Colpo furioso | 2 | 1 |
| 223 | Colpo preciso | 3 | 4 |
| 224 | Energia della spada | 4 | 5 |
| 226 | Terremoto | 6 | 1 |
| 228 | Attacco Doppio | 8 | 3 |

Il valore `0` dei mostri e degli astanti **non è** nella tabella `skill`
(i vnum vanno da 1 a 2012): coerente con «attacco base, nessuna abilità».

### Tre strutture che il filo non poteva sapere

1. **I due giocatori hanno insiemi disgiunti.** L'id `3443217` usa
   {220, 222, 223, 224, 226, 228}; l'id `3548294` usa {200}. Due personaggi, due
   repertori separati.
2. **200 e 220 hanno entrambi posizione 0** nella rispettiva classe — sono
   l'attacco base di due classi diverse, ed è esattamente la coppia che i due
   giocatori distinti usano.
3. **`ct` e `su` non tornano per uno solo dei sette, ed è quello giusto.** Per
   attaccante di tipo 1, ogni valore compare in `ct` tante volte quante in `su`
   — rapporto 1,00 per 200, 220, 222, 223, 224, 228 — tranne **226**, dove il
   rapporto è **6,50** (2 `ct` contro 13 `su`; in `nostale_combat` un `ct`
   seguito da undici `su`). Nel catalogo `226` è **l'unica dei sette con raggio
   d'area** (`TARGET` = `1 1 1 3 0`, quarto slot diverso da zero): un lancio,
   molti colpi. L'unica anomalia si spiega da sé.

### La contro-prova, e il suo limite

**La sola presenza in `skill` è debole, e va detto.** Nell'intervallo 0-228 la
tabella `monster` risponde a **229 vnum su 229** e `card` pure: qualunque numero
in quell'intervallo «esiste» come mostro. Sette su sette in `skill` non è di per
sé sorprendente.

Quello che rompe la parità è misurato: i vnum di mostro **davvero presenti** in
quelle otto catture (59 pacchetti `in` di tipo 3) sono
`{2, 9, 20, 24, 36, 39, 40, 45, 48, 49, 96, 333}` — **intersezione vuota** con
`{0, 200, 220, 222, 223, 224, 226, 228}`. Nessuno dei valori del campo 5 è un
mostro visto in quelle sessioni, e `item` non ne contiene nessuno.

## `ct` — il campo è il **7**, non il 6

272 pacchetti, **sempre 7 campi**:

| indice | distinti | valori |
|---:|---:|---|
| 1 | 3 | `3`×163, `1`×107, `2`×2 — **tipo del lanciatore** |
| 2 | 37 | id del lanciatore |
| 3 | 3 | `1`×164, `3`×107, `2`×1 — **tipo del bersaglio** |
| 4 | 37 | id del bersaglio |
| 5 | 2 | `-1`×265, `14`×7 — **non decodificato** |
| 6 | 4 | `-1`×267, `518`×2, `521`×2, `516`×1 — **non decodificato** |
| **7** | **8** | `0`×165, `220`×63, `200`×34, `222`×3, `224`×2, `226`×2, `228`×2, `223`×1 |

L'insieme del campo 7 è **identico** a quello del campo 5 di `su`, con la stessa
divisione per tipo: per lanciatore di tipo 1 assume i sette valori giocatore, per
i tipi 2 e 3 vale sempre `0` (165 pacchetti).

## Cosa fare

### Parte 6 — leggere il vnum della skill

`DecodeHit` (il gestore di `su`) legge oggi attaccante e bersaglio e i due campi
finali della vita, e **salta il campo 5**. Portalo fuori:

- **il vnum va sull'evento del colpo**, dove sta già il resto di ciò che il colpo
  dice. Il contratto lo scegli tu, ma additivamente: nessuna firma esistente
  cambia significato;
- **`0` non è una skill.** È l'attacco base, ed è ciò che i mostri fanno sempre
  (159 su 159). Un `0` pubblicato come «vnum 0» diventerebbe una skill inventata
  al primo che lo cerca nel catalogo. Deve restare distinguibile: assente, non
  zero. È la stessa regola che `fields[11]` impone alla vita del bersaglio, per
  la stessa ragione;
- **non risolvere il nome qui.** Il decoder non conosce il catalogo e non deve:
  pubblica il numero, il nome lo dà chi ha il catalogo aperto.

### Parte 7 — `DecodeCast` legge il campo 7

`DecodeCast` (il gestore di `ct`) pubblica oggi la selezione del bersaglio.
Aggiungi il campo 7 con **le stesse due regole**: additivo, e `0` assente invece
che zero.

**Non collegare `ct` e `su` fra loro.** Il rapporto 6,50 di `226` dice che un
lancio può produrre molti colpi, e appaiarli uno a uno sarebbe sbagliato. Sono
due letture indipendenti dello stesso numero.

### Parte 8 — test

Oltre a quelli già chiesti sopra:

9. `su 1 3443217 3 313816 226 …` porta il vnum `226`; `su 3 313816 1 3443217 0 …`
   **non porta nessun vnum** — e in particolare non ne porta uno che valga zero.
10. Lo stesso per `ct` sul campo 7.
11. Con `[RecordedCaptureTheory]` sulle catture reali: i valori distinti che il
    decoder pubblica per l'attaccante di tipo 1 sono **esattamente**
    `{200, 220, 222, 223, 224, 226, 228}`, e per il tipo 3 **nessuno**. Il numero
    di pacchetti per cattura è nella tabella qui sopra — asseriscilo, perché un
    test che ne vede zero passerebbe a vuoto.
12. **Il test che vale il riscontro**: con `[NosTaleClientFact]`, ognuno dei
    sette valori esiste nella tabella `skill` del catalogo e `0` non c'è. Se il
    catalogo non è sul disco, il test **salta visibilmente** — mai `if (…) return`.

## Ancora fuori scope

- **Nessun consumatore**: non collegare il vnum alla selezione della skill, alla
  predizione o al Guard. Sapere quale abilità è stata usata e *decidere* di
  usarla sono due cose diverse.
- **`ct[5]` e `ct[6]` restano `Unknown`.** `ct[6]` vale `516`/`518`/`521` in
  5 pacchetti su 272, sempre insieme a `ct[5] = 14`, e quei tre vnum esistono sia
  in `card` sia in `skill`: non incrociato, quindi non deciso. Non riempirli per
  somiglianza con il campo 7 — è esattamente ciò che la regola sulle fonti
  esterne vieta.

## Definition of done, aggiornata

Alla lista di sopra si aggiunge:

- I sette valori e i loro sette nomi riprodotti dal tuo test contro il catalogo
  reale, incollati nel report.
- Il conteggio per cattura del test 11.
