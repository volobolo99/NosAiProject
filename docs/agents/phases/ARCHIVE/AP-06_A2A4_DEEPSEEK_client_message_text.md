# AP-06 / A2+A4 — DeepSeek — il testo dei messaggi di sistema è in un archivio che non apriamo

**Nessun task in coda: prendibile subito.** Tocca `src/NosAi.Runtime/GameData/`
e file di test nuovi. Non tocca `Program.cs`, né il decoder, né la calibrazione.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/PROTOCOLLO_NOSTALE.md` § `sayi` — **la metà già stabilita**, con la
   cattura che la prova.
3. `docs/TEST_RIMANDATI.md` § T-16 — dove il testo **non** è, e come è stato
   escluso.
4. `src/NosAi.Runtime/GameData/ReferenceImporter.cs` — **il modello da seguire**,
   in particolare `LanguageTables` e `TextOnlyTables` e le loro *remarks*.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**
(non c'è hook).

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci.

---

## Il fatto

Il filo porta `sayi 1 3548294 12 975 2 8 1 0 0`. Del pacchetto sappiamo, per
misura su due catture indipendenti:

- il campo 4 è **l'id del messaggio**;
- il campo 6 è **il suo argomento**, e vale il vnum dell'oggetto — `8` qui,
  `2006` in `nostale_combat`, ognuno corroborato da `drop`, `get` e `ivn`, ognuno
  risolto nel catalogo («Fionda in legno», e l'altro).

Quello che manca è **il testo del messaggio 975**. E si sa dove non è: cercato
`zts975?e` — con il carattere marcatore, la stessa regola che risolve i nomi dei
mostri — in **tutte e dodici** le tabelle di `NSlangData_IT.NOS`, per 975, 654,
697 e 2110, escono solo voci scollegate («Kamil», «Tinta per capelli lilla»).

L'importatore apre due archivi: `NSgtdData.NOS` per le entità e
`NSlangData_<LANG>.NOS` per il testo. Nella stessa cartella ce n'è un terzo che
nessuno ha mai aperto:

```
NScliData_IT.NOS      313 790 byte      (e le sue otto sorelle di lingua)
NSlangData_IT.NOS   3 767 074 byte      (questo lo leggiamo)
```

`cli` sta per client. È il candidato, e nessuno l'ha guardato.

---

## Cosa fare

### Parte 1 — aprire l'archivio e dire cosa c'è

`NosArchive.Open` legge già questo formato: `NSlangData` e `NSgtdData` passano da
lì. Elenca le voci di `NScliData_IT.NOS` e **riporta l'elenco nel report**: nomi,
quante voci, e per ognuna quante righe decodifica `NosDataTable.Decode`.

Se il formato interno non è quello che `NosDataTable` sa leggere, **fermati e
riferisci cosa hai visto** — i primi byte, la struttura apparente. Un formato
diverso è una scoperta, non un fallimento; inventare un parser su un formato non
capito è il contrario.

### Parte 2 — il riscontro, che è il task

Non basta trovare una tabella di testo: bisogna dimostrare che **è quella**. Il
riscontro è già disponibile e non richiede l'operatore:

- il messaggio **975** appare in `data/nostale_combat.noscap` e in
  `data/messaggi.noscap`, in entrambe con un vnum di oggetto come argomento, in
  entrambe subito dopo un `get`. Il testo che cerchiamo, in italiano, dice che
  un oggetto è stato raccolto;
- il messaggio **654** (`data/messaggi.noscap`, argomento 13 = «Uniforme da
  allenamento») è un secondo caso dello stesso genere;
- il messaggio **697** ha argomento 50 con il campo 5 a `4` invece che `2`, e il
  campo 7 a `0` invece che `1`: se la tabella è quella giusta, il suo testo deve
  spiegare perché quell'argomento non è un oggetto.

**Se la riga trovata per 975 non parla di raccogliere un oggetto, la tabella non
è quella**: dillo e continua a cercare, o fermati riferendo dove hai guardato.
Una tabella che risolve *qualche* id e ne sbaglia uno verificabile è peggio di
nessuna tabella, perché sembra funzionare.

Prova anche la corrispondenza **diretta** (`zts<id>?e`) e quella **±1**: per i
mostri l'indice è vnum+1, e non c'è motivo di dare per scontato che qui sia
uguale. Qualunque regola scegli, dev'essere **misurata su almeno due id
diversi**, non su uno.

### Parte 3 — importare, come le altre

Se e solo se la Parte 2 riesce: importa la tabella con la stessa forma di
`TextOnlyTables`, e con lo stesso motivo scritto nelle *remarks* — perché sta
separata, cosa risolve e cosa **non** risolve.

Se la Parte 2 non riesce, **la Parte 3 non si fa**. Importare del testo che non
sappiamo indicizzare aggiunge righe al catalogo e nessuna risposta.

---

## Test (file nuovi in `tests/NosAi.Runtime.Tests/`)

Con `[NosTaleClientFact]` (esiste già, 13 test lo usano: salta **visibilmente**
dove il client non è installato — **mai** `if (…) return`):

1. `NScliData_IT.NOS` si apre e ha più voci di una soglia bassa che **misuri tu**
   e scrivi nel commento.
2. La tabella che hai identificato si decodifica e ha più righe di una soglia
   che misuri tu.
3. **Il test che vale il task**: l'id 975 risolve a un testo che nomina la
   raccolta di un oggetto. Asserisci una sottostringa che **hai letto davvero**
   nel file, non una che ti aspetti.
4. Lo stesso per un secondo id (654 o 697), perché una regola misurata su un
   caso solo non è una regola.
5. Un id che non esiste non risolve, e non restituisce la riga vicina.

## Fuori scope

- **Nessun consumatore.** Non collegare il testo al World Model, al
  `DialogWindowStateComposer` o alla pianificazione: promuovere un messaggio a
  fatto usabile è un'altra decisione.
- **Nessuna modifica al decoder del filo.**
- **Nessun aggiornamento ai documenti** — REGOLA #2. `PROTOCOLLO_NOSTALE.md` e
  `TEST_RIMANDATI.md` li aggiorna Claude con i numeri del tuo report.

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests` 0 falliti, totale riportato.
- **L'elenco delle voci di `NScliData_IT.NOS`**, incollato nel report.
- Il testo esatto che 975 e il secondo id risolvono, e la regola di indicizzazione
  che hai misurato (diretta, ±1, altro) con i due casi su cui l'hai misurata.
