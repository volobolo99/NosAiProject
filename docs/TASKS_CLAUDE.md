# Schede di lavoro — Claude

**Obiettivo di riferimento:** `docs/OBIETTIVO_CONTROLLO_GIOCO.md`
**Coda di Cursor, da tenere sincronizzata:** `docs/TASKS_CURSOR.md`

Queste schede tengono le decisioni di progetto già prese, così che riprendere il
lavoro non voglia dire ridecidere. Ogni scheda dichiara la scelta e la ragione
della scelta: se la ragione non regge più, si cambia la scelta e si scrive perché.

**Comandi di verifica** (mai `NosAi.sln`: fallisce sul packaging Android per un
blocco file, ed è rumore che non riguarda il runtime):

```bash
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test  tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test  tests/NosAi.ControlPanel.Tests/NosAi.ControlPanel.Tests.csproj -c Release
```

Baseline all'apertura: **648** verdi nel runtime, **43** nel Control Panel.

## Artefatti attesi — come si verifica da fuori

`scripts/verifica-obiettivo.ps1` cerca questi nomi per sapere quali schede sono
chiuse. Sono vincolanti alla lettera: un file equivalente con un altro nome
lascia la scheda aperta agli occhi di chiunque non abbia seguito il lavoro.

| Scheda | Artefatto cercato |
|---|---|
| F0-1 | `.cursorrules` **senza** «must not hook into the target process memory» e **senza** «tactical decision and simulation layers: Python» |
| F1-1 | `double? HpRatio` in `GameTrafficObserver.cs` |
| F1-8 | `docs/adr/ADR-0018-establishing-the-target.md` |
| F1-10 | `src/NosAi.Runtime/LiveIntegration/MemoryGameplayProvider.cs` |
| F2-1 | **nessuna** occorrenza di `TARGET_MOB_01`, `WAYPOINT_A`, `ITEM_POTION_HP` in `src/` |
| F2-3 | `src/NosAi.Runtime/Perception/ScreenProjection.cs` |
| F3-1 | `src/NosAi.Runtime/Gate3/InputActionEffector.cs` |
| F4-1b | `maxMp` in `GameplayObservation.ToWire()` |

Lo script dice **presente** o **assente**, mai `VERIFIED`. Un file che esiste non
è una funzione che funziona: è la distinzione su cui il modello di stato del
progetto è costruito, e ADR-0004 la richiede.

---

## F0-1 — Allineare `.cursorrules` ad ADR-0014 e al C# reale

**Priorità: prima di tutto il resto.** È il lavoro più economico della lista e
sblocca ogni scheda di Cursor.

Due affermazioni del file sono false oggi:

- **§3, "Black-Box Supervision":** *«The framework must not hook into the target
  process memory. It operates purely as a black-box supervisor via vision and
  keyboard/mouse simulation»*. ADR-0014 ha revocato questo divieto e ha stabilito
  che la scelta della sorgente è dell'operatore. La lettura di memoria è **la**
  strada scelta per la posizione propria (B2): il wire non la porta e la
  minimappa dà un rapporto, non una coordinata.
- **§5, "Implementation Language":** *«Perception, tactical decision and
  simulation layers: Python»*. Percezione e decisione sono C#:
  `src/NosAi.Runtime/Perception/`, `src/NosAi.Runtime/Gate3/`. Python nel
  repository copre altro.

Perché conta: Cursor legge `.cursorrules` a ogni richiesta. Finché resta così,
o rifiuta il lavoro su F1-9 e F1-10, o lo scrive nel linguaggio sbagliato.

**Da fare.** Riscrivere §3 e §5 in modo che dicano ciò che gli ADR hanno deciso.
Ciò che **non** cambia, e va riaffermato nel testo perché è la sostanza che
ADR-0012 proteggeva: ogni sorgente porta il proprio controllo di validità; una
lettura che fallisce il controllo è `UNKNOWN` e mai l'ultimo valore buono; nessuna
sorgente acquista il diritto di agire; l'elusione dei sistemi di rilevamento resta
fuori (ADR-0014, *Detection evasion*), e questo va detto per nome perché è la sola
tecnica esclusa e non una categoria riaperta.

