# Piano delle capacità — da « Gate superato » a « gioca »

**Versione:** 1.0
**Data:** 2 settembre 2026
**Ruolo:** **operativo e prevalente sull'ordine dei lavori.** Dice *in che ordine* si
costruiscono le capacità e *chi* fa cosa. Non decide architettura: quella resta in
`NOSAI_ARCHITECTURE_BASELINE.md` e negli ADR, e questo piano non li contraddice.
**Sostituisce come ordine dei lavori:** `PIANO_OPERATIVO.md`, e le liste « cosa fare
adesso » sparse in `CONTROLLO_PERSONAGGIO_ROADMAP.md` e `SESSIONI_CURSOR.md`, che
restano validi per il *contenuto tecnico* delle loro tappe.

---

## 1. Che cos'è NosAiProject

Un runtime di automazione controllata per il client NosTale, su Windows e .NET 8.

Il suo scopo non è « un bot ». È un sistema che **osserva il gioco da sorgenti reali, si
costruisce un modello del mondo con la provenienza di ogni dato, decide in modo
deterministico, e agisce solo attraverso un confine che può rifiutare** — con un secondo
parere su un nodo mobile e un registro a catena di hash di tutto ciò che ha fatto.

Il percorso critico, che non si riordina:

```
Observe -> WorldState -> Simulation -> Ranking -> Orchestrator
        -> Planner -> Guard -> Trust -> Safety -> Execute -> Verify
```

Le regole da cui discende tutto il resto:

| | |
|---|---|
| Nessun LLM ha autorità di esecuzione | può proporre un candidato, non creare un atto |
| Fail-closed | timeout o anomalia **chiudono** |
| Sconosciuto non è zero | un dato non letto resta `UNKNOWN` e la regola che lo legge viene saltata |
| Ogni valore porta la sua provenienza | `LIVE`, `DERIVED`, `CACHED`, `SIMULATED`, `UNKNOWN` |
| Niente mock sul percorso critico | analyzer `NOSAI0001` |
| Si osserva su tre canali, si agisce su uno | filo e memoria osservano; **l'atto è input del sistema operativo** (`ADR-0019`) |

L'ultima riga è la scelta di fondo: un atto emesso come input passa dal codice del
client, quindi ogni rifiuto che il client già implementa — cella non calpestabile, skill
in cooldown, bersaglio fuori portata — resta in vigore gratis.

---

## 2. Perché ci si è persi, e la regola che lo impedisce

Non è colpa di una tappa sbagliata. Sono tre cause strutturali, e vanno chiuse qui.

**Cinque sistemi di numerazione per lo stesso lavoro.** `M001…M127` (master roadmap,
superata), `Gate 1…8` (roadmap esecutiva, canonica), `P0…P8` (controllo personaggio),
`S1…S5` (sessioni Cursor), `F1-9`, `F2-3`, `F4-1b` (schede), `T-01…T-11` (test
rimandati). Nessuno di questi è sbagliato. Insieme rendono impossibile rispondere a
« a che punto siamo ».

**Tre documenti che dicono cosa fare adesso.** `PIANO_OPERATIVO.md`,
`CONTROLLO_PERSONAGGIO_ROADMAP.md § 6`, `SESSIONI_CURSOR.md`. Divergono appena qualcuno
lavora.

**Più agenti sullo stesso file.** Il 2 settembre 2026 due sessioni hanno scritto la
stessa sezione `P4` a pochi minuti di distanza, e la seconda ha sovrascritto la prima.

### Le tre regole, da adesso

1. **Un solo asse.** Le capacità di questo documento — `C1…C6`. Le vecchie sigle
   restano come *riferimento* al contenuto tecnico; **non se ne conia più nessuna**.
2. **Un solo documento dice cosa fare adesso**, ed è il § 6 di questo. Gli altri
   descrivono il *come*, mai l'*ordine*.
3. **Un file, un agente.** Ogni lavoro del § 6 nomina i file che tocca. Due lavori che
   nominano lo stesso file non partono insieme. Chi finisce riporta; non aggiorna la
   tabella di stato di un altro.

---

## 3. Dove siamo davvero

