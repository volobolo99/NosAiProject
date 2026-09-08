# AP-09 / A2+A4 — DeepSeek — `--decide-replay` non arriva mai al pianificatore, e non lo dice

**Indipendente dai tre task in coda.** Non tocca nessuno dei loro file, e non
tocca `Program.cs` (il flag nuovo è un sotto-flag di un comando esistente).
Prendibile in qualunque ordine, anche in parallelo — se lo fai, dillo nel report.

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/adr/ADR-0016` — *l'ignoto non autorizza un atto*.
3. `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs` righe **550-566**, il
   commento che spiega perché la freschezza si misura sull'ora del filo.

**REGOLA #3 su ogni riga**: i numeri qui sotto sono l'uscita reale dei comandi,
eseguiti il 2026-09-07. Se il codice ti dice altro, **vince il codice**.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull` prima, **`git
push` tu alla fine.**

---

## Il fatto misurato

`--decide-replay data/nostale_combat.noscap` — 8 211 pacchetti reali, 62 letture
di vitali, 117 colpi in 90 secondi — produce **sette cicli**:

```
counts by reason:
  5  Stato del mondo non disponibile: player_vitals_stale. Nessuna pianificazione possibile.
  2  Nessun candidato d'azione pianificato.
counts by outcome:
  5  NoWorldState
  2  NoCandidate
Acting enabled: False
```

Il comando esiste per stampare «the plan / safety / execution / verify scale of
every decision cycle over a recording». Su una registrazione reale **non
raggiunge mai `plan`**: cinque cicli su sette si fermano prima, e gli altri due
non trovano candidati.

## Perché — e perché **non** è un difetto da correggere

`GameplayProvider.cs:555` lo dice per esteso, ed è una scelta:

> *The time the packet crossed the wire, not the time this poll ran. They are the
> same thing on a live capture and hours apart on a replay, and the difference is
> exactly what a freshness rule needs to tell a current reading from a recorded
> one (ADR-0016).*

`observedAt` è l'ora del pacchetto (settembre); `now` è l'orologio di sistema
(oggi); `MaxVitalsAge` è 5 secondi. Ogni lettura registrata è quindi vecchia di
mesi, e diventa `player_vitals_stale`. **È corretto**: alla domanda «questa
lettura è attuale?» la risposta giusta su una registrazione è sempre no.

**Non toccare quella regola.** Chi la allenta per far girare il replay rompe la
protezione che impedisce di agire su byte vecchi. Questo task non la tocca:
ne aggiunge una seconda accanto, dichiarata.

---

## Cosa fare

### Metà 1 — dire la verità strutturale nell'uscita

Oggi chi legge quei sette cicli conclude che la registrazione è povera o che il
pianificatore è rotto. Nessuna delle due. Il rapporto deve distinguere:

- **`player_vitals_stale` perché la cattura è vecchia** — strutturale, vale per
  ogni registrazione, non dice nulla su questa;
- **`player_vitals_stale` per un buco dentro la cattura** — cioè due `stat`
  distanti più di `MaxVitalsAge` *nel tempo del filo*: questo sì che è un fatto
  su questa registrazione.

Oggi il primo caso copre il secondo e lo rende invisibile. Il rapporto stampa
una riga sola, una volta, in testa o nel riepilogo, che dice quale dei due sta
guardando e perché — con il numero di secondi fra l'ultima lettura e adesso, che
è il numero che rende ovvia la ragione.

### Metà 2 — `--as-of-capture`, la domanda che il comando voleva fare

```
--decide-replay <file.noscap> --as-of-capture
```

Un `TimeProvider` che **avanza con i timestamp della registrazione** invece che
con l'orologio di sistema: la freschezza viene giudicata com'era al momento
della cattura. La domanda diventa «cosa avrebbe deciso il runtime *mentre questo
accadeva*», che è quella per cui il comando è stato scritto, e resta diversa da
«questa lettura è attuale adesso», che ha sempre risposta no.

Il seme c'è già e non va inventato: `NetworkGameplayProvider` prende già un
`TimeProvider? clock = null` (`GameplayProvider.cs:523`). Manca solo che
`Gate1ObservationChannel.FromPackets` lo accetti e lo passi — **parametro
opzionale in coda**, così tutti e sei i chiamanti esistenti continuano a
compilare senza una modifica.

**`CaptureClock`** (file nuovo): un `TimeProvider` il cui `GetUtcNow()`
restituisce l'ultimo timestamp visto sul filo. Non è un orologio che scorre da
solo: avanza solo quando un pacchetto lo fa avanzare, e **non torna mai
indietro** (un timestamp fuori ordine non riporta l'ora indietro — lo dice un
test). Prima del primo pacchetto risponde con l'istante del primo pacchetto del
file, non con `DateTime.UtcNow`.