Verificare anche `.cursor/rules/25-connection-and-ban-risk.mdc`: ADR-0014 ne
prevede la riscrittura fra le proprie conseguenze. Se è già allineato, dirlo nel
commit; se no, allinearlo qui.

**Commit:** `docs(rules): let the rules say what ADR-0014 decided`

---

## F1-1 — `EntitySighting` ammette la posizione senza la salute

**Sblocca C2 di Cursor.** Farla per prima fra le schede di percezione.

`EntitySighting(long EntityId, string Kind, double X, double Y, double HpRatio,
DataSourceKind Source)` non ha modo di dire "so dov'è, non so come sta", e quindi
7685 `mv` su 8211 pacchetti finiscono scartati.

**Decisione:** `double HpRatio` → `double? HpRatio`. Non un valore sentinella,
non un `bool HasHp` accanto: un campo che può essere assente è la cosa che
`ClassifiedValue` afferma ovunque nel progetto, e un `-1` sarebbe la salute
inventata che si vuole evitare, con un travestimento.

**Da controllare, perché è dove la modifica può fare danno:**
`EntitySighting.ToDetection()` costruisce `new Detection(Kind, X, Y, HpRatio)`, e
`Detection` è `readonly record struct Detection(string Kind, double X, double Y,
double HpRatio)` in `PerceptionPipeline.cs` — condivisa con la percezione visiva,
dove la salute è sempre nota. Un avvistamento senza salute **non deve** diventare
una `Detection` con salute zero: un modello del mondo che vede un mob a zero HP lo
considera morto.

Scelta: `ToDetection()` restituisce `Detection?` e chi chiama gestisce l'assenza.
Essendo `Detection` uno struct, il nullable è naturale e la percezione visiva non
viene toccata — la sorgente che ha sempre la salute continua a produrre sempre un
valore.

Aggiornare i consumatori: `NetworkWorldFeed.ToDecisionContext`, `GameTrafficObserver`,
il modello del mondo. Cercare `HpRatio` in tutto `src/` prima di dichiarare finito.

**Test:** un avvistamento senza salute non produce mai una detection a `0.0`;
i test esistenti sulle sighting con salute passano invariati.

**Commit:** `feat(perception): let a sighting say where without saying how healthy`

---

## F1-8 — Stabilire `HasTarget`, e l'ADR che lo motiva

**Dipende da:** C1 di Cursor (`TargetFrameReader`).
**È il buco numero uno:** senza questo fatto, ADR-0016 salta ogni regola d'attacco
e il combattimento non esiste.

### Il problema, per intero

Nessuna delle due sorgenti basta da sola.

- **Il wire** ha `ct` (targeting fra due entità, 108 occorrenze) e `su` (ogni
  colpo), ma **nessun contrario osservato**: non esiste un pacchetto "bersaglio
  annullato". Un flag ricavato da `ct` resterebbe `true` per sempre, e niente sul
  wire lo correggerebbe. È scritto in `docs/PROTOCOLLO_NOSTALE.md` ed è la ragione
  per cui il campo è rimasto `UNKNOWN`.
- **Lo schermo** ha il contrario — il riquadro sparisce — ma la ROI `TargetHpBar`
  in `RoiSegmenter.Segment` (`0.40, 0.06, 0.20, 0.02`) non è mai stata calibrata
  su un client reale. Solo la `PlayerHpBar` lo è, con la prova T-03.

### La decisione da prendere nell'ADR-0018

**Lo schermo stabilisce il fatto; il wire lo conferma e non lo crea.**

- `TargetFrameState.Present` → `HasTarget = true`, `DERIVED`.
- `TargetFrameState.Absent` → `HasTarget = false`, `DERIVED`.
- `TargetFrameState.Unreadable` → `HasTarget` **`UNKNOWN`**, con il motivo del
  lettore. Mai `false`: ADR-0016 manderebbe il personaggio verso un waypoint
  durante un combattimento, ed è il caso preciso che quell'ADR esiste per impedire.

Il wire entra solo come **contraddizione**: un `su` in cui il giocatore è
l'attaccante, più recente della lettura dello schermo, mentre lo schermo dice
`Absent`, significa che le due sorgenti non concordano. In quel caso il risultato
è `UNKNOWN` con motivo `target_sources_disagree`, non la scelta di una delle due.