Misurato il 2 settembre 2026 su `2499715` (`C2-7` compresa): `dotnet build -c Release`
→ **0 errori**, 1 warning preesistente (Android). `dotnet test` → **1651 test, 0
falliti** (1487 runtime, 66 core, 98 control panel). I conteggi precedenti erano 1591 e
prima 1322; le catture reali in `data/` sono ignorate da git, quindi i test che le
rigiocano passano a vuoto su un clone e portano il loro peso solo qui.

Il progetto non è in difficoltà tecnica. È in difficoltà di *ordine*: la quantità di
codice sano è alta, e ciò che manca sono pochi collegamenti e tre misure che solo
l'operatore può prendere.

| Capacità | Stato reale | Evidenza |
|---|---|---|
| Aggancio al client, canale col telefono, dashboard | **Verified** su sistema reale | Gate 1 chiuso, `GATE1_CHECKLIST.md` |
| Lettura dei vitali propri dal filo | **Verified** — HP, maxHP, MP, maxMP `LIVE` | `stat`, confrontato con l'HUD |
| Lettura delle entità dal filo | **arrivano al pianificatore** | il decoder legge dodici opcode (i sette di prima più `sr`, `ivn`, `get`, `drop`, `ct`) e il vnum di `in`; gli avvistamenti diventano `Gate3WorldState.Entities` con età e provenienza per campo, e la posizione propria resta `UNKNOWN` con il suo motivo finché un lettore non è legato |
| Griglie di mappa | **777 mappe estratte**, id mappa provato su 4 mappe e 1 riavvio | `MapIdModuleOffset = 0x38D1BC` |
| Posizione propria | **provata** — `T-11` chiuso il 1 settembre | firma di codice, non offset; l'id `3443217` letto dal client coincide con quello che il server ha mandato su `cond`. Richiede console **elevata**: il manifest del client dichiara `requireAdministrator` |
| Bersaglio (`HasTarget`) | **UNKNOWN** | non più per la ROI: `ADR-0021` sposta la sorgente sulla memoria del client, e il motivo è ora `target_offset_not_established`. L'oracolo `TargetIdFinder` è scritto; manca la passata sul client |
| Proiezione mappa→schermo | **non calibrata** | `T-10`: cinque tentativi, nessuno utilizzabile |
| Catena d'input e sue guardie | **scritta e testata in locale** | commit point a 5 condizioni, autorità di sessione, `StepGuardChain`, 33 test |
| Percorso: ammissione, rivalidazione, replan (`C2-7`) | **scritto e testato in locale** | un percorso è ammesso guardando **ogni** cella, non gli estremi, e ogni segmento è rivalidato prima di essere emesso; limite di 3 replan consecutivi, dove « consecutivi » significa senza avvicinarsi più di quanto ci si sia mai avvicinati. 24 test, 21 percorsi da ≥ 16 celle su 3 mappe, e la prova che un percorso attraverso una cella bloccata **non raggiunge affatto** il backend d'input |
| Jump Point Search | **misurato e non introdotto** | rivalidare ogni cella di un percorso costa meno che pianificarlo una volta: JPS ottimizzerebbe la metà già economica. Un test fallisce se il rapporto si inverte, così la decisione resta falsificabile |
| Emissione di un atto reale | **mai avvenuta** | `--step` non esiste ancora |
| Tasti (skill, pozioni, interfaccia) | **impossibili oggi** | `data/keybinds.json` **non esiste**: ogni pressione rifiuta con `keybind_not_configured:` |
| Verifica dell'atto | **catalogo implementato** (`C4-1`) | sei schede con la loro finestra e il loro soggetto; `RestAndRecover` non ha scheda e per questo non è ammissibile. `VER-01` è impossibile da violare per firma, `VER-04` chiuso: il tier di verifica non è più severo di quello di attuazione |
| Ciclo decisionale | **ha un motivo** (`C6-1`, `C6-2`, `C6-3`) | contrattacca chi l'ha colpito entro una finestra di decadimento; senza obiettivo attivo non sceglie nessun bersaglio e non cammina più verso il waypoint costante `(130, 90)`; attacca solo ciò che è stato **stabilito** attaccabile |
| Bersaglio su una sessione viva | **ancora `UNKNOWN`** | il tubo c'è, l'offset no. `TargetIdFinder` cerca in memoria dove il client tiene l'entità selezionata, usando la lista della scena come oracolo; finché una passata non lo stabilisce, `HasTarget` resta `UNKNOWN` e le regole che lo leggono restano saltate. Nessuna misura di pixel, nessuna dipendenza dalla risoluzione |

