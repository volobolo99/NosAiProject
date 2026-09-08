# AP-05 / A2+A4 — DeepSeek — il catalogo delle abilità incontra il filo

**Task grande, in sei misure e due costruzioni.** Si prende **dopo Q-140**, non
in parallelo: usa lo strumento cronologico che Q-140 costruisce (Parte 4) e
tocca `Program.cs`, che Q-140 tocca pure.

## Cosa possiedi

- `src/NosAi.Runtime/GameData/SkillReferenceDecoder.cs`
- `src/NosAi.Runtime/GameData/SkillCatalogue.cs` — **nuovo**
- `src/NosAi.Runtime/Observability/SkillReportCommand.cs` — **nuovo**
- `src/NosAi.Runtime/Program.cs` — **solo** la registrazione dell'opzione nuova
- file di test nuovi in `tests/NosAi.Runtime.Tests/`

Puoi **aggiungere** metodi di sola lettura a `GameReferenceDatabase.cs` se ti
servono; Claude non lo tocca finché questo task è aperto. Non toccare
`NosTaleWorldProtocolDecoder.cs`, `GameTrafficObserver.cs`, `GameplayProvider.cs`,
`WireInspectCommand.cs`, `CombatPlanner.cs`.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**.

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci.

---

## Il fatto

`SkillReferenceDecoder` esiste dal 2026-09-06, decodifica **trenta campi** per
abilità, ed è **codice morto**: nessun file sotto `src/` chiama `Decode`, solo i
test. Il suo tracciato viene da una fonte esterna di comunità
(<https://nt-research.github.io/>, «NOS files / NSgtdData / Skill.dat»), e le sue
stesse *remarks* dichiarano il limite:

> «Its tag layout is cross-checked for eight fields (skill vnum 201); every other
> tag position and every coded value's in-game meaning remains provisional.»

**Fino al 2026-09-08 non c'era modo di fare di meglio.** Ora c'è: il campo 5 di
`su` è il vnum dell'abilità usata, e il campo 7 di `ct` porta lo stesso insieme
(Q-140, addendum). Per la prima volta si può mettere accanto a ogni riga del
catalogo un'osservazione del filo che la riguarda.

`CLAUDE.md` § «External reference data» dice esattamente cosa fare in questo caso:
*«An external source is a lead, not a ground truth: cross-check at least one
decoded value against a real, observed one before trusting it on any path that is
not purely diagnostic.»* Questo task è quel cross-check.

## Le sette abilità che le registrazioni contengono

Sono l'insieme su cui tutte le misure si fanno. Il catalogo le dà così:

| vnum | nome IT | `TYPE[1]` | `COST[0]` | `DATA[8]` | `DATA[5]` | `TARGET` |
|---:|---|---:|---:|---:|---:|---|
| 200 | Ritmo | 0 | **0** | **0** | 6 | `0 0 1 0 0` |
| 220 | Colpo di base | 0 | **0** | **0** | 7 | `0 0 1 0 0` |
| 222 | Colpo furioso | 2 | 5 | 15 | 50 | `0 0 1 0 0` |
| 223 | Colpo preciso | 3 | 7 | 25 | 100 | `0 0 1 0 0` |
| 224 | Energia della spada | 4 | 8 | 30 | 250 | `0 1 4 1 0` |
| 226 | Terremoto | 6 | 15 | 28 | 250 | `1 1 1 3 0` |
| 228 | Attacco Doppio | 8 | 7 | 22 | 100 | `0 0 1 0 0` |

Il decoder mappa oggi `CpCost = COST[0]` e `MpCost = DATA[8]`.

---

# Parte A — le sei misure

Ognuna produce **numeri nel report**. Nessuna di esse, da sola, autorizza a
cambiare il codice: la Parte B dice cosa si può cambiare e cosa no.

Usa lo strumento cronologico di Q-140. Se una misura non è fattibile con i dati
che ci sono, **dillo e passa oltre**: un «non misurabile» documentato vale più di
un numero costruito.

## A1 — quale dei due campi è il costo MP

**È la misura più importante del task.** `CpCost` e `MpCost` valgono **entrambi
zero** per i due attacchi base, quindi le settanta osservazioni già fatte non li
distinguono. Si distinguono sulle altre cinque.

Il filo dà un indizio e **non una prova**: in `nostale_combat` la raffica di
`226` cade fra due letture di `stat` che distano **esattamente 15** punti di MP,
cioè `COST[0]` e non `DATA[8]` (28). Ma la finestra è sporca — fra le due letture
la rigenerazione risale a scatti di **+24** — quindi la coincidenza non conclude.

**Misura, su tutte le catture**: per ogni cambiamento del valore di MP in `stat`,
la finestra di pacchetti che lo precede, ogni `ct`/`su` del giocatore dentro
quella finestra con il vnum dell'abilità, e la durata della finestra. Riporta la
tabella intera. Poi di' quale delle due ipotesi le finestre pulite (se ce ne
sono) sostengono, **e quante finestre pulite hai trovato**.

