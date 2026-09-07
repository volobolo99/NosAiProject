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