**Che cosa resta, dopo questa sessione.** Due dei tre tubi sono collegati: le entità
arrivano al pianificatore con la loro età, e il ciclo ha un motivo per attaccare e una
verifica che riguarda l'azione eseguita invece di una stringa. Con `C2-7` anche il
percorso è coperto in locale, e resta vero il limite che conta: **nessun atto reale è
mai stato emesso**, quindi tutto ciò che sta sopra è `Done` e nulla è `Verified`. Ciò
che manca non è più codice di scrivania:

- **la ROI del bersaglio** (`C1-6`) — venti minuti col client aperto, e sblocca ogni
  regola d'attacco che legge `HasTarget`;
- **i tasti** (`C3-1`) — `data/keybinds.json` non esiste, quindi ogni skill e ogni
  pozione rifiutano per nome;
- **la proiezione** (`C2-3`) — senza di essa nessun clic è emesso, quindi il
  contrattacco pianifica e non colpisce.

Le tre sono misure che solo l'operatore può prendere.

---

## 4. L'obiettivo, in sei capacità

Le parole sono quelle dell'operatore, tradotte in condizioni verificabili.

| | Capacità | È vera quando |
|---|---|---|
| `C1` | **Vede** | entità, posizione propria e bersaglio arrivano al `WorldState` con provenienza e età, e l'operatore lo vede su una sessione viva |
| `C2` | **Si muove** | il personaggio va da una cella a una cella adiacente, e il runtime lo **verifica** su griglia; poi un percorso di ≥ 15 celle |
| `C3` | **Usa mouse e tastiera** | i tasti dell'operatore sono dichiarati e usabili; apre l'inventario; ogni tasto non configurato rifiuta per nome |
| `C4` | **Colpisce** | sceglie un bersaglio osservato, attacca, e **verifica sul bersaglio** che la vita sia scesa; usa una skill e vede gli MP scendere |
| `C5` | **Raccoglie** | vede un oggetto a terra, lo raccoglie, e lo conferma sull'inventario |
| `C6` | **Ha un motivo** | **reagisce** a chi lo attacca; e **non attacca** se non ha un obiettivo attivo che lo giustifichi |

`C6` è la richiesta più importante e la meno ovvia, quindi va detta per esteso.

> **Reattivo:** essere colpiti è un fatto osservato — `su` con il giocatore come
> bersaglio. La risposta è contrattaccare **chi** ha colpito.
> **Proattivo:** attaccare senza essere stati attaccati richiede un **obiettivo
> attivo** che nomini cosa cercare. Nessun obiettivo, nessun attacco: non « a caso »
> non è una raccomandazione, è una regola che rifiuta.
> **Mai bloccato:** un attacco che non produce l'effetto atteso non si ripete
> all'infinito. È ciò che fanno le soglie di divergenza e il breaker di recovery,
> quando la post-condizione esiste.

---

## 5. I lavori

Ogni lavoro ha: **chi**, **cosa tocca**, **quando è finito**. « Operatore » significa
che serve il client vivo e una persona davanti.

