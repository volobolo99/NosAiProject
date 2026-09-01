# Obiettivo — Controllo del personaggio: vedere, capire, muovere, combattere

**Versione:** 1.0 · **Aperto:** 1 settembre 2026 · **Stato:** IN CORSO
**Riferimenti vincolanti:** `docs/PROTOCOLLO_NOSTALE.md`, `docs/adr/ADR-0012`, `ADR-0014`,
`ADR-0016`, `ADR-0017`, `docs/GATE3_PIPELINE.md`, `CLAUDE.md`, `.cursorrules`

Questo documento è la sorgente di verità per un solo obiettivo. Non sostituisce
`NOSAI_MASTER_ROADMAP.md` né `docs/ROADMAP_ESECUTIVA.md`: li attraversa, prendendo
da ciascuno il solo percorso che porta al risultato qui sotto.

---

## 1. L'obiettivo, in termini verificabili

> NosAiProject osserva abbastanza del gioco da **muovere il personaggio** e da
> **combattere**, e ogni azione che compie è **confermata sull'osservazione**, non
> sulla propria previsione.

Un obiettivo dichiarato "al 100%" ha bisogno di una definizione che si possa
firmare. Questa:

| # | Criterio di accettazione | Prova richiesta |
|---|---|---|
| **A1** | Il runtime pubblica HP, HP massimi e MP propri come `LIVE` durante una sessione in corso | T-05: `--observe-game` acceso su sessione reale, valori uguali all'HUD |
| **A2** | Il runtime pubblica la **posizione propria** del personaggio, con un controllo di validità che la rende `UNKNOWN` quando fallisce | Spostamento reale in gioco: le coordinate seguono il movimento e si fermano quando il personaggio si ferma |
| **A3** | Il runtime pubblica le **entità in vista** con id, vnum, posizione e — quando nota — salute | Cattura di combattimento: i mob compaiono, si muovono e spariscono coerentemente con lo schermo |
| **A4** | Il runtime stabilisce **`HasTarget`**, e sa dire quando non lo sa | Bersaglio selezionato → `true`; deselezionato → `false`; riquadro illeggibile → `UNKNOWN` |
| **A5** | Il runtime **muove il personaggio** verso una coordinata richiesta, e la posizione osservata dopo lo spostamento conferma l'arrivo | Ciclo Gate 3 che termina `Confirmed` su una `MoveToPosition` |
| **A6** | Il runtime esegue un **attacco base** su un bersaglio reale, e l'HP osservato del bersaglio scende | Ciclo Gate 3 `Confirmed` su `UseBasicAttack`, incrociato con il `su` sul wire |
| **A7** | Il runtime usa una **pozione** quando l'HP è critico, e l'HP osservato risale | Ciclo Gate 3 `Confirmed` su `UseConsumable` |
| **A8** | Ogni rifiuto è **nominato**: niente si ferma senza dire perché | `ExecutionDisabled`, `RefusedStaleInput`, `NoWorldState`, `Blocked` distinti nel log |

`A5`, `A6` e `A7` sono l'obiettivo. `A1`–`A4` sono ciò senza cui non possono esistere.

---

## 2. Cosa il progetto vede già — e con quale prova

Non è poco, e non va rifatto. Ricostruirlo è il modo più veloce per perdere un mese.

**Dalla rete (server → client), verificato su due catture reali**
`WinDivertPacketSource` → `Ipv4TcpParser` → `TcpStreamReassembler` →
`NosTaleWorldFramer` (terminatore `0xFF`) → `NosTaleWorldDecoder` →
`NosTaleWorldProtocolDecoder` → `NetworkWorldFeed` → `NetworkGameplayProvider` →
snapshot Gate 1 → `Gate3WorldState`.

- `stat` → **HP, HP massimi, MP propri**, `LIVE`, confrontati con l'HUD del client.
  62 pacchetti in 90 s di combattimento.
- `st` → **HP e HP massimi assoluti** di un'altra entità (campi 7 e 9; il campo 5,
  la percentuale, è sbagliato in 21 pacchetti su 49 ed è correttamente ignorato).
