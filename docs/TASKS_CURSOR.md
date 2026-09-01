# Schede di lavoro — Cursor

**Obiettivo di riferimento:** `docs/OBIETTIVO_CONTROLLO_GIOCO.md`
**Vale sopra ogni altra istruzione locale:** `CLAUDE.md`, `.cursorrules`, `docs/adr/`

Ogni scheda qui sotto è autosufficiente. Contiene il percorso esatto, la firma
esatta, l'estratto di specifica che serve, i criteri di accettazione, il comando
di build e quello di test, e il messaggio di commit già scritto.

**Una scheda si esegue senza aprire nulla che non sia elencato in "File".** Se
per completarla servisse aprire un altro file, la scheda è scritta male: fermati e
segnalalo. Non compensare esplorando.

---

## Protocollo di esecuzione — le sette regole che tengono basso il costo

1. **Una scheda per conversazione.** Mai due schede nello stesso prompt, mai una
   scheda spezzata su due conversazioni. Finita la scheda, si apre una chat nuova.
2. **Niente `@codebase`, niente indicizzazione dell'intero repository.** Si allegano
   con `@file` **soltanto** i file elencati nella sezione "File" della scheda.
   L'esplorazione è la voce di costo più alta di tutte e in queste schede non serve:
   quello che c'è da sapere è già scritto dentro.
3. **Solo diff.** Mai riscrivere un file intero, mai ristampare una funzione che
   non cambia. `.cursorrules` §2 lo impone già; qui conta il doppio, perché questi
   file arrivano a 500 righe.
4. **Build e test solo del progetto toccato.** Il comando è scritto in ogni scheda.
   **Non compilare mai `NosAi.sln`:** fallisce sul progetto Android
   (`XABBA7000: Permission denied`, packaging APK) per un blocco file, brucia
   novanta secondi e riempie il contesto di uno stack trace che non riguarda il lavoro.
5. **Non toccare la zona riservata.** Se una modifica sembra richiederlo, fermati
   e segnalalo — è il segnale che la scheda va rivista, non un ostacolo da aggirare:

   ```
   src/NosAi.Runtime/Safety/       src/NosAi.Runtime/Gate3/
   src/NosAi.Runtime/Contracts/    docs/adr/
   src/NosAi.Runtime/LiveIntegration/ProcessMemoryReader.cs
   .cursorrules                    CLAUDE.md
   ```

6. **Additivo, sempre.** Nessuna firma pubblica esistente cambia di posizione o di
   tipo. I parametri nuovi si aggiungono **in coda, con un default**, così ogni
   chiamante esistente continua a compilare senza essere toccato.
7. **`UNKNOWN` non è mai `false`, `0` o vuoto.** È l'invariante che il progetto
   difende ovunque. Un lettore che non riesce a leggere restituisce *non lo so*
   **con il motivo**, mai un valore di comodo. Ogni scheda con un caso di lettura
   ha un test che verifica proprio questo, e non è negoziabile.

**Se una scheda risulta inesatta** — una firma diversa da quella scritta, un file
che non esiste, un test già presente — **fermati e riporta la differenza.** Una
scheda sbagliata eseguita alla lettera costa meno di una scheda sbagliata
compensata a intuito.

---

## Coda, in ordine di valore

| # | ID | Titolo | Sblocca |
|---|---|---|---|
| **C1** | F1-7 | `TargetFrameReader` — leggere il riquadro del bersaglio | il combattimento |
| **C2** | F1-2 | `mv` pubblica la posizione dei mob senza `in`/`st` precedente | sapere dove sono i bersagli |
| **C3** | F2-4 | `KeybindMap` — gli slot dell'operatore da configurazione | l'esecuzione delle azioni |
| **C4** | F4-1 | `NetworkWorldStateObserver` — rileggere lo stato dopo l'azione | la chiusura del ciclo |
| **C5** | F1-5 | `sr` — quali skill sono pronte | scelte di combattimento non sprecate |
| **C6** | F1-4 | `cond` — la velocità di movimento del giocatore | la verifica dello spostamento |
| **C7** | F1-3 | `stat` campo 4 — gli MP massimi | completezza dei vitali |
| **C8** | F1-6 | `lev` — livello ed esperienza | progressione |

