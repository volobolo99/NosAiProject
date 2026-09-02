# Catalogo delle azioni e delle post-condizioni

**Versione:** 1.0
**Data:** 2 settembre 2026
**Ruolo:** normativo su **che cosa promette ogni azione e come quella promessa viene
controllata**. Il canale di attuazione sta in `adr/ADR-0019`; le guardie dell'istante
dell'atto in `CONTROLLO_PERSONAGGIO_ATTUAZIONE.md`; l'ordine dei lavori in
`CONTROLLO_PERSONAGGIO_ROADMAP.md`.
**Subordinato a:** `NOSAI_ARCHITECTURE_BASELINE.md` e `docs/adr/*` — in particolare
`ADR-0002` (classificazione), `ADR-0003` (autorità di sicurezza), `ADR-0012` (sorgente
di osservazione), `ADR-0016` (pianificare su osservazione parziale), `ADR-0018` (il
bersaglio dallo schermo).
**Consumato da:** il passo Verify di Gate 3 (`ActionExecutionVerifier`), le tappe `P4`,
`P6` e `P7` del controllo del personaggio, e il Gate 7 di `ROADMAP_ESECUTIVA.md`
(`IPostCondition`, `PostConditionTable`).

---

## 0. Perché esiste

`ROADMAP_ESECUTIVA.md` § 8.3 chiede una cosa sola, e la chiede da sempre:

> Ogni `ActionId` dichiara un predicato osservabile su `WorldState`.

Quel predicato non è mai stato scritto. Al suo posto ci sono due cose, entrambe nel
runtime che gira oggi:

- una tabella di previsioni costanti in `SimulationEngine.Simulate`
  (`src/NosAi.Runtime/Gate3/Gate3Runtime.cs`), che per ogni tipo di azione produce un
  delta di HP, un delta di MP e una durata;
- **un unico confronto** in `ActionExecutionVerifier.Verify` (stesso file), che per
  **tutte e otto** le azioni verifica la stessa cosa: che la stringa
  `POST_HP_<hp>_MP_<mp>` costruita dalla previsione sia identica a quella costruita
  dall'osservazione.

Nel frattempo tre tappe stanno progettando tre verificatori separati: `P4` un
`MovementVerifier` a griglia, `P6` un verifier « oggetto rimosso dalla lista », `P7`
un verifier « cooldown attivo e MP decrementato ». Nessuno dei tre ha una semantica
comune di « riuscita », di « divergenza » o di « non verificabile », e nessuno dei tre
sa che cosa fare quando la sorgente che gli serve non esiste ancora.

Questo documento definisce quella semantica **prima** che i tre vengano scritti, e la
definisce sulle grandezze che il runtime osserva davvero.

---

## 1. Che cosa non funziona nel confronto attuale

Quattro difetti, tutti verificabili leggendo i due metodi citati. Non sono
imprecisioni: sono quattro modi diversi in cui l'esito del passo Verify oggi non
riguarda l'azione che è stata eseguita.

### 1.1 Per metà delle azioni il predicato è « non è cambiato niente »

`MoveToPosition` e `TargetEntity` hanno previsione `hpDelta = 0`, `mpDelta = 0`. La
firma attesa è quindi « HP e MP identici a prima ». Il confronto passa quando nulla è
cambiato — cioè **anche quando il personaggio non si è mosso e il clic non ha
selezionato nulla** — e fallisce quando un mostro colpisce durante uno spostamento
riuscito.

Il verificatore risponde a una domanda che non ha rapporto con l'atto.

### 1.2 L'attacco è verificato sul soggetto sbagliato

`UseBasicAttack` prevede `hpDelta = -15`: gli HP **del giocatore**. L'effetto
osservabile di un attacco è la vita **del bersaglio** — `st` campi 7 e 9, e il danno
di `su` con il giocatore come attaccante, entrambi catalogati come leggibili in
`PROTOCOLLO_NOSTALE.md`. I quindici punti sono una ritorsione ipotizzata, non
l'effetto dell'azione, e nessuno li ha misurati.

### 1.3 L'uguaglianza esatta rende il predicato falso in entrambi i versi

`UseConsumable` prevede `+300` HP e `+150` MP esatti. Una pozione che ne restituisce
un numero diverso produce `Discrepant`; un colpo incassato nello stesso istante pure.
E poiché `stat` viene inviato **quando il numero cambia, non a cadenza** — 62 pacchetti
in 90 s di combattimento — confrontare due estremi significa confrontare due punti
scelti dal traffico, non dall'azione.