Questa è la stessa logica di ADR-0017 al contrario: là il wire insegnava allo
schermo perché era la sorgente più forte; qui lo schermo stabilisce perché è
l'unica che sa dire *no*, e il wire vale come controllo perché è indipendente.

### Precondizione operativa, da non saltare

Prima che il lettore valga qualcosa, la ROI va **calibrata su un ritaglio reale**
con un bersaglio selezionato. Riusare il percorso di `HudCropWriter` e `--hud-probe`
già usato per T-03. Finché la calibrazione non esiste, `HasTarget` deve restare
`UNKNOWN`: un riquadro letto nel posto sbagliato produce un `false` sicuro di sé,
che è il peggiore dei tre esiti possibili.

**Da fare:** ADR-0018; il compositore che unisce le due sorgenti; il collegamento a
`GameplayObservation.HasTarget`; la calibrazione della ROI con l'operatore.

**Commit:** `feat(perception): establish the target from the screen, checked against the wire`

---

## F1-9 — Trovare gli offset della posizione propria

**Richiede il client in esecuzione. Va fatta con l'operatore, non da soli.**

Il metodo è già costruito, in tre comandi di `NosAi.Runtime`: `--memory-scan`
apre l'insieme dei candidati, `--memory-narrow` lo restringe a ogni passaggio
successivo, `--memory-dump` legge le parole attorno a un indirizzo trovato. Un
solo passaggio non prova niente: un indirizzo si identifica sopravvivendo a più
restringimenti attraverso cambiamenti che l'operatore provoca in gioco. L'insieme
dei candidati persiste fra un'invocazione e l'altra in
`data/memory_scan_candidates.txt`, perché è il metodo a richiederlo.

**Procedura:** l'operatore legge le proprie coordinate sull'interfaccia del gioco
→ `--memory-scan` su quel valore → si sposta di qualche passo →
`--memory-narrow` sul nuovo valore → ripetere finché i candidati non sono pochi →
`--memory-dump` sull'indirizzo di `x`: `y` è quasi certamente a una o due parole
di distanza, com'è per i vitali.

**Esito da registrare** in `docs/GATE1_CHECKLIST.md` come prova reale: gli offset
trovati, il modulo base a cui sono relativi, e **quante volte** hanno superato un
riavvio del client. Un offset che non è stato riverificato dopo un riavvio non è un
offset: è un indirizzo che ha funzionato una volta.

---

## F1-10 — `MemoryGameplayProvider` con controllo di validità

**Dipende da:** F1-9.

`ProcessMemoryReader.ReadValidatedInt32(IntPtr, Func<int,bool>, DateTime)` esiste
già e restituisce un `ClassifiedValue<int?>`. Il provider lo usa; il lavoro vero è
**il predicato di validità**, perché è ciò che separa `LIVE` da una bugia
plausibile. ADR-0014 è esplicito: senza un controllo del genere, l'offset potrebbe
essersi spostato e la lettura è `UNKNOWN`.

Tre controlli, tutti e tre necessari:

1. **Intervallo.** Le coordinate delle mappe NosTale stanno in un intervallo
   ristretto — le catture mostrano valori a due e tre cifre (`121 110`, `109 63`).
   Fuori intervallo → `UNKNOWN`.
2. **Continuità.** Uno spostamento fra due letture consecutive maggiore di quanto
   la velocità consenta in quel tempo è un offset che si è spostato, non un
   personaggio che ha corso. La velocità arriva da `cond` (scheda C6 di Cursor).
3. **Coerenza con la mappa.** Se la mappa corrente è nota, una coordinata fuori dai
   suoi limiti è `UNKNOWN`.

**La classificazione:** `LIVE` mentre i tre controlli passano; `UNKNOWN` con il
motivo del controllo fallito appena uno cede. **Mai l'ultimo valore buono**, che è
il caso nominato per esteso in ADR-0014.

**Commit:** `feat(memory): read the player's own position, and check it is still true`

---

## F2-1 — `ActionCandidate` porta un bersaglio tipizzato