### C1 — Vede

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C1-1` | **Collegare il tubo delle entità.** `EntitySighting` e la posizione propria devono popolare `Gate3WorldState.Entities` e `PlayerPosition`, che oggi nessuna sorgente riempie. Con l'età e la provenienza per campo | Claude | `Gate3/Gate3WorldState.cs` |
| `C1-2` | **Non perdere l'attaccante.** `DecodeHit` calcola `attackerId` e poi emette un evento che porta solo il bersaglio. Serve un fatto « sono stato colpito da *chi*, e quando » fino al `WorldState` | Claude | `Perception/Network/NosTaleWorldProtocolDecoder.cs` |
| `C1-3` | **Quattro opcode già catalogati.** `sr` (cooldown), `ivn`, `get`, `drop` (inventario). Il decoder ne legge sette; questi quattro hanno la forma già scritta in `PROTOCOLLO_NOSTALE.md` e sbloccano la verifica di skill e raccolta | Claude — stesso file di `C1-2` |
| `C1-8` | **Il vnum, che dice *che cosa* è un'entità.** `in` lo porta in `fields[2]` e il decoder lo salta: senza, un mostro e un mercante sono due id con una posizione. Il catalogo di riferimento ha già 2 705 mostri e `Lookup(kind, vnum)`. Vedi `TASTI_E_BERSAGLIO.md` § 5 | Claude — stessa sessione |
| `C1-9` | **`ct` decodificato**: dice *quale* entità è selezionata dopo un `F8`. 108 occorrenze catalogate, mai lette. Vedi `TASTI_E_BERSAGLIO.md` § 6.3 | Claude — stessa sessione | stesso decoder, `NetworkObservationContracts.cs` |
| `C1-4` | **Prova offline.** `--world-replay` sui `.noscap` esistenti stampa: entità distinte, quante con posizione, quante con vita, attaccanti risolti, eventi d'inventario. Nessun client, nessun rischio | Cursor | `Program.cs`, test |
| `C1-5` | **Confermare `LIVE` su sessione in corso** (`T-05`): il decodificatore è provato offline sui `.noscap` (62 letture, HP 7218..7305); manca l'accensione di `--observe-game <host:porta>` su una sessione vera. Control Panel **come amministratore** | operatore | — |
| `C1-6` | **Trovare il bersaglio in memoria** (`ADR-0021`, *proposto*): un oracolo sul modello di `MapIdFinder`. Una parola è candidata solo finché vale l'id che `ct` ha appena nominato; ogni `ct` successivo restringe; la morte dell'entità tenuta è l'evento di azzeramento **gratuito**, perché il client si toglie il bersaglio da solo. Il superstite è ancorato alla base del modulo. **Nessuna misura umana, nessuno screenshot, indipendente dalla risoluzione**, e sblocca ogni regola d'attacco | Claude scrive l'oracolo, l'operatore lancia una passata | `LiveIntegration/`, `Perception/Network/` |
| `C1-6b` | **Calibrare la ROI del bersaglio** (`T-09`) — declassato da precondizione a *irrobustimento*: resta la seconda sorgente indipendente che deve concordare, ma il combattimento non la aspetta più | operatore, quando conviene | `data/perception/` |
| `C1-7` | **Barra parziale e atlante dei glifi** (`T-03`, residuo): `--hud-probe` con la barra **non piena** — le due fixture esistenti sono entrambe a barra piena, quindi il bordo del riempimento non è mai stato misurato su una scanalatura vuota vera | operatore | `data/perception/crops/` |

**Fatto quando** — su una sessione viva, lo snapshot mostra almeno un'entità con
posizione e vita, la posizione propria, e `HasTarget` che passa da `UNKNOWN` a
`true`/`false` seguendo il riquadro.

### C2 — Si muove

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C2-1` | ~~**Autorità dell'atto sullo scope** (`ADR-0020`)~~ **fatto** il 2 settembre 2026 (`4e76eea`): `TryBeginActuation` riceve `ActuationAuthority`, `SingleStepExecutor` la porta, e i test la coprono | Claude | — |
| `C2-2` | **Cella su cui si sta** (`P1` DoD): `--grid-check` col client aperto; la cella sotto il personaggio deve risultare calpestabile. Falsifica la semantica dei bit in un campione | operatore | — |
| `C2-3` | **Calibrare la proiezione** (`T-10`): `--screen-autocalibrate --arm-input` in una zona **davvero aperta**, ≥ 15 celle libere in ogni direzione, nord compreso. I cinque tentativi falliti sono falliti per un ostacolo a nord | operatore | `data/perception/` |
| `C2-4` | `--step <dx> <dy>` con la stampa di ogni guardia e gli eventi di audit, ognuno con l'autorità dell'atto | Cursor (`S4`) | `Program.cs` |
| `C2-5` | **Le tre prove di `P2` e quella di `P3`** sul client vivo, prima di premere `--step` | operatore | — |
| `C2-6` | **I 100 passi.** DoD di `P4` | operatore | — |
| `C2-7` | Rivalidazione per segmento, politica di replan e limite ai replan consecutivi (`P5`) | Claude | `Navigation/` |
| `C2-8` | `--walk <gx> <gy>`, visualizzazione del percorso | Cursor | `Program.cs` |