**C2 dipende da F1-1, che fa Claude.** Non iniziarla finché `EntitySighting` non
porta una salute opzionale: la scheda lo dice e il codice non compilerebbe comunque.

---

## Prompt di avvio — da incollare in Cursor, una scheda per chat

Sostituire `C1` con l'identificativo della scheda e la lista dei file con quella
della sezione "File" della scheda. Non aggiungere altri allegati.

```
Esegui la scheda C1 di @docs/TASKS_CURSOR.md.

Allegati: @docs/TASKS_CURSOR.md @src/NosAi.Runtime/Perception/HudBarFillReader.cs

Vincoli, senza eccezioni:
- Non usare @codebase e non aprire file diversi da quelli allegati. Tutto ciò
  che serve è nella scheda.
- Solo diff. Non ristampare funzioni che non cambiano.
- Non toccare la zona riservata elencata nel protocollo del documento.
- Build e test solo con i comandi scritti nella scheda. Mai NosAi.sln.
- Un valore non letto è UNKNOWN con il motivo, mai 0, false o vuoto.
- Se una firma o un percorso della scheda non corrisponde al codice reale,
  fermati e dimmi la differenza invece di adattarti.

Al termine riporta soltanto: file modificati, esito della build, esito dei test
con il numero di test passati, e i criteri di accettazione che restano scoperti.
```

---

# C1 — `TargetFrameReader`: leggere il riquadro del bersaglio

**ID:** F1-7 · **Sblocca:** B1, il buco che impedisce ogni combattimento

## Perché

ADR-0016 fa saltare al planner ogni regola d'attacco quando `HasTarget` è ignoto,
e oggi è ignoto sempre: nessun pacchetto delle catture lo stabilisce. Questa
scheda produce **il lettore**; sarà Claude a decidere come il suo risultato
diventa `HasTarget` incrociandolo con il wire (F1-8). Qui serve solo che il
lettore dica la verità su ciò che vede.

## File

- Nuovo: `src/NosAi.Runtime/Perception/TargetFrameReader.cs`
- Nuovo: `tests/NosAi.Runtime.Tests/TargetFrameReaderTests.cs`
- Da allegare in sola lettura, come modello: `src/NosAi.Runtime/Perception/HudBarFillReader.cs`

## Cosa esiste già (non riscriverlo, usalo)

```csharp
// src/NosAi.Runtime/Perception/HudBarFillReader.cs
public enum HudFillHue : byte { /* … */ }
public readonly record struct HudBarMeasure(double? Ratio, double Confidence, string? FailureReason);
public static class HudBarFillReader
{
    public static HudBarMeasure Measure(ReadOnlySpan<byte> bgra, int width, int height, HudFillHue hue);
}

// src/NosAi.Runtime/Perception/PerceptionPipeline.cs
public enum RoiKind : byte { PlayerHpBar, PlayerMpBar, Minimap, TargetHpBar, ChatLog }
public sealed record RegionOfInterest(RoiKind Kind, PixelRect Rect);
```

## Da scrivere

```csharp
namespace NosAi.Runtime.Perception;

/// <summary>Cosa il riquadro del bersaglio ha detto di sé.</summary>
public enum TargetFrameState : byte
{
    /// <summary>I pixel non erano leggibili. Non è "nessun bersaglio".</summary>
    Unreadable = 0,
    /// <summary>Riquadro presente: c'è una barra bersaglio con un riempimento misurabile.</summary>
    Present = 1,
    /// <summary>Riquadro assente: la regione è leggibile e non contiene una barra.</summary>
    Absent = 2
}

public readonly record struct TargetFrameReading(
    TargetFrameState State,
    double? HpRatio,
    double Confidence,
    string? FailureReason);

public static class TargetFrameReader
{
    /// <summary>Legge la regione del riquadro bersaglio da un buffer BGRA.</summary>
    /// <param name="bgra">Pixel della sola ROI, quattro byte per pixel.</param>
    public static TargetFrameReading Read(ReadOnlySpan<byte> bgra, int width, int height);
}
```

## La regola che rende utile questa scheda