`ActionCandidate(Guid, ActionType, string TargetId, int TargetX, int TargetY,
int SkillOrItemId, TrustTier, string Rationale)` porta oggi `"TARGET_MOB_01"`,
`"WAYPOINT_A"`, `"ITEM_POTION_HP"` con coordinate costanti `125, 85` e `130, 90`.
Sono stringhe che non corrispondono a niente. Un effector collegato adesso
eseguirebbe azioni su bersagli che non esistono.

**Decisione:** un tipo somma per il bersaglio — un'entità con il suo `EntityId`
reale, una posizione di mappa, uno slot di inventario, oppure nessuno — invece di
una stringa più due interi che ogni chiamante interpreta a modo suo. La stringa
può contenere qualsiasi cosa e nulla la controlla; un tipo rende impossibile
costruire un candidato d'attacco senza un bersaglio.

`ActionCandidate` è in `src/NosAi.Runtime/Autonomy/AutonomyPipeline.cs` ed è
consumato da simulazione, ranking, Guard, Safety e dai runner di certificazione:
la modifica va fatta in un passaggio solo e va compilata contro tutti.

**Attenzione al Safety Gate:** il token HMAC è legato al candidato. Se la firma
copre i campi del bersaglio, cambiarne la forma cambia ciò che viene firmato.
Verificare cosa entra nel calcolo del token **prima** di toccare il record, e
tenere i test sul riuso e sulla contraffazione del token come rete: se passano
ancora dopo la modifica, il legame regge.

**Commit:** `refactor(autonomy): give an action a target that exists`

---

## F2-2 — Il planner sceglie il bersaglio reale più vicino

**Dipende da:** F2-1 e da C2 di Cursor.

Con le posizioni dei mob osservate (C2) e la posizione propria (F1-10), le regole
d'attacco possono puntare a un'entità vera invece che a `"TARGET_MOB_01"`.

**Regola di selezione, e la sua condizione di rifiuto:** il più vicino fra gli
avvistamenti la cui posizione è fresca secondo il limite dell'orchestratore
(2 secondi per difetto, ADR-0016). Se non ci sono avvistamenti freschi, la regola
d'attacco **non pianifica** — non si attacca il più vicino fra i ricordi. Un mob
visto tre secondi fa può essere morto, o essere altrove.

Se manca la posizione propria, "più vicino" non è calcolabile: la regola salta,
esattamente come salta su `HasTarget` ignoto. È lo stesso principio di ADR-0016,
applicato a un fatto nuovo.

**Commit:** `feat(gate3): aim at an entity the runtime has actually seen`

---

## F2-3 — Coordinate di gioco → pixel della finestra

**Dipende da:** F1-10. **È la scheda in cui un errore fa cliccare nel posto sbagliato.**

Muovere e attaccare significano cliccare in un punto della finestra. Serve la
trasformazione da `(x, y)` di mappa al pixel corrispondente, e va **calibrata**,
non dedotta: dipende dalla risoluzione, dallo zoom e dalla proiezione isometrica
del client.