- `su` → **ogni colpo**: attaccante, bersaglio, skill, danno, HP risultante.
- `in` → **comparsa** di un'entità con vnum e posizione. `die` → scomparsa.
- `mv` → **movimento** di un'entità.

**Dallo schermo**
`ClientWindowLocator` (la finestra del client, non lo schermo intero) →
`DxgiCapture` → `RoiSegmenter` → `HudGlyphExtractor` → `HudGlyphAtlas` →
`GlyphHashOcrCache` → `ScreenDerivedVitalGate`.
Dopo ADR-0017 l'atlante viene addestrato **dal wire**, e l'HP a schermo è un
intero `DERIVED`, non più un rapporto. Verificato sul ritaglio T-03.

**Dalla memoria del processo**
`ProcessMemoryReader` legge con un controllo di validità e classifica il
risultato; `MemoryScanner` + `--memory-scan` restringono un indirizzo attraverso
più passaggi; il dump del vicinato di un indirizzo trovato consente di leggere i
campi adiacenti senza altre scansioni.

**Decisione ed esecuzione**
Gate 3 è completo e collegato: planner per fatto (ADR-0016), simulazione
deterministica, ranking MAUT, policy Guard, Trust che scende soltanto, Safety Gate
con token HMAC monouso, executor che valida il token, verifier che confronta con
un'osservazione, recovery a scala. `Gate3DecisionLoop` lo esegue contro ciò che il
runtime osserva davvero, e il Control Panel lo mostra.
`Win32InputBackend` inietta input reale via `SendInput`; `GatedInputBackend` lo
rifiuta fail-closed finché la policy non lo consente.

**Baseline al 1 settembre 2026:** `NosAi.Runtime.Tests` 648 verdi,
`NosAi.ControlPanel.Tests` 43 verdi.
*(La build dell'intera solution fallisce sul solo progetto Android
`NosAi.GuardAi.App` con `XABBA7000: Permission denied` in fase di packaging APK:
è un blocco del file sul disco, non un errore di codice. Non compilare mai la
solution intera per validare una modifica al runtime.)*

---

## 3. I buchi che separano tutto questo dall'obiettivo

Otto, e sono precisi. Ognuno è nominato perché ognuno ha un criterio di chiusura.

### B1 — `HasTarget` non è stabilito da nessuna sorgente
ADR-0016 fa saltare al planner **ogni regola che legge il bersaglio** quando il
fatto è ignoto. Oggi lo è sempre: nessun pacchetto delle due catture lo stabilisce,
e `ct` non ha un contrario osservato ("bersaglio annullato"), quindi un flag dedotto
sarebbe appiccicoso e sbagliato senza che nulla sul wire lo corregga.
**Conseguenza:** il combattimento non esiste. Il runtime può solo reagire alla
propria salute. È il buco numero uno.

### B2 — La posizione propria del personaggio non arriva mai
Ogni `mv` delle catture è di tipo entità 3. La posizione del giocatore è
autoritativa lato client, e quella direzione del wire è cifrata.
**Conseguenza:** lo spostamento non esiste. Non si può andare da qualche parte
senza sapere da dove si parte.

### B3 — La posizione dei mob viene scartata quasi sempre
7685 `mv` su 8211 pacchetti non producono osservazione, perché `EntitySighting`
non ha spazio per "posizione nota, salute ignota" e riempirla di salute piena
sarebbe un'osservazione inventata. Con 25 `in` e 49 `st` contro quei 7685 `mv`,
tutto ciò che era già sullo schermo all'inizio della cattura resta invisibile.
**Conseguenza:** non si sa dove sono i bersagli, anche quando il wire lo dice.

### B4 — Nessun `IActionEffector` reale
`DisabledActionEffector` è l'unica implementazione. `ActionEffectorFactory`
restituisce `no_live_effector_bound` anche a policy aperta. Esistono l'input reale
e il cancello che lo protegge; **manca il pezzo che traduce un `ActionCandidate` in
un gesto.**
**Conseguenza:** ogni ciclo finisce `ExecutionDisabled`. Il runtime decide e non agisce.