### 1.4 Il tier di verifica è più severo del tier di attuazione

`ActionExecutionVerifier.Verify` rifiuta di concludere quando
`ObservedState.IsFullyObserved` è falso, e quel predicato richiede `HP` e `MP`
**`LIVE`** (`src/NosAi.Runtime/Gate3/Gate3Observation.cs`).

`ADR-0016` § 2 ha però stabilito che si **agisce** su `LIVE`, `DERIVED` o `CACHED`
entro il limite di freschezza, e ha tolto a `IsFullyObserved` il compito di aprire
l'effettore. Le due regole non sono state riconciliate: il runtime **può agire** su
una lettura `DERIVED` e **non può verificarla mai**. Ogni ciclo guidato dallo schermo
— cioè tutto ciò che `ADR-0018` rende possibile — finisce `Unverified`.

La severità sta dalla parte sbagliata. Un dato troppo debole per verificare è troppo
debole per agire; il contrario non è vero.

---

## 2. Le regole comuni

Valgono per ogni azione, presente e futura. Un'azione che non le rispetta non entra
nel catalogo.

| ID | Regola |
|---|---|
| `VER-01` | La post-condizione è un predicato su **osservazioni**, mai sulla previsione. Dove non c'è osservazione non c'è conferma |
| `VER-02` | **Direzione e limite, non uguaglianza.** Il predicato dichiara il verso del cambiamento e i suoi estremi ammissibili, non un valore esatto |
| `VER-03` | L'osservazione che verifica deve portare `ObservedAtUtc` **posteriore** all'istante di emissione. Una lettura anteriore non è una conferma debole: non è una conferma |
| `VER-04` | Il tier di verifica **non è più severo** del tier di attuazione. Ciò che `ADR-0016` ammette per agire — `LIVE`, `DERIVED`, `CACHED` freschi — è ammesso per verificare |
| `VER-05` | **Non osservabile non è né riuscito né fallito.** L'esito è `Unverified`, non conta come successo, e il Recovery non lo conta come fallimento |
| `VER-06` | La finestra di verifica appartiene **all'azione**, non al ciclo. Un movimento e una pozione non si controllano nello stesso tempo |
| `VER-07` | Il predicato nomina il **soggetto** giusto: chi subisce l'effetto, non chi lo produce |
| `VER-08` | Un'azione la cui post-condizione è **interamente** non osservabile non è eseguibile. Una parzialmente osservabile si esegue, e la parte cieca viene **dichiarata** tale, mai data per soddisfatta |
| `VER-09` | Il predicato si valuta **sulla serie** delle osservazioni nella finestra, non sui due estremi. Le sorgenti sono guidate dal cambiamento, non dall'orologio |

### Perché `VER-02`

L'uguaglianza esatta richiede di conoscere il valore atteso. Per la cura sarebbe la
resa della pozione, per la skill il costo in MP: entrambi stanno nel database di
riferimento fra 7 726 oggetti e 1 958 abilità, e `STATO_IMPLEMENTAZIONE.md` registra
che **quale slot di `ATTRIB` porti quale significato non è dichiarato da nessuna parte
e non viene indovinato**. Un predicato che dipende da un numero non stabilito è un
predicato che non si può scrivere. Un predicato sul verso — « gli MP sono
strettamente diminuiti », « la vita del bersaglio è strettamente diminuita » — si
scrive oggi e non diventa falso quando quel numero sarà noto: si stringe.

### Perché `VER-09`

`stat` e `st` arrivano quando il valore cambia. Fra due pacchetti il provider
ripubblica l'ultima lettura come `CACHED` con l'istante in cui è stata davvero
osservata (`ADR-0012`, `ADR-0016`). Confrontare l'estremo finale con l'estremo
iniziale significa perdere ogni cambiamento avvenuto e disfatto dentro la finestra —
il caso tipico è una cura seguita da un colpo, che a saldo è negativa mentre la
pozione ha funzionato. Il predicato guarda il **massimo** (o il minimo, secondo il
verso) osservato nella finestra, che è la quantità che l'azione ha davvero prodotto.

---

## 3. Il catalogo

Le otto azioni sono quelle di `ActionType`
(`src/NosAi.Runtime/Autonomy/AutonomyPipeline.cs`). Nessuna è aggiunta qui: aggiungerne
una è la procedura del § 9.

### 3.1 Ingressi — bersaglio, gesto, precondizioni