- Se non hai trovato la tabella: dove hai guardato e cosa hai visto. È un
  risultato, e chiude una possibilità.
- Livello: **Integrated** (`Verified` vorrebbe un operatore che legge lo stesso
  messaggio sullo schermo mentre la cattura gira — il pannello lo raccoglie ora,
  in Rete → «Registra il filo, e annota cosa hai visto»).

---

# Addendum del 2026-09-08 — la colonna chiave si decodifica

**Il tuo risultato negativo è confermato, due volte.** L'id `sayi` non indicizza
`conststring.dat`: né come chiave diretta (975, 654, 2110 non esistono; 697 dà
«Lacrima», che non c'entra), né con uno scarto costante — fissato lo scarto sulla
coppia giusta (975 → 10666, «Hai raccolto [%s]»), gli altri tre cadono su «Sono
passate %d ore», «Stessa età» e «Questo giocatore è già sposato». Hai fatto bene
a non rivendicare un lookup.

**Ma la chiave si legge**, e il motivo per cui `NosDataTable` non ce la fa è
preciso.

## La causa

`NosDataTable.Parse` fa così:

```csharp
byte declared = body[at + 1];
int start = at + 2;          // <-- il byte "lunghezza" viene saltato
```

Per le chiavi sotto 100 quel byte **è** una lunghezza e tutto torna: la riga 11
esce `11\x0bNome`. Per le chiavi da 100 in su quel byte **è il marcatore di
numero impacchettato** — `0x83` per tre cifre, `0x84` per quattro — e saltarlo
lascia il lettore dentro il payload, che finisce interpretato come testo XOR. È
per questo che la chiave esce come `Gî?` invece che come un numero.

La distinzione è nel nibble alto, la stessa che `ReadLine` già usa:

```
riga  11:  02 01 38 7d 5a 50 58        02 = lunghezza,  chiave "11"
riga  99:  83 54 40 08 38 70 52 ...    83 = marcatore,  chiave "100"
riga 3099: 84 75 44 4a 38 7a 5f ...    84 = marcatore,  chiave "3099"
```

`0x38 ^ 0x33 = 0x0B`, il tabulatore: tutto ciò che sta prima è la chiave.

## Cosa ne esce

Leggendo il byte come marcatore quando `(b & 0xF0) == 0x80` e come lunghezza
altrimenti, la tabella dà **7565 chiavi numeriche distinte** su 7869 righe, in
ordine crescente dall'1, e i testi giusti accanto:

```
   3098  -> "è ottenuto."
   3099  -> "è raccolto."
  10665  -> "[%s] raccolta:<NEW_TYPE><0>"
  10666  -> "Hai raccolto [%s]:<NEW_TYPE><0>"
```

Resta un byte in coda alla chiave che il mio lettore di prova non consuma
(`100;`, `101>`): un dettaglio a una misura di distanza, non un ostacolo.

## Cosa ti chiedo adesso

1. **Correggi `NosDataTable.Parse`** perché distingua il marcatore dalla
   lunghezza. È un difetto del lettore, non di questa tabella: ogni tabella con
   chiavi da 100 in su lo subisce.
2. **Importa `conststring` fra le `TextOnlyTables`**, con le chiavi numeriche.
   Ora ha senso: il testo è indicizzabile anche se l'id del filo non lo indicizza.
3. **Non inventare il collegamento con `sayi`.** Resta aperto, ed è giusto che
   resti: si chiuderà con una coppia osservata (testo a schermo ↔ riga della
   cattura), che il pannello ora raccoglie in Rete → «Registra il filo, e annota
   cosa hai visto».

Il test che vale: la chiave 10666 dà «Hai raccolto [%s]» e la 3099 «è raccolto.»,
lette dal file e non attese.