**Fatto quando** — 20 percorsi da ≥ 15 celle su ≥ 3 mappe, e nessun input emesso per un
percorso che attraversa una cella bloccata.

### C3 — Usa mouse e tastiera

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C3-1` | **Il file dei tasti non esiste.** Serve `data/keybinds.json` con gli intenti dell'operatore, e un `--keybinds-check` che stampi quali intenti sono configurati e quali no. Finché manca, ogni skill e ogni pozione rifiutano | Cursor + operatore | `LowLevel/KeybindMap.cs`, `Program.cs` |
| `C3-2` | **Intenti d'interfaccia**, a partire da `ui.inventory`. Sono atti come gli altri: passano dal gate, dal commit point e dall'autorità | Claude | catalogo azioni |
| `C3-3` | **Post-condizione di un intento d'interfaccia**: aprire l'inventario si verifica sullo schermo, non sui vitali. Finché non c'è un lettore, l'esito è `Unverified` **dichiarato**, mai un successo | Claude | `CATALOGO_AZIONI_E_POSTCONDIZIONI.md` |

**Fatto quando** — `--keybinds-check` elenca gli intenti reali dell'operatore, e un
comando apre l'inventario sul client vivo con l'atto registrato e attribuito.

### C4 — Colpisce

Dipende da `C1-6` (ROI) e `C1-1` (entità). Senza quei due non è pianificabile, per
costruzione.

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C4-1` | **Implementare il catalogo**: `IPostCondition`, `PostConditionTable`, e le schede già scritte in `CATALOGO_AZIONI_E_POSTCONDIZIONI.md`. È ciò che rende un attacco verificabile **sul bersaglio** invece che sui propri HP | Claude | `Gate3/` |
| `C4-2` | Politica di selezione del bersaglio, criterio di irraggiungibilità e suo decadimento (`P6`) | Claude | `Autonomy/TargetSelector.cs` |
| `C4-3` | Precondizioni della skill: MP noti, bersaglio vivo e in raggio; **divieto di rinvio automatico** finché il cooldown non è osservabile | Claude | `Gate3/` |
| `C4-4` | Comandi e cinque test negativi, uno per precondizione (`P7`) | Cursor | test |

**Fatto quando** — 50 attacchi in cui la vita del bersaglio scende o il bersaglio muore,
verificati sull'osservazione; e una skill che fa scendere gli MP entro 250 ms.

### C5 — Raccoglie