### Le tre cose che questa modalità **non** cambia — e che tre test fissano

1. **La provenienza resta `Cached`.** `FromPackets` continua a ricevere
   `DataSourceKind.Cached`; nessuna lettura registrata diventa `Live`. Se dopo
   la tua modifica un solo campo esce `Live`, la consegna è sbagliata.
2. **`Acting enabled` resta `False`.** Viene da un'altra politica e non si
   sfiora. L'uscita continua a stampare la riga che lo dice.
3. **Vale solo su file.** `--as-of-capture` è accettato solo dal percorso che
   apre un `.noscap`; nessun percorso vivo può riceverlo.

E l'uscita porta una riga di intestazione che dichiara la modalità, sulla forma
di quella che il comando già stampa sulla provenienza — un rapporto che risponde
a una domanda diversa deve dire quale.

---

## OWN (file nuovi)

- `src/NosAi.Runtime/Observability/CaptureClock.cs`
- `tests/NosAi.Runtime.Tests/DecideReplayAsOfCaptureTests.cs`

## MODIFY

- `src/NosAi.Runtime/Observability/DecideReplayCommand.cs`
- `src/NosAi.Runtime/Gate1/Gate1ObservationChannel.cs` — **solo** il parametro
  opzionale del clock

**Nient'altro.** In particolare: non `GameplayProvider.cs` (il seme c'è già,
usalo), non `Program.cs`, non i quattro file dei task in coda, niente in
`src/NosAi.Core/`.

---

## Test — `DecideReplayAsOfCaptureTests.cs` (nuovo)

Con `InMemoryPacketSource`, senza disco:

1. `CaptureClock` prima di ogni pacchetto risponde con l'istante del primo, non
   con l'ora di sistema.
2. Avanza sul timestamp di ogni pacchetto, e un timestamp fuori ordine **non**
   lo riporta indietro.
3. Due `stat` distanti meno di `MaxVitalsAge` nel tempo del filo: con
   `--as-of-capture` la seconda lettura è fresca; senza, è stale.
4. Due `stat` distanti **più** di `MaxVitalsAge` nel tempo del filo: stale in
   entrambe le modalità. È il caso che la Metà 1 deve saper distinguere, e qui
   si dimostra che la distinzione esiste davvero.
5. `--as-of-capture` rifiutato, con motivo nominato, se non c'è un file.
6. Rifiuti già in uso non regrediti: `empty.noscap`, `absent.noscap`,
   `junk.noscap` (vedi `DecideReplayCommandTests`).

Contro le registrazioni vere, con `RecordedCaptureFactAttribute` (**mai** un
`if (…) return`):

7. `[RecordedCaptureFact("nostale_combat.noscap")]` senza `--as-of-capture`:
   l'esito resta quello di oggi — nessun ciclo raggiunge `plan`, e il rapporto
   ora **dice perché**.
8. Lo stesso file **con** `--as-of-capture`: il replay arriva alla fase di
   pianificazione. Non fisso qui un numero di cicli, e non è pigrizia: quel
   numero è il risultato che nessuno conosce ancora, e inventarlo qui
   trasformerebbe una misura in un'aspettativa. **Riportalo tu nel report**,
   insieme al conteggio per esito. Il test asserisce la proprietà — almeno un
   ciclo supera `NoWorldState` — non il numero.
9. Nella stessa esecuzione: ogni lettura resta `Cached` e `Acting enabled` resta
   `False`. È il test che rende la Metà 2 sicura, e va scritto per primo.

---

## Fuori scope

- **Nessuna modifica alla regola di freschezza** né a `MaxVitalsAge`.
- **Nessuna attuazione**, nemmeno dietro un flag.
- **Nessun nuovo flag di primo livello**: `--as-of-capture` si legge dentro
  `DecideReplayCommand`, quindi `Program.cs` non si tocca.
- **Nessun aggiornamento ai documenti** — REGOLA #2, quella metà è di Claude.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errori, 0 avvisi**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` — 0 falliti, numeri
  riportati; gli skip esistenti restano skip.
- `dotnet test tests/NosAi.Core.Tests -c Release` — invariato.
- Le due esecuzioni reali, uscita incollata nel report:
  `--decide-replay data/nostale_combat.noscap` e la stessa con
  `--as-of-capture`. Il confronto fra i due riepiloghi **è** il risultato della
  consegna.
- Una riga che conferma esplicitamente le tre proprietà del §"non cambia":
  provenienza `Cached`, `Acting enabled: False`, modalità solo-file.
- Livello: **Integrated**. `Verified` richiede un operatore su client vivo.