**Non leggibile non è "nessun bersaglio".**

Le proporzioni della ROI `TargetHpBar` in `RoiSegmenter.Segment` — `0.40, 0.06,
0.20, 0.02` — non sono mai state calibrate su un client reale: solo la
`PlayerHpBar` lo è (prova T-03). Finché non lo saranno, quella regione può
inquadrare tutt'altro.

Un lettore che restituisse `Absent` quando i pixel non gli dicono niente
farebbe credere al pianificatore che il bersaglio non c'è, e ADR-0016 lo
manderebbe a camminare verso un waypoint **durante un combattimento**. È
esattamente il caso che quell'ADR è stato scritto per impedire.

Quindi:

- `Present` **solo** con un riempimento misurato e una confidenza sopra soglia;
- `Absent` **solo** con una regione leggibile e coerente in cui non c'è barra —
  cioè copertura di colore barra sotto la soglia minima **su una regione che ha
  superato i controlli di forma**;
- `Unreadable` in **ogni** altro caso: dimensioni sotto i minimi, buffer di
  lunghezza incoerente con `width * height * 4`, misura senza confidenza, macchie
  oltre soglia. Sempre con `FailureReason` valorizzato.

## Criteri di accettazione

1. `Read` non lancia mai: ogni ingresso malformato è `Unreadable` con un motivo.
2. `FailureReason` è non nullo **se e solo se** lo stato è `Unreadable`.
3. `HpRatio` è non nullo **se e solo se** lo stato è `Present`, ed è in `[0, 1]`.
4. `width` o `height` sotto i minimi di `HudBarFillReader` → `Unreadable`.
5. `bgra.Length != width * height * 4` → `Unreadable`, senza leggere il buffer.
6. Una regione tutta nera e ben formata → `Absent`, non `Unreadable`.
7. Nessuna dipendenza da Win32, da DXGI o dal filesystem: la funzione è pura.

## Test richiesti — `TargetFrameReaderTests.cs`

Buffer sintetici costruiti nel test, non file su disco. Un test per criterio:

- barra piena costruita a mano → `Present`, rapporto ≈ 1.0;
- barra a metà → `Present`, rapporto ≈ 0.5 con tolleranza dichiarata;
- regione nera → `Absent`, `HpRatio` nullo, `FailureReason` nullo;
- buffer troncato → `Unreadable`, motivo valorizzato;
- larghezza 3 pixel → `Unreadable`;
- rumore casuale a macchie → `Unreadable`, **non** `Absent`. *(È il test che
  protegge l'invariante: metti in chiaro nel nome del test che il rumore non è
  l'assenza di un bersaglio.)*

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter TargetFrameReaderTests
```

## Commit

```
feat(perception): read the target frame, and say when it cannot
```

---

# C2 — `mv` pubblica la posizione anche senza `in`/`st` precedente

**ID:** F1-2 · **Sblocca:** B3 · **Bloccata da:** F1-1 (Claude)

> **Non iniziare** finché `EntitySighting` non porta `double? HpRatio`. Verificalo
> aprendo il solo `GameTrafficObserver.cs`: se il tipo è ancora `double HpRatio`,
> fermati e segnalalo.

## Perché

7685 pacchetti `mv` su 8211 non producono oggi alcuna osservazione. Un `mv`
porta la posizione ma non la salute, e `EntitySighting` non aveva spazio per
"posizione nota, salute ignota", quindi il movimento veniva scartato finché un
`in` o uno `st` non avesse detto la salute di quell'entità. Su una cattura che
comincia a sessione avviata sono 25 `in` e 49 `st` contro 7685 `mv`: tutto ciò
che era già sullo schermo resta invisibile.

Dopo F1-1 lo spazio c'è. Questa scheda lo riempie.

## File

- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs` — il solo
  metodo `DecodeMove`, e il campo `HasHp` di `TrackedEntity` se serve leggerlo
- `tests/NosAi.Runtime.Tests/` — il file di test esistente del decoder

## Specifica del pacchetto — da `docs/PROTOCOLLO_NOSTALE.md`, già verificata

```
mv 3 3194 121 110 5
   ty id   x   y   speed
```

> Tutti i campi **confirmed** per coerenza su 7685 pacchetti e continuità con `in`.
> **Non porta mai il giocatore**: ogni `mv` delle catture è di tipo entità 3.

## Cosa cambiare

`DecodeMove` oggi aggiorna la posizione dell'entità e restituisce una
`EntitySighting` **soltanto** se quella entità aveva già una salute nota
(`previous.HasHp`). Deve invece restituire sempre una sighting:

- se la salute è nota → `HpRatio` valorizzato, come adesso;
- se **non** è nota → `HpRatio` **nullo**.

Il tipo di entità continua a essere filtrato come adesso: solo il tipo 3 è letto.
Le forme degli altri tipi non sono state osservate, e leggere `x` e `y` da campi
che non sono `x` e `y` produrrebbe una posizione inventata con un'etichetta `LIVE`.

## Criteri di accettazione

1. Un `mv` di un'entità mai vista prima produce una sighting con posizione e
   `HpRatio` nullo.
2. Un `mv` di un'entità la cui salute è nota produce la stessa sighting di prima,
   con lo stesso rapporto: nessuna regressione.
3. Un `mv` di tipo entità diverso da 3 continua a non produrre nulla.
4. Un `mv` malformato — campi mancanti, coordinate non numeriche — continua a non
   produrre nulla. Non si emette una sighting a coordinate zero.
5. **Nessuna salute inventata.** Da nessuna parte compare un rapporto `1.0`, `0.0`
   o un valore di comodo per un'entità di cui non si è letta la salute.

## Test richiesti

- `mv` di entità sconosciuta → una sighting, `HpRatio` nullo;
- `in` seguito da `mv` → sighting con il rapporto già noto conservato;
- `mv` con tipo `1` → nessuna osservazione;
- `mv` con quattro soli campi → nessuna osservazione;
- test di non-regressione: la sequenza già coperta dai test esistenti dà lo
  stesso risultato di prima.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter Decoder
```

## Commit

```
feat(perception): report where an entity moved, even before its health is known
```

---

# C3 — `KeybindMap`: gli slot dell'operatore, letti da configurazione

**ID:** F2-4 · **Sblocca:** l'effector reale (B4)

## Perché

Un `ActionCandidate` dice *usa una pozione*; il client vuole un tasto. Nessuno può
sapere quale, se non l'operatore: la barra rapida è configurata da lui. Un default
inventato — "la pozione è sul 1" — farebbe premere un tasto qualsiasi durante un
combattimento reale.

Quindi: **nessun default.** Una mappa non configurata restituisce *non lo so*, e
sarà l'effector a rifiutare l'azione con quel motivo. È la stessa regola dei
vitali non letti, applicata all'uscita invece che all'ingresso.

## File

- Nuovo: `src/NosAi.Runtime/LowLevel/KeybindMap.cs`
- Nuovo: `tests/NosAi.Runtime.Tests/KeybindMapTests.cs`
- Da allegare in sola lettura: `src/NosAi.Runtime/LowLevel/VirtualKeys.cs`

## Da scrivere

```csharp
namespace NosAi.Runtime.LowLevel;

/// <summary>Un gesto che l'operatore ha associato a un'intenzione.</summary>
public readonly record struct Keybind(ushort VirtualKey, string Label);

/// <summary>
/// Quali tasti valgono quali intenzioni, secondo l'operatore. Una voce assente
/// non ha un default: restituisce false, e chi chiama rifiuta l'azione.
/// </summary>
public sealed class KeybindMap
{
    /// <summary>Mappa vuota: nessuna intenzione è associata a un tasto.</summary>
    public static KeybindMap Empty { get; }

    /// <summary>Legge la mappa dal JSON dell'operatore, o dice perché non ci è riuscita.</summary>
    /// <returns>false con <paramref name="failureReason"/> valorizzato in caso di problema.</returns>
    public static bool TryLoad(string path, out KeybindMap map, out string? failureReason);

    /// <summary>Il tasto per un'intenzione, se l'operatore ne ha configurato uno.</summary>
    public bool TryGet(string intent, out Keybind bind);

    /// <summary>Le intenzioni configurate, in ordine alfabetico.</summary>
    public IReadOnlyCollection<string> ConfiguredIntents { get; }
}
```

## Formato del file — `data/config/keybinds.json`

```json
{
  "version": 1,
  "binds": {
    "potion.hp":    { "virtualKey": 49, "label": "1" },
    "potion.mp":    { "virtualKey": 50, "label": "2" },
    "attack.basic": { "virtualKey": 32, "label": "Space" },
    "skill.1":      { "virtualKey": 112, "label": "F1" }
  }
}
```

Le chiavi di `binds` sono libere: `KeybindMap` non conosce l'elenco delle
intenzioni e non lo valida. Sapere quali intenzioni esistono è compito di chi
chiede, non di chi conserva. Una chiave sconosciuta a chi chiede è semplicemente
una voce mai richiesta.

## Criteri di accettazione

1. `version` diversa da `1` → `TryLoad` restituisce false con motivo. Nessuna
   ipotesi su un formato futuro.
2. File assente → false con motivo, **non** una mappa vuota. Non trovare il file e
   trovarlo vuoto sono due stati diversi e vanno riportati come tali.
3. JSON malformato → false con motivo. Nessuna eccezione propagata al chiamante.
4. `virtualKey` fuori da `[1, 254]` → quella voce è rifiutata e l'intero caricamento
   fallisce con motivo. Un tasto virtuale `0` non esiste e premerlo non è definito.
5. Intenzione duplicata nel JSON → `TryLoad` fallisce con motivo. **Non** "vince
   l'ultima": un file ambiguo si segnala, non si interpreta.
6. `TryGet` su un'intenzione non configurata → false, e `bind` resta `default`.
7. `Empty.ConfiguredIntents` è vuota e `Empty.TryGet` restituisce sempre false.
8. Nessuna scrittura su disco: la classe legge soltanto.

## Test richiesti

Uno per criterio, con i file JSON scritti in una directory temporanea dal test e
cancellati alla fine. Includi il caso "file valido, quattro voci, tutte
rileggibili".

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter KeybindMapTests
```

## Commit

```
feat(input): let the operator say which key means which intention
```

---

# C4 — `NetworkWorldStateObserver`: rileggere lo stato dopo l'azione

**ID:** F4-1 · **Sblocca:** B6, la chiusura del ciclo

## Perché

`ActionExecutionVerifier` confronta la previsione con un `ObservedState` letto da
`IWorldStateObserver`. Il seam esiste, il verifier lo usa correttamente, e
**nessuno lo implementa**: ogni esecuzione finirebbe `Unverified`, che non è né
successo né fallimento e non chiude niente.

Il feed di rete legge già gli HP propri dal `stat`. È la rilettura che serve.

## File

- Nuovo: `src/NosAi.Runtime/LiveIntegration/NetworkWorldStateObserver.cs`
- Nuovo: `tests/NosAi.Runtime.Tests/NetworkWorldStateObserverTests.cs`
- Da allegare in sola lettura: `src/NosAi.Runtime/Gate3/Gate3Observation.cs`,
  `src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`

## Da scrivere

Una classe che implementa `IWorldStateObserver` prendendo un `IGameplayProvider`
nel costruttore e traducendo la sua `GameplayObservation` in `ObservedState`.

**Leggi le due interfacce dai file allegati e rispettane le firme esatte.** Se
`Gate3Observation.cs` definisce `ObservedState` diversamente da come te lo
aspetti, vale il file, non l'aspettativa.

## Le regole che contano

1. **Un campo non osservato non diventa un numero.** Se
   `GameplayObservation.Hp` è `UNKNOWN`, l'`ObservedState` non riporta zero: usa
   il modo che `Gate3Observation.cs` prevede per dire "non osservato". Se non ne
   prevede nessuno, **fermati e segnalalo** — è una modifica al contratto e sta
   nella zona riservata.
2. **Un'eccezione del provider non abbatte la pipeline.** `Gate3ExecutionOrchestrator`
   ha già un test che verifica che un osservatore che lancia lascia il ciclo
   `Unverified`. Non aggiungere un `catch` che nasconda l'errore: propaga o
   restituisci non-osservato, secondo ciò che il seam dichiara. Non inventare una
   terza via.
3. **Nessuna cache propria.** Il provider gestisce già la ritenzione e classifica
   `CACHED` ciò che ripubblica. Una seconda cache qui produrrebbe una lettura
   vecchia con un'età sbagliata, e ADR-0016 misura la freschezza proprio su
   quell'età.

## Criteri di accettazione

1. Provider con vitali osservati → `ObservedState` con quegli stessi valori e la
   stessa classificazione.
2. Provider `Unobserved` → stato non osservato, con il motivo conservato.
3. Provider che lancia → nessuna eccezione ingoiata in silenzio (vedi regola 2).
4. Nessun accesso a rete, disco o Win32: il test costruisce un provider finto in
   memoria e la classe non sa da dove vengano i dati.
5. Il costruttore rifiuta un provider nullo con `ArgumentNullException`.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter NetworkWorldStateObserverTests
```

## Commit

```
feat(gate3): read the world back through the provider that observed it
```

---

# C5 — `sr`: quali skill sono pronte

**ID:** F1-5

## Perché

Il planner propone `UseSkill` quando gli MP bastano, senza sapere se la skill sia
in ricarica. Sul wire l'informazione c'è.

## File

- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs` — aggiungere
  il ramo `"sr"` allo `switch` di `Decode` e il metodo che lo tratta
- il file di test esistente del decoder

## Specifica — da `docs/PROTOCOLLO_NOSTALE.md`

```
sr 0     sr 2     sr 6
   slot
```

> 17 occorrenze. **probable** — skill pronta / ricarica terminata, per slot.

## Cosa fare

Emetti un `GameEvent` con un `GameEventKind` adatto fra quelli **già definiti**
in `GameTrafficObserver.cs` e un `Descriptor` che porta lo slot. **Non aggiungere
valori all'enum `GameEventKind`**: è un contratto condiviso e sta nella zona
riservata. Se nessun valore esistente è onesto per questo evento, fermati e
segnalalo: la scelta è di Claude.

## Criteri di accettazione

1. `sr <n>` con `n` intero non negativo → un evento con lo slot nel descrittore.
2. `sr` senza campo, o con campo non numerico, o negativo → nessuna osservazione.
3. Il valore è `probable` nella specifica: il commento nel codice lo dice, con il
   rimando a `docs/PROTOCOLLO_NOSTALE.md`. Non spacciarlo per confermato.
4. Nessuna regressione sugli opcode già letti.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter Decoder
```

## Commit

```
feat(perception): read which skill slot came off cooldown
```

---

# C6 — `cond`: la velocità di movimento del giocatore

**ID:** F1-4

## Perché

Verificare uno spostamento vuol dire sapere quanto avrebbe dovuto durare. Senza
la velocità, la finestra di verifica sarebbe un numero scelto a caso.

## File

- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- il file di test esistente del decoder

## Specifica — da `docs/PROTOCOLLO_NOSTALE.md`

```
cond 1 3443217 0 0 11
     ty id     ? ? speed
```

| Campo | Confidenza |
|---|---|
| tipo, id | **confirmed** |
| campi 3 e 4 | **probable** — candidati: non-può-attaccare, non-può-muoversi. Entrambi `0` in tutte le catture, quindi **mai osservati asseriti** |
| velocità (campo 5) | **probable** — `11` per un personaggio di livello 56 |

## Cosa fare

Leggi **solo la velocità**, e **solo per il tipo entità 1** (il giocatore: è
confermato che il proprio id compare come tipo 1 in `cond`).

**Non leggere i campi 3 e 4.** Sono `0` in ogni pacchetto di entrambe le catture:
nessuno li ha mai visti asseriti, quindi nessuno sa cosa significhi il valore `1`.
Un flag "non può muoversi" ricavato da un campo mai osservato diverso da zero è
un'ipotesi con l'aspetto di un'osservazione, ed è precisamente ciò che
`docs/PROTOCOLLO_NOSTALE.md` vieta per i campi marcati *unknown*.

Emetti un evento con la velocità nel descrittore, come in C5, senza aggiungere
valori all'enum.

## Criteri di accettazione

1. `cond 1 <id> 0 0 11` → un'osservazione che porta la velocità `11`.
2. `cond 3 <id> …` → nessuna osservazione: non è il giocatore.
3. Velocità non numerica, negativa o assente → nessuna osservazione.
4. I campi 3 e 4 non compaiono in nessun punto del codice aggiunto.
5. Un commento nel codice cita la specifica e il livello `probable`.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter Decoder
```

## Commit

```
feat(perception): read the player's movement speed from cond
```

---

# C7 — `stat` campo 4: gli MP massimi

**ID:** F1-3 · **Priorità:** bassa

## Perché

Il decoder **legge già** `maxMp` e lo usa per rifiutare uno `stat` malformato
(`mp > maxMp`), ma poi lo butta: `PlayerVitals` non ha un campo per portarlo.
Il valore è **confirmed** contro l'HUD. È già osservato: manca solo lo spazio.

## File

- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs` — il solo record
  `PlayerVitals`
- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs` — il solo
  metodo `DecodeStat`
- il file di test esistente del decoder

## Firma attuale — non spostare nulla

```csharp
public sealed record PlayerVitals(
    int Hp,
    int MaxHp,
    int Mp,
    bool? HasTarget,
    bool? InCombat,
    DataSourceKind Source,
    DateTime? ObservedAtUtc = null);
```

Aggiungi `int? MaxMp = null` **in coda**, dopo `ObservedAtUtc`. In coda e con un
default: ogni chiamante posizionale esistente continua a compilare senza essere
toccato. Non riordinare per estetica — un riordino qui è una modifica di contratto
travestita da pulizia.

## Cosa non fare

**Non pubblicare `MaxMp` sullo snapshot Gate 1.** `GameplayObservation.ToWire()`
è il contratto `gate1.snapshot.v1` letto anche dal telefono: aggiungere un campo
lì è una modifica di protocollo, e sta nella zona riservata. Questa scheda si
ferma al decoder. Alla pubblicazione pensa Claude.

## Criteri di accettazione

1. Uno `stat` valido produce `PlayerVitals` con `MaxMp` valorizzato.
2. Tutti i test esistenti sul `stat` passano invariati, senza essere modificati.
3. `GameplayObservation` non è toccato.
4. Un `stat` con `mp > maxMp` continua a essere rifiutato come adesso.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter Decoder
```

## Commit

```
feat(perception): keep the max MP the decoder already validates
```

---

# C8 — `lev`: livello ed esperienza

**ID:** F1-6 · **Priorità:** bassa

## Perché

Catalogato in `docs/PROTOCOLLO_NOSTALE.md` e non ancora pubblicato. Non serve al
movimento né al combattimento: è la progressione, e vale come conferma
indipendente che il decoder segue la sessione giusta.

## File

- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- il file di test esistente del decoder

## Specifica — da `docs/PROTOCOLLO_NOSTALE.md`

```
lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0
    lv xp      jl jXp   xpMax    jXpMax rep   ?
```

| Campi | Confidenza |
|---|---|
| livello, XP, livello di lavoro, XP di lavoro | **probable** — l'XP sale in modo monotono nella cattura mentre gli altri restano fermi |
| XP massimi, XP di lavoro massimi | **probable** — costanti e maggiori dei valori correnti |
| campo 7 (`35106`) | **unknown** — candidato reputazione. **Non leggerlo** |
| resto | **unknown**. **Non leggerlo** |

## Criteri di accettazione

1. Sono letti **sei** campi: livello, XP, livello di lavoro, XP di lavoro, XP
   massimi, XP di lavoro massimi. Nessun altro.
2. Il campo 7 non compare nel codice aggiunto.
3. `xp > xpMax` o `jobXp > jobXpMax` → nessuna osservazione. È il controllo
   aritmetico che rende la lettura affidabile, ed è quello che ha scartato il
   campo 5 di `st`.
4. Valori negativi o non numerici → nessuna osservazione.
5. Nessun valore aggiunto a `GameEventKind`.

## Comandi

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter Decoder
```

## Commit

```
feat(perception): read level and experience from lev
```