### B5 — I bersagli del planner sono stringhe finte
`ActionPlanner.Plan` produce `"TARGET_MOB_01"`, `"WAYPOINT_A"`, `"ITEM_POTION_HP"`,
con coordinate costanti `125, 85` e `130, 90`. Nessun id reale, nessuno slot reale,
nessuna coordinata reale.
**Conseguenza:** anche con un effector collegato, non c'è nulla da eseguire.

### B6 — Nessun `IWorldStateObserver` reale
Il seam esiste e il verifier lo usa correttamente. Nessuno lo implementa.
**Conseguenza:** ogni esecuzione finirebbe `Unverified`. Il ciclo non si chiude,
e un ciclo che non si chiude non è verificabile per definizione.

### B7 — Nessuna corrispondenza fra coordinate di gioco e pixel della finestra
Muovere e attaccare significano cliccare in un punto. Nulla converte una `(x, y)`
di gioco nel pixel corrispondente della finestra del client.
**Conseguenza:** un effector collegato adesso cliccherebbe nel posto sbagliato,
che è peggio del non cliccare.

### B8 — Conflitto documentale che dirige il lavoro nella direzione sbagliata
`.cursorrules` §3 impone *"must not hook into the target process memory"* e la
*black-box supervision*; §5 assegna percezione e decisione a **Python**.
Entrambe le affermazioni sono false oggi: ADR-0014 ha revocato il divieto e ha
dato la scelta all'operatore, e percezione e decisione sono C#.
**Conseguenza:** Cursor legge quel file a ogni richiesta. Finché resta com'è,
o rifiuta il lavoro sulla memoria, o lo scrive nel linguaggio sbagliato. È il
lavoro meno costoso e più redditizio dell'intera lista, e va fatto per primo.

---

## 4. L'ordine, e perché è questo

```
F0  regole allineate            (B8)          — sblocca il lavoro di Cursor
 |
F1  percezione completa         (B1, B2, B3)  — senza fatti non si decide nulla
 |
F2  azioni con bersagli veri    (B5, B7)      — senza bersagli non c'è cosa eseguire
 |
F3  effector reale              (B4)          — il collegamento mancante
 |
F4  verifica reale              (B6)          — chiude il ciclo
 |
F5  evidenza sul gioco vero     (A1–A8)       — l'unica che vale
```

Non è riordinabile. F3 prima di F1 produrrebbe un runtime che agisce su fatti che
non ha; F3 prima di F2 produrrebbe un runtime che esegue azioni su bersagli
inventati. Sono la stessa violazione, presa da due lati.

### F1 in dettaglio — le tre strade per i tre fatti mancanti

| Fatto | Sorgente scelta | Classificazione | Perché non un'altra |
|---|---|---|---|
| **Posizione propria** (B2) | memoria del processo, con controllo di validità | `LIVE` se il check passa, `UNKNOWN` altrimenti | Il wire non la porta. La minimappa dà un rapporto, non una coordinata di mappa |
| **`HasTarget`** (B1) | riquadro bersaglio a schermo (ROI `TargetHpBar`), confermato da `ct`/`su` sul wire | `DERIVED` | Il wire non ha un "bersaglio annullato": da solo produrrebbe un flag appiccicoso |
| **Posizione dei mob** (B3) | `mv` sul wire, con la salute lasciata ignota | `LIVE` per la posizione, `UNKNOWN` per la salute | È già sul wire: manca solo lo spazio nel contratto per dirlo |

La regola che le governa tutte e tre è quella di ADR-0014, e non si negozia:
**una sorgente che non sa distinguere un valore giusto da uno sbagliato non
pubblica `LIVE`.** Un offset di memoria che si è spostato restituisce un numero
plausibile; è esattamente per questo che ogni lettura porta il proprio controllo.

---

## 5. Chi fa cosa, e perché così

La divisione non è per difficoltà: è per **quanto contesto serve per non sbagliare**.

**Claude prende** ciò che richiede di tenere in testa più file insieme, e ciò
dove un errore non si vede subito: contratti e record condivisi, classificazione
delle sorgenti, confini di sicurezza, letture di memoria, corrispondenza
mondo→schermo, ADR, integrazione fra i gate.