**Decisione:** calibrazione a due punti fatta dall'operatore — si registra la
posizione del personaggio, ci si sposta di una distanza nota, si registra di
nuovo. Da due coppie `(mappa, schermo)` si ricava la trasformazione affine.
Persistere in `data/perception/` accanto all'atlante dei glifi, **non nel
repository**: come l'atlante, è specifica di una macchina, di una risoluzione e
di uno schermo (ADR-0017 lo argomenta per l'atlante e vale identico qui).

**Il rifiuto che conta:** senza calibrazione, la conversione restituisce *non lo
so*, e l'effector rifiuta ogni azione che richieda un punto sullo schermo. Una
trasformazione di ripiego produrrebbe un clic in un punto qualsiasi della
finestra, che è peggio del non cliccare — e il ciclo lo scoprirebbe solo alla
verifica, dopo aver già agito.

Aggiungere un controllo di dominio: un punto calcolato fuori dall'area client
è un rifiuto, non un clic sui bordi.

**Commit:** `feat(perception): turn a map coordinate into a pixel, once calibrated`

---

## F3-1 — `InputActionEffector`: il collegamento mancante

**Dipende da:** F2-1, F2-2, F2-3, C3 di Cursor. **È B4, il pezzo che manca.**

Esistono `Win32InputBackend` (SendInput reale), `GatedInputBackend` (rifiuta
fail-closed finché la policy non apre) e l'interfaccia `IActionEffector`. Non
esiste nulla che traduca un `ActionCandidate` in un gesto.

**Traduzione:**

| `ActionType` | Gesto | Serve |
|---|---|---|
| `UseConsumable` | tasto dello slot pozione | `KeybindMap` |
| `UseSkill` | tasto dello slot skill | `KeybindMap` + slot pronto (`sr`, C5) |
| `UseBasicAttack` | clic sul bersaglio | posizione del bersaglio + F2-3 |
| `MoveToPosition`, `EmergencyFlee` | clic sul punto di destinazione | F2-3 |
| `TargetEntity` | clic sull'entità | posizione del bersaglio + F2-3 |
| `CollectGroundItem`, `RestAndRecover` | non implementati | rifiuto nominato |

**Le tre regole non negoziabili.**

1. **`Completed` solo se l'input è stato davvero accettato.** `SendInput`
   restituisce quanti eventi ha accodato: se non è quello atteso, l'esito è
   `Failed` con motivo. Il difetto che ha reso Gate 3 incapace di dire la verità
   era esattamente un `Completed` senza esecuzione, ed è documentato in
   `docs/GATE3_PIPELINE.md`. Non reintrodurlo da questo lato.
2. **Ogni ingrediente mancante è un rifiuto nominato, mai un valore di ripiego.**
   Keybind non configurato → `Refused("keybind_not_configured:<intent>")`.
   Nessuna calibrazione → `Refused("screen_projection_not_calibrated")`.
   Bersaglio senza posizione → `Refused("target_position_unknown")`.
3. **L'input passa da `GatedInputBackend`, mai da `Win32InputBackend` diretto.**
   Il cancello sta al confine proprio perché non lo si possa scavalcare (ADR-0003).
   Un effector che prende il backend concreto scavalca il Safety Gate: è la
   ragione per cui `GatedInputBackend` è stato scritto.

**Da collegare:** `ActionEffectorFactory.ForPolicy(policy, liveEffector)` accetta
già l'effector reale come parametro. Il collegamento è nella composizione del
runtime, e resta spento finché l'operatore non accende `LiveInputEnabled`.

**Test:** con `RecordingInputBackend` si verifica quali gesti sarebbero partiti
senza toccare il desktop. Un test per ogni rifiuto nominato; un test che verifica
che con la policy chiusa non arriva **nulla** al backend.

**Commit:** `feat(gate3): apply an authorised action to the real client`

---

## F4-1b — Pubblicare gli MP massimi sullo snapshot

**Dipende da:** C7 di Cursor.

C7 fa arrivare `MaxMp` fino a `PlayerVitals`. Pubblicarlo su
`GameplayObservation.ToWire()` è una modifica al contratto `gate1.snapshot.v1`,
letto anche dal telefono: additiva, quindi compatibile, ma va trattata come una
modifica di protocollo (ADR-0005) — versione controllata, e un lettore vecchio
che ignora il campo nuovo deve continuare a funzionare. Verificarlo con il client
Guard, non solo con i test.

---

## F5-1 — T-05: conferma `LIVE` su una sessione in corso

**Con l'operatore. È la prova che manca da più tempo.**

Il percorso è costruito e attivabile: `--observe-game <host:porta>`,
`NOSAI_OBSERVE_GAME`, o l'impostazione nel Control Panel. Il recording di
combattimento legge già gli stessi HP/MP dell'HUD. Quel che manca è la conferma
su una **sessione accesa**, non su un replay.

Da registrare in `docs/GATE1_CHECKLIST.md`: HP letti dal runtime accanto agli HP
sull'HUD, nello stesso istante, con l'orario. Un replay è `CACHED` per costruzione
e non chiude T-05.

---

## F5-2 — Le tre sequenze reali

**Con l'operatore. Sono A5, A6 e A7 di `docs/OBIETTIVO_CONTROLLO_GIOCO.md`.**

Nell'ordine, dalla meno reversibile alla più:

1. **Pozione.** HP sotto soglia → il ciclo pianifica `UseConsumable` → l'HP
   osservato risale → `Confirmed`. La prima, perché non muove il personaggio e non
   coinvolge nessun'altra entità.
2. **Spostamento.** Destinazione richiesta → clic → la posizione osservata
   converge → `Confirmed`. Verifica insieme F2-3 e F1-10, ed è dove una
   calibrazione sbagliata si manifesta subito.
3. **Attacco base.** Bersaglio presente → clic → l'HP del bersaglio scende sul
   `su` → `Confirmed`. Ultima, perché è la sola con una controparte che reagisce.

Per ciascuna, registrare l'esito del ciclo, l'età dell'osservazione al momento
della decisione, e la discrepanza misurata dal verifier. **Un `Unverified` non è
un successo**, e non va riportato come tale: è il difetto che Gate 3 ha già
corretto una volta.

---

## Decisione — `GameEventKind` non viene esteso (1 settembre 2026)

Cursor si è fermato su C5 e C6 chiedendo quale valore di `GameEventKind` usare per
`sr` e per `cond`. Le schede gli dicevano di fermarsi lì, e ha fatto bene. La
risposta è che l'enum non c'entra: la domanda era la spia di due schede scritte
male, non di un buco nel contratto.

`GameEvent(GameEventKind Kind, long EntityId, string Descriptor, DataSourceKind
Source)` è fatto per *«è successo qualcosa a un'entità»*. Il `Descriptor` è
un'etichetta — nel codice esistente vale `"die"` e `"su"` — non un contenitore di
valori. Non c'è posto per un numero che non sia un id di entità, e infilarcelo in
una stringa lascerebbe ogni lettore a riparsarla.

Ma il difetto vero sta sopra la forma:

- **La velocità di `cond` non è un evento, è uno stato.** È una proprietà del
  personaggio nel tempo, come gli HP, e va in `PlayerVitals` — che porta già
  `HasTarget` e `InCombat`, ed è quindi già lo stato del personaggio sotto un nome
  stretto. C6 riscritta di conseguenza. Ha un consumatore reale: il controllo di
  continuità di F1-10.
- **`sr` non è leggibile, e nessun contenitore lo aggiusterebbe.** Dice quando una
  skill torna pronta; niente sul wire dice quando smette di esserlo. Un insieme di
  slot pronti partirebbe vuoto e crescerebbe soltanto, e nessun pacchetto lo
  correggerebbe — lo stesso identico difetto per cui `docs/PROTOCOLLO_NOSTALE.md`
  rifiuta di dedurre `HasTarget` da `ct`. **C5 ritirata**, con la condizione di
  riapertura scritta nella scheda.
- **`lev` non ha un lettore.** Sei campi in più su un contratto condiviso per un
  valore che nessuna regola consulta. **C8 rinviata**, per lo stesso principio con
  cui ADR-0016 ha smesso di far bloccare il ciclo su `InCombat`.

**Perché non un ADR.** Nessuna di queste tre cambia una decisione registrata: sono
l'applicazione di regole che ADR-0012, ADR-0014 e ADR-0016 hanno già preso, a tre
casi nuovi. Restano qui, dove chi riprende il lavoro le trova accanto alle schede
che governano.

**Se in futuro un opcode richiedesse davvero un evento nuovo** — uno con un vero
soggetto e un vero istante — allora `GameEventKind` si estende **in coda**, mai
rinumerando: `MessageSpec` in `ProtocolMap.cs` lo serializza per valore nelle
mappe ricostruite dall'operatore, e un valore spostato reinterpreterebbe una mappa
già scritta su disco.

---

## Debito noto, da non lasciare crescere

`TrustTier` è definito in `Contracts`, `Gate3`, `Gate6` e `Host`. `SafetyGate` in
`Gate3`, `Gate6` e `Safety`; `TrustBoundary`, `RuntimeMode` e `RecoveryController`
in `Gate3` e `Gate6`.

Compilano perché stanno in namespace diversi, ma qualunque file che ne importi due
diventa ambiguo, e **nulla impedisce alle definizioni di divergere su un confine
di sicurezza**. Non è cosmetica.

Non è nella coda perché unificarli tocca contratti condivisi e più gate insieme,
e va coordinato invece che fatto a metà — ed è il tipo di refactoring ampio che
`CLAUDE.md` vieta durante una milestone mirata. Resta qui perché F2-1 tocca
`ActionCandidate` e `TrustTier` insieme: se in quel passaggio la divergenza si
manifesta, si affronta lì, con la sua ragione scritta.