Se nessuna finestra è pulita, la risposta corretta è **«non decidibile con queste
registrazioni»**, e il task lo riporta. `docs/TEST_RIMANDATI.md` § T-17 descrive
la registrazione dedicata che chiuderebbe la domanda; non è compito tuo farla.

## A2 — `sr` porta la posizione dell'abilità

`sr <n>` è già decodificato (`DecodeSkillReady`) e porta un solo numero. I valori
osservati sono **0, 2 e 6**, e sono esattamente i `TYPE[1]` delle abilità che
quelle registrazioni usano: `220`→0, `222`→2, `226`→6.

**Misura**: per ogni `sr <n>` di ogni cattura, esiste un'abilità usata nella
stessa cattura con `TYPE[1] == n`? Quanti sì, quanti no. Se esce un `n` che
nessuna abilità osservata giustifica, **riportalo**: significa che il giocatore
possiede abilità che non ha usato, il che è normale e va detto, non nascosto.

Questa misura promuove `TYPE[1]` da «provvisorio» a confermato — o lo smentisce.

## A3 — il tempo di ricarica

Se A2 regge, il tempo fra l'uso di un'abilità e l'`sr` della sua posizione **è**
la ricarica.

**Misura**: per ogni `ct`/`su` del giocatore con abilità X, l'istante del primo
`sr` successivo con `n == TYPE[1]` di X, e la differenza in millisecondi (le
catture portano l'istante di ogni pacchetto). Confrontala con `DATA[5]`
(222→50, 223→100, 224→250, 226→250, 228→100).

**Determina l'unità**, non darla per scontata: se 250 corrisponde a ~25 secondi
l'unità è il decimo di secondo; se a ~250 ms sono millisecondi. **La regola va
misurata su almeno due abilità con `DATA[5]` diverso** — una sola non distingue
una regola da una coincidenza.

## A4 — la portata

`TARGET[2]`/`TARGET[3]` e `DATA[11]`/`DATA[12]` sono candidati per portata e
raggio d'area. Le posizioni delle entità arrivano da `in` e `mv`.

**Misura**: al momento di ogni `ct`/`su` del giocatore, la distanza fra la
posizione nota dell'attaccante e quella del bersaglio. Riporta minimo, mediana e
massimo **per abilità**. Un'abilità con portata dichiarata 1 che colpisce
regolarmente a otto caselle smentisce il campo.

**Attenzione a una trappola**: la posizione nota può essere vecchia. Riporta anche
quanti pacchetti separano l'ultimo `mv` del bersaglio dal colpo, e **scarta le
coppie in cui la posizione è stantia** — dichiarando quante ne hai scartate.

## A5 — il raggio d'area

`226` ha `TARGET[3] = 3` (le altre 0 o 1) ed è l'unica che produce **un lancio e
molti colpi**: rapporto `ct`/`su` misurato **6,50** contro **1,00** delle altre
sei, e in `nostale_combat` un `ct` seguito da undici `su`.

**Misura**: per ogni `ct` del giocatore, quanti `su` con lo stesso vnum lo
seguono prima del `ct` successivo. Riporta la distribuzione per abilità. Se
`TARGET[3]` predice quel numero — o almeno lo separa in «uno solo» e «più di
uno» — il campo è confermato.

## A6 — la classe

`TYPE[2]` vale 0 per `200` e 1 per tutte le altre sei. Le due registrazioni hanno
**due giocatori distinti con repertori disgiunti**: `3548294` usa solo `200`,
`3443217` usa le altre sei.

**Misura**: per ogni giocatore osservato, l'insieme dei `TYPE[2]` delle abilità
che usa. Se ogni giocatore ne ha uno solo, `TYPE[2]` è la classe. Due giocatori
sono pochi: **dillo**, invece di dichiarare confermato ciò che poggia su due
casi.

---

# Parte B — le due costruzioni

## B1 — `SkillCatalogue`

Un tipo nuovo che apre il catalogo e restituisce la `SkillReference` di un vnum,
con **una distinzione esplicita fra i campi confermati dalle misure della Parte A
e quelli che restano provvisori**.

Come farla è una tua decisione di progetto, ma tre vincoli sono obbligatori:

1. **Un campo provvisorio non si legge come se fosse confermato.** Chi chiama deve
   poter distinguere le due cose senza leggere la documentazione. Il repository ha
   già `ClassifiedValue<T>` con `Unknown(motivo)` per esattamente questo problema:
   guardalo prima di inventare un meccanismo nuovo.
2. **Il catalogo assente non è un errore e non è un catalogo vuoto.** Segui
   `GameReferenceLocator`, che già distingue le due cose.
3. **Nessuna scrittura.** Questo tipo legge.

## B2 — `--skill-report`

Il comando che rende leggibile a un umano tutto quello che la Parte A ha
misurato: per un vnum, o per tutte le abilità osservate in una cattura, stampa i
campi del catalogo **e** l'osservazione corrispondente accanto, marcando ogni
riga come confermata o provvisoria.

È l'artefatto che serve all'operatore per fidarsi o no: senza, le misure vivono
solo in un report che nessuno rilegge.

Modello: `OutcomeReportCommand` e `--learning-report` (Q-132), stessa forma.
**L'opzione sconosciuta si rifiuta**, come già fanno.

## Cosa NON fare

- **Nessun consumatore nella pianificazione.** Non toccare `CombatPlanner`, non
  cablare il costo o la ricarica nella scelta dell'abilità, non collegare niente
  al Guard. Sapere quanto costa un'abilità e *decidere* di usarla sono due cose
  diverse, e la seconda è un'altra decisione che non è stata presa.
- **Non rinominare `MpCost`/`CpCost` sulla base di un indizio.** Se A1 non
  conclude, i due campi restano come sono e il report dice perché. Rinominarli su
  una coincidenza sarebbe peggio di lasciarli: darebbe a un nome sbagliato
  l'aspetto di una verifica.
- **Non riempire un campo per somiglianza con uno adiacente confermato.** È
  esattamente ciò che `CLAUDE.md` vieta.
- **Nessun aggiornamento ai documenti** — REGOLA #2.

---

## Test

In `tests/NosAi.Runtime.Tests/`, file nuovi:

1. `SkillCatalogue` restituisce una `SkillReference` per un vnum reale e **null
   con motivo** per uno inesistente, e le due risposte non si somigliano.
2. Un campo provvisorio è distinguibile da uno confermato **dal tipo di ritorno**,
   non da un commento.
3. Catalogo assente: motivo dichiarato, nessuna eccezione, nessun catalogo vuoto
   spacciato per «nessuna abilità».
4. Con `[NosTaleClientFact]`: le sette abilità della tabella qui sopra hanno i
   valori della tabella qui sopra. **È il test che sorveglia il catalogo reale**:
   se un aggiornamento del client cambiasse quei numeri, si vedrebbe qui.
5. Con `[RecordedCaptureTheory]`, uno per ogni misura della Parte A che ha
   concluso: il numero misurato, asserito. **Il conteggio va asserito**, perché un
   test che itera zero elementi passa a vuoto.
6. `--skill-report` su un vnum reale stampa i campi; su un vnum inesistente
   stampa un motivo; con un'opzione sconosciuta stampa `[REFUSED]`.
7. Per ogni misura della Parte A che **non** ha concluso, un test che fissa lo
   stato attuale — non l'ipotesi. Esempio: se A1 non decide, il test asserisce che
   `MpCost` e `CpCost` restano due campi distinti e che nessuno dei due è
   dichiarato «il costo MP».

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests` 0 falliti, totale riportato.
- **Le sei misure della Parte A, coi numeri**, nel report. Comprese quelle che non
  concludono, con scritto perché.
- Per A1: quante finestre pulite hai trovato e cosa dicono. Se zero, «zero».
- Per A3: l'unità di misura della ricarica e le due abilità su cui l'hai misurata.
- L'elenco dei campi che passano da provvisori a confermati, e di quelli che
  restano provvisori.
- Livello: **Integrated**.

## Il criterio con cui questo task sarà giudicato

Non da quanti campi hai confermato. **Da quanti hai confermato con una misura che
regge, e da quanto onestamente hai riportato quelli che non reggono.** Un task che
conclude «cinque campi confermati, uno indeciso, ecco i numeri» vale più di uno
che ne dichiara sette senza dire su cosa.