Il bersaglio è quello che `ActionCandidate.RequireTarget` già impone; il gesto è quello
che `InputActionEffector.ApplyAsync` già emette. Questa tabella non decide, registra.

| Azione | Bersaglio ammesso | Gesto | Precondizioni osservabili |
|---|---|---|---|
| `MoveToPosition` | `Position` | clic sul punto proiettato | proiezione calibrata; cella di destinazione **osservata** e calpestabile; nessuna cella del segmento bloccata |
| `TargetEntity` | `Entity` risolta con posizione | clic sul punto proiettato | proiezione calibrata; avvistamento del bersaglio entro `MaxSightingAge` e `MaxRangeTiles` |
| `UseBasicAttack` | `Entity` risolta con posizione | clic sul punto proiettato | `HasTarget` **noto e vero**; bersaglio vivo secondo l'ultimo avvistamento |
| `UseSkill` | `Entity` risolta con posizione | pressione di `skill.<id>` | `HasTarget` noto e vero; `MP` noti; keybind configurato |
| `UseConsumable` | `InventorySlot` | pressione di `consumable.<slot>` | `HP` o `MP` noti; keybind configurato per quello slot |
| `CollectGroundItem` | `Position` o `Entity` | **nessuno** — rifiuto `action_not_implemented` | — |
| `RestAndRecover` | `None` | **nessuno** — rifiuto `action_not_implemented` | — |
| `EmergencyFlee` | `Position` | clic sul punto proiettato | proiezione calibrata; posizione propria nota; direzione di fuga osservata libera |

### 3.2 Uscite — post-condizione, sorgente, finestra

`Stato` dice se la post-condizione è controllabile **oggi**, con le sorgenti che
esistono. Il § 6 elenca che cosa chiude ogni riga incompleta.

| Azione | Post-condizione | Osservabile da | Finestra | Stato |
|---|---|---|---|---|
| `MoveToPosition` | la cella occupata si è avvicinata alla destinazione di almeno una cella | posizione propria (memoria) + griglia del client | 350 ms ± 20 ms **per cella** (`P4`) | **parziale** — serve il lettore di posizione stabile |
| `TargetEntity` | compare il riquadro bersaglio, e `HasTarget` passa a vero | schermo (`TargetFrameReader`), `ADR-0018` | 250 ms *(dichiarata)* | **cieca** — ROI bersaglio non calibrata |
| `UseBasicAttack` | la vita **del bersaglio** è strettamente diminuita, oppure il bersaglio è morto | `st` / `HpRatio` dell'avvistamento; `die` | 1200 ms *(dichiarata, da misurare sui `.noscap`)* | **osservabile** |
| `UseSkill` | gli `MP` sono strettamente diminuiti **e** la skill è entrata in cooldown | `stat` per gli MP; `sr` per il cooldown | 250 ms per gli MP (`P7`) | **parziale** — `sr` non è decodificato |
| `UseConsumable` | il massimo di `HP` (o `MP`) nella finestra supera il valore all'emissione | `stat` | 600 ms *(dichiarata)* | **osservabile** |
| `CollectGroundItem` | lo slot d'inventario del vnum raccolto è aumentato | `get` + `ivn` | — | **cieca** — nessuno dei due è decodificato |
| `RestAndRecover` | `HP` e `MP` crescono monotoni per l'intera finestra | `stat` | — | **cieca** — nessun gesto |
| `EmergencyFlee` | la distanza dall'ostile più vicino osservato è aumentata | posizione propria + avvistamenti | 500 ms + finestra per cella | **parziale** — come il movimento |

---

## 4. Le schede

Una per azione. La formula di divergenza è normalizzata in `[0,1]`: `0` è la promessa
mantenuta, `1` è la promessa contraddetta.

### 4.1 `MoveToPosition`

**Promessa.** La cella occupata cambia nella direzione della destinazione.

```
d = clamp( celle(osservata, destinazione) / max(1, celle(partenza, destinazione)), 0, 1 )
```

Tre esiti distinti, e vanno tenuti distinti perché portano a decisioni diverse:
**riuscita** (`d ≤ 0.15`), **stallo** (la cella non è cambiata: `d = 1`, e la causa
plausibile è un ostacolo che la griglia non ha, quindi si ripiana invece di
ripetere), **deviata** (la cella è cambiata allontanandosi: `d = 1`, e ripetere è
peggio che fermarsi, perché la proiezione sta puntando altrove).

**Che cosa non entra nel predicato.** `HP` e `MP`. Un colpo incassato durante uno
spostamento riuscito non rende lo spostamento fallito, ed è esattamente l'errore
del § 1.1.