**Cursor prende** ciò che è **additivo, di un file solo, e già specificato**:
un nuovo opcode del decoder la cui forma è già scritta in
`docs/PROTOCOLLO_NOSTALE.md`, un lettore di ROI modellato su uno esistente, una
mappa di configurazione, un'implementazione di seam la cui interfaccia è già fissa.

Il costo di Cursor non sta nello scrivere: sta nell'**esplorare**. Ogni scheda in
`docs/TASKS_CURSOR.md` è scritta perché non debba cercare niente — percorso esatto,
firma esatta, estratto della specifica incollato dentro, comando di build del solo
progetto toccato, messaggio di commit già pronto. Una scheda che lo costringe ad
aprire un file non elencato è una scheda scritta male, e va corretta invece che
eseguita.

**Zona riservata a Claude** — Cursor non modifica questi percorsi, mai:

```
src/NosAi.Runtime/Safety/            confine di autorizzazione (ADR-0003)
src/NosAi.Runtime/Gate3/             orchestrazione, token, verifica
src/NosAi.Runtime/Contracts/         contratti condivisi e classificazione
src/NosAi.Runtime/LiveIntegration/ProcessMemoryReader.cs
docs/adr/                            i record decisionali
.cursorrules  CLAUDE.md              le regole stesse
```

---

## 6. Tabellone

`docs/TASKS_CLAUDE.md` e `docs/TASKS_CURSOR.md` contengono le schede eseguibili.

| ID | Blocco | Titolo | A chi | Dipende da |
|---|---|---|---|---|
| **F0-1** | B8 | Allineare `.cursorrules` ad ADR-0014 e al C# reale | Claude | — |
| **F1-1** | B3 | `EntitySighting`: ammettere posizione nota con salute ignota | Claude | F0-1 |
| **F1-2** | B3 | `mv` pubblica la posizione anche senza `in`/`st` precedente | Cursor | F1-1 |
| **F1-3** | — | `stat` campo 4: pubblicare gli MP massimi | Cursor | — |
| **F1-4** | — | `cond`: pubblicare la velocità di movimento del giocatore | Cursor | — |
| **F1-5** | — | `sr`: pubblicare gli slot skill pronti | Cursor | — |
| **F1-6** | — | `lev`: pubblicare livello e XP | Cursor | — |
| **F1-7** | B1 | `TargetFrameReader`: leggere il riquadro bersaglio dalla ROI | Cursor | F0-1 |
| **F1-8** | B1 | Stabilire `HasTarget` da schermo + wire, e l'ADR che lo motiva | Claude | F1-7 |
| **F1-9** | B2 | Trovare gli offset della posizione propria sul client reale | Claude + operatore | F0-1 |
| **F1-10** | B2 | `MemoryGameplayProvider` con controllo di validità | Claude | F1-9 |
| **F2-1** | B5 | `ActionCandidate` porta un bersaglio tipizzato, non una stringa | Claude | F1-1 |
| **F2-2** | B5 | Il planner sceglie il bersaglio reale più vicino | Claude | F2-1, F1-2 |
| **F2-3** | B7 | Corrispondenza coordinate di gioco → pixel, calibrata dall'operatore | Claude | F1-10 |
| **F2-4** | — | `KeybindMap`: gli slot dell'operatore, letti da configurazione | Cursor | — |
| **F3-1** | B4 | `InputActionEffector`: da `ActionCandidate` a gesto reale | Claude | F2-1…F2-4 |
| **F4-1** | B6 | `NetworkWorldStateObserver`: rileggere lo stato dopo l'azione | Cursor | — |
| **F5-1** | A1 | T-05: conferma `LIVE` su sessione in corso | Claude + operatore | F1-* |
| **F5-2** | A5–A7 | Le tre sequenze reali: pozione, attacco, spostamento | Claude + operatore | F3-1, F4-1 |

Nessuna voce di questa tabella è `DONE` finché non ha la prova che la sua riga
richiede. `CLAUDE.md` e ADR-0004 valgono qui come ovunque: l'esistenza del file
non è mai la verifica.