Dipende da `C1-3` (`get`, `ivn`, `drop`).

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C5-1` | Gesto per `CollectGroundItem`, che oggi rifiuta con `action_not_implemented` | Cursor | `Gate3/InputActionEffector.cs` |
| `C5-2` | Post-condizione sull'inventario: lo slot del vnum raccolto aumenta. Non « l'oggetto è sparito dallo schermo » | Claude | catalogo |

**Fatto quando** — 50 raccolte verificate sull'inventario, non su un'ipotesi visiva.

### C6 — Ha un motivo

| ID | Lavoro | Chi | File |
|---|---|---|---|
| `C6-1` | **Regola reattiva**: colpito ⇒ il candidato è attaccare **l'attaccante**, con una finestra di decadimento (dopo N secondi senza colpi, l'aggressione non è più un motivo). Dipende da `C1-2` | Claude | `Gate3/Gate3Runtime.cs` (`ActionPlanner`) |
| `C6-2` | **`GoalStack` come precondizione dell'attacco proattivo**: nessun obiettivo attivo che nomini cosa cercare ⇒ **nessun candidato d'attacco**. Sostituisce il waypoint costante `(130, 90)` che oggi nessuno ha osservato | Claude | `Gate3/`, `Core/Planning` |
| `C6-3` | **Il primo obiettivo reale**, ancorato a ciò che si osserva: « uccidi N entità di vnum X » si misura con `die`; il progresso di livello con `lev` (catalogato, non ancora pubblicato) | Claude + Cursor | decoder, planner |
| `C6-4` | **Anti-attacco bloccato**: soglie di divergenza collegate al breaker di recovery, così un attacco che non produce effetto degrada invece di ripetersi. È `C4-1` più il cablaggio | Claude | `Gate3/` |

**Fatto quando** — con un mostro che attacca, il runtime contrattacca **quello** entro
la finestra; senza obiettivo attivo e senza aggressione, il runtime **non attacca** e
dice perché; e un attacco che non produce effetto per tre volte in 60 s porta il tier a
`Quarantined` invece di continuare.

---

## 6. Cosa fare adesso — le onde

Ogni onda è un insieme di lavori che **non si incrociano su nessun file**. Si possono
dare a agenti diversi contemporaneamente.

### Onda 1 — subito, quattro corsie in parallelo

| Corsia | Lavoro | Perché per primo |
|---|---|---|
| **Claude A** | `C1-1` + `C1-2` — il tubo delle entità e l'attaccante | Sbloccano insieme bersaglio e contrattacco. Nessun client, nessun rischio |
| **Claude B** | `C6-1` — la regola reattiva, appena `C1-2` ha consegnato l'attaccante (`C2-1` è già fatto) | È la capacità che l'operatore ha chiesto per prima: rispondere a chi attacca |
| **Cursor 1** | `C2-4` (`--step`) + `C3-1` (`--keybinds-check`) — corsia `Program.cs` | Non tocca `Perception/` né il pannello |
| **Cursor 2** | `S5` — mappa e cella d'appoggio nel pannello | Sta solo in `src/NosAi.ControlPanel/` |
| **Operatore** | `C2-2`, `C1-6`, `C1-7`, `C1-5` — in quest'ordine, dal più economico al più invasivo | Sono `UNKNOWN` che nessun lavoro di scrivania può togliere |

`C1-6` è il singolo lavoro con il rapporto valore/tempo più alto di tutto il piano:
finché `HasTarget` è `UNKNOWN`, **ogni** regola d'attacco viene saltata, per progetto.
Con `ADR-0021` smette però di essere un lavoro d'operatore: l'oracolo si scrive a
tavolino e la prova gliela dà il filo, che nella cattura di combattimento nomina il
bersaglio 16 volte in 90 secondi. All'operatore resta una passata col client aperto.

### Onda 2 — quando l'onda 1 ha riportato

| Corsia | Lavoro |
|---|---|
| Claude A | `C4-1` — implementare il catalogo delle post-condizioni |
| Claude B | `C6-2` — il `GoalStack` come precondizione dell'attacco proattivo |
| Cursor | `C2-4` (`--step`, con l'autorità già in firma) e `C3-1` (`--keybinds-check`) |
| Operatore | `C2-3` la proiezione in zona aperta, poi `C2-5` le prove di `P2`/`P3` |

### Onda 3

| Corsia | Lavoro |
|---|---|
| Claude A | `C4-2`, `C4-3` — bersaglio e skill |
| Claude B | `C6-4` — l'anti-attacco bloccato, che ora ha le post-condizioni da cui dipende |
| Cursor | `C4-4`, `C5-1` |
| Operatore | `C2-6` i 100 passi |

### Onda 4

`C6-3` il primo obiettivo, `C2-7`/`C2-8` il percorso, `C5-2`, `C3-2`/`C3-3`. Da qui in
poi il sistema **gioca**, e il lavoro diventa allargare gli obiettivi.

---

## 7. Come si riporta

Alla fine di ogni lavoro, e non prima:

- ID del lavoro (`C1-2`, non un'altra sigla);
- file creati e modificati;
- comando di build e **esito reale**; comando di test e **esito reale**, col numero;
- livello raggiunto: `Present`, `Integrated`, `Done`, `Verified` — e `Verified`
  **solo** con l'evidenza reale che lo sostiene;
- che cosa resta aperto, comprese le domande su cui ci si è fermati invece di decidere.

**Chi finisce aggiorna il § 3 di questo documento e nient'altro.** Non le tabelle delle
altre roadmap: quelle descrivono il come, e le riscrive chi le possiede.