### 4.2 `TargetEntity`

**Promessa.** Dopo il clic esiste un bersaglio, e `HasTarget` lo dice.

`ADR-0018` fissa già le tre uscite del lettore e vieta di collassarle in due:
`Present → true`, `Absent → false`, `Unreadable → UNKNOWN`. La post-condizione le
riusa senza aggiungere nulla: `d = 0` su `Present`, `d = 1` su `Absent`, e
`Unverified` su `Unreadable`, mai `d = 1`.

Il filo non stabilisce questa post-condizione e non deve provarci: `ct` non ha una
controparte osservata che azzeri il bersaglio, e `PROTOCOLLO_NOSTALE.md` registra
perché un flag derivato da lì sarebbe appiccicoso. Entra solo come **contraddizione**,
alla condizione dell'`ADR-0018`.

**Stato.** Finché la ROI del riquadro non è calibrata su un client reale, questa
azione ricade sotto `VER-08`: post-condizione interamente cieca, quindi non
eseguibile. Non è una restrizione nuova — `ADR-0016` già salta ogni regola che legge
un `HasTarget` sconosciuto — ma qui il motivo è dichiarato dalla parte dell'esito
invece che da quella della pianificazione.

### 4.3 `UseBasicAttack`

**Promessa.** La vita del **bersaglio** diminuisce, o il bersaglio muore.

```
min_finestra(HpRatio del bersaglio) < HpRatio all'emissione     ->  d = 0
un `die` per quell'entity id nella finestra                     ->  d = 0
nessuna delle due, con almeno un avvistamento del bersaglio     ->  d = 1
nessun avvistamento del bersaglio nella finestra                ->  Unverified
```

`VER-07` in forma concreta: il soggetto è l'entity id del bersaglio, mai il
personaggio. L'avvistamento porta `HpRatio` nullable, e un avvistamento senza vita —
il caso di gran lunga più frequente, perché 7 685 pacchetti su 8 211 sono `mv` e `mv`
non porta salute — **non** è una vita invariata: è nessuna lettura, quindi
`Unverified`.

**La finestra va misurata, non dichiarata.** I 1200 ms della tabella sono un numero
scritto qui per avere un valore, e il modo di sostituirlo è già a disposizione:
`data/nostale_combat.noscap` contiene gli `su` reali di una sessione, e l'intervallo
fra due `su` con il giocatore come attaccante è la cadenza vera. Finché non è
misurata, la finestra è dichiarata tale.

### 4.4 `UseSkill`

**Promessa, in due metà.**

1. `min_finestra(MP) < MP all'emissione` → la skill è partita. **Osservabile** da
   `stat`, entro i 250 ms che `P7` dichiara.
2. La skill è entrata in cooldown. **Non osservabile**: `sr`, che il catalogo del
   protocollo registra come « skill ready / cooldown ended, by skill slot », **non è
   fra gli opcode letti dal decodificatore** — `NosTaleWorldProtocolDecoder` ne legge
   sette: `stat`, `st`, `in`, `mv`, `die`, `su`, `cond`.

Sotto `VER-08` l'azione resta eseguibile perché la prima metà basta a distinguere una
skill partita da un tasto premuto a vuoto. Sotto `VER-05` la seconda metà **non si dà
per soddisfatta**: il verdetto porta `d` dalla prima metà e la ragione
`cooldown_not_observable`, e il pianificatore non può dedurre da un `Confirmed` che la
skill sia ora in ricarica.

**Il divieto che ne discende.** Nessun rinvio automatico dal backend
(`CONTROLLO_PERSONAGGIO_ROADMAP.md`, `P7`): con il cooldown cieco, un tentativo
ripetuto non è un ritentativo — è una seconda azione su uno stato ignoto.

### 4.5 `UseConsumable`

**Promessa.** `max_finestra(HP) > HP all'emissione`, oppure lo stesso sugli `MP`.

È la scheda che paga `VER-09` per intero. Il verso è ciò che si controlla, la
quantità no: la resa della pozione è un dato del catalogo non decodificato, e
pretenderla esatta è il difetto del § 1.3.

**Il caso ambiguo, dichiarato.** Nessun massimo superiore al valore d'emissione ha due
cause indistinguibili con questa sola sorgente: lo slot era vuoto, oppure la cura è
stata interamente annullata da colpi dentro la stessa finestra. L'esito è `Discrepant`
con ragione `heal_not_observed_ambiguous`, e la distinzione è ciò che arriva con
`ivn` (§ 6). Nominare l'ambiguità è preferibile a scegliere per essa.

### 4.6 `CollectGroundItem`

Nessun gesto: `InputActionEffector` la rifiuta per nome. La post-condizione naturale è
« lo slot d'inventario è aumentato per il vnum raccolto », e nessuna delle sue due
sorgenti — `get`, `ivn` — è decodificata.

**Non eseguibile** per `VER-08`. Il rifiuto per nome resta il comportamento corretto.

### 4.7 `RestAndRecover`

Nessun gesto. La post-condizione è l'unica del catalogo che richiede **monotonia**
sull'intera finestra invece di un singolo verso: una risalita interrotta da un colpo
non è un riposo riuscito, perché il riposo si interrompe davvero quando si viene
colpiti. Va scritta quando esisterà un gesto, e non prima.

**Non eseguibile** per `VER-08`.

### 4.8 `EmergencyFlee`

**Promessa.** La distanza dall'ostile più vicino osservato aumenta.

```
d = clamp( 1 - (distanza_dopo - distanza_prima) / celle_previste, 0, 1 )
```

**La regola che questa scheda aggiunge alle altre.** Una fuga non verificata **non si
ripete**. Le altre azioni possono ricadere in `Replan`; questa no, perché il caso in
cui la verifica fallisce è per costruzione il caso in cui la situazione è peggiore, e
un ciclo di fughe non verificate è il comportamento che il circuito di recovery
(`RecoveryCircuitBreakerTests`) esiste per fermare. Una fuga `Discrepant` o
`Unverified` sale direttamente a `HardStop` con allarme all'operatore.

---

## 5. Divergenza, esiti e conseguenze

Le soglie sono quelle di `ROADMAP_ESECUTIVA.md` § 8.3, e non vengono ridefinite qui.
Ciò che manca — e che questo documento aggiunge — è il rapporto fra quelle soglie e i
quattro esiti che Gate 3 già produce.

| Divergenza | Azione successiva | `VerificationOutcome` di Gate 3 |
|---|---|---|
| `d < 0.15` | `Continue` | `Confirmed` |
| `0.15 ≤ d < 0.40` | `Replan` | `Discrepant` |
| `0.40 ≤ d < 0.70` | `Quarantine` | `Discrepant` |
| `d ≥ 0.70` | `HardStop` | `Discrepant` |
| non calcolabile | `Replan`, **mai** `Continue` | `Unverified` |
| azione non eseguita | nessuna | `NotExecuted` |

Oggi `DiscrepancyScore` assume tre soli valori — `0.0`, `0.45`, `1.0` — perché nulla
calcola una divergenza vera. Le schede del § 4 sono ciò che la rende calcolabile.

**Effetto sul Trust.** Invariato rispetto al Gate 7: tre verifiche fallite entro 60 s
portano a `Quarantined`; nessun esito di verifica, per quanto positivo, promuove
(`INV-06`). Un `Unverified` **non** conta fra le tre: contarlo significherebbe punire
il sistema per una sorgente assente, e `VerificationResult.CountsAsFailure` già lo
esclude.

---

## 6. Ciò che oggi non è osservabile, e che cosa lo chiude

Ogni riga « cieca » o « parziale » del § 3.2 finisce qui. Nessuna richiede una
sorgente nuova: tutte richiedono di leggere qualcosa che è già sul filo o già nel
client.

| Ciò che manca | Perché manca | Che cosa lo chiude | Chi ne beneficia |
|---|---|---|---|
| Cooldown delle skill | `sr` non è fra i sette opcode decodificati | aggiungere `sr` a `NosTaleWorldProtocolDecoder`, con la stessa disciplina di confidenza del catalogo | `UseSkill` (seconda metà) |
| Inventario | `ivn`, `get`, `drop` catalogati e non pubblicati | pubblicarli sull'osservazione, come è stato fatto per `maxMp` | `CollectGroundItem`, `UseConsumable` (disambiguazione) |
| Riquadro bersaglio | ROI `TargetHpBar` mai calibrata su client reale | la calibrazione che `ADR-0018` già impone come precondizione | `TargetEntity`, e con essa ogni regola d'attacco |
| Posizione propria | il server non la manda mai: è autoritativa del client | il lettore di memoria, già provato per l'id mappa il 2 settembre | `MoveToPosition`, `EmergencyFlee` |
| Cadenza d'attacco | mai misurata | contare gli `su` con il giocatore attaccante in `data/nostale_combat.noscap` | `UseBasicAttack` (finestra) |

Le prime due righe sono le più economiche del progetto in rapporto a ciò che
sbloccano: sono due opcode già catalogati, con la loro forma già scritta in
`PROTOCOLLO_NOSTALE.md`, su un decodificatore che ne legge già sette.

---

## 7. Il contratto minimo

La forma che le schede del § 4 assumono in codice. Riusa i tipi che esistono —
`ActionType`, `ActionCandidate`, `Gate3WorldState`, `VerificationOutcome` — e non ne
introduce di paralleli.

```csharp
namespace NosAi.Runtime.Gate3;

/// <summary>Che cosa un'azione promette, e come lo si controlla.</summary>
public interface IPostCondition
{
    ActionType Action { get; }

    /// <summary>La finestra dell'azione, non del ciclo (VER-06).</summary>
    TimeSpan Window { get; }

    PostConditionVerdict Evaluate(in PostConditionInput input);
}

/// <param name="States">
/// La serie osservata nella finestra, non i due estremi (VER-09). Ogni elemento
/// porta gia' la provenienza e l'istante di osservazione dei propri campi.
/// </param>
public readonly record struct PostConditionInput(
    ActionCandidate Candidate,
    DateTime DispatchedAtUtc,
    IReadOnlyList<Gate3WorldState> States,
    IReadOnlyList<SelectableEntity> Sightings);

/// <param name="Divergence">
/// In [0,1]. Significativa solo quando l'esito e' Confirmed o Discrepant:
/// su Unverified non esiste una distanza da misurare.
/// </param>
public readonly record struct PostConditionVerdict(
    VerificationOutcome Outcome,
    float Divergence,
    string Reason);

/// <summary>Le post-condizioni, indicizzate per tipo di azione.</summary>
/// <remarks>
/// Un array indicizzato da ActionType (byte), popolato all'avvio: una voce assente
/// e' un rifiuto per nome, mai un'azione senza verifica.
/// </remarks>
public sealed class PostConditionTable
{
    public bool TryGet(ActionType action, out IPostCondition postCondition);
}
```

Due proprietà che i test devono fissare, perché sono l'intero valore della tabella:

1. **Nessuna azione esegue senza una voce.** Una `ActionType` senza post-condizione
   viene rifiutata all'ammissione, non eseguita e poi non verificata.
2. **`Evaluate` non legge la previsione.** La firma non la riceve. È `VER-01` reso
   impossibile da violare invece che raccomandato.

---

## 8. Che cosa questo documento non decide

- **Dove vive la tabella.** Deciso da `ADR-0020` (*proposto*, 2 settembre 2026): la
  tabella sta in `Gate3`, accanto a `ActionExecutionVerifier`, e non in `NosAi.Core`
  accanto a `IVerifier` e `ExecutionToken` — tipi che `ROADMAP_ESECUTIVA.md` § 8.2
  dichiara e che nel repository non esistono. Il § 7 di questo documento è già scritto
  in quel namespace. Finché l'ADR non è accettato, quella collocazione è una proposta,
  non un fatto.
- **L'ordine e la composizione delle guardie** all'istante dell'atto: è `C-P4`.
- **Il canale di attuazione**: deciso, `ADR-0019`.
- **Quali azioni appartengono alla 1.0 Beta.** Il catalogo descrive le otto che
  esistono nel tipo; non promuove `CollectGroundItem` e `RestAndRecover` a lavoro
  pianificato.

---

## 9. Come si aggiunge un'azione

Sette punti, nell'ordine. Un'azione che non li completa tutti resta un valore
dell'enum senza effettore, che è uno stato legittimo e dichiarato.

1. Il **bersaglio ammesso**, aggiunto a `ActionCandidate.RequireTarget`: la coppia
   sbagliata deve diventare non costruibile, non un errore a runtime.
2. Le **precondizioni osservabili**, con la sorgente di ciascuna e il motivo di
   rifiuto per nome quando manca.
3. Il **gesto**, in `InputActionEffector`, o un rifiuto per nome se non esiste
   ancora.
4. La **post-condizione**, come verso e limite (`VER-02`), sul soggetto giusto
   (`VER-07`).
5. La **finestra**, dichiarata o misurata — e detto quale delle due.
6. La **formula di divergenza**, normalizzata in `[0,1]`.
7. I **test negativi**: uno per precondizione, più il caso « eseguita e non
   osservabile », che deve produrre `Unverified` e non un successo.
