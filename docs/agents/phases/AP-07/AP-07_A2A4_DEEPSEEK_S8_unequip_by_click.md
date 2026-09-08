# S8 — DeepSeek — togliere un pezzo di equipaggiamento, e chiederlo al filo

Una sessione sola. **Può correre in parallelo a S6 e S7**: nessun file in comune.
La contesa di build su `NosAi.Runtime.dll` è però attesa — `MSB3027 file bloccato`
è contesa, non una tua regressione: aspetta qualche secondo e riprova.

Regole generali: `docs/agents/DEEPSEEK_HANDOFF.md`. `git pull` prima, `git push`
tu, **committa tutto**, niente impalcature lasciate indietro, e l'unico documento
che scrivi è il rapporto.

**Compila solo** `src/NosAi.Runtime`, `src/NosAi.ControlPanel`,
`tests/NosAi.Runtime.Tests` e `tests/NosAi.ControlPanel.Tests`. Mai la soluzione.

## File tuoi (7)

- `src/NosAi.Runtime/Tactical/UnequipExecutor.cs` — **nuovo**
- `src/NosAi.Runtime/Tactical/UnequipCommand.cs` — **nuovo**
- `src/NosAi.Runtime/Program.cs` — **solo** la registrazione dell'opzione nuova
- `src/NosAi.ControlPanel/MainWindow.xaml`
- `src/NosAi.ControlPanel/MainWindow.xaml.cs`
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**, nomi a tua scelta
- `tests/NosAi.ControlPanel.Tests/` — un file **nuovo**

**Sei l'unico su `Program.cs` e sull'intero `NosAi.ControlPanel`.**

Rapporto: `docs/agents/reports/RAPPORTO_S8.md`.

---

## Il fatto

Il progetto sa **cosa** il personaggio indossa e sa **dove** stanno i diciotto
riquadri a schermo, e non sa togliere niente. `Q-042`
(`docs/agents/EXECUTION_QUEUE.md`) aveva dichiarato AP-07/A2+A4 genuinamente
bloccato per due ragioni: nessuna calibrazione dello spazio-schermo del pannello,
e nessun canale di verifica per lo stato equipaggiato. **Entrambe sono cadute**, e
questo blocco esiste perché nessuno le ha ancora messe insieme:

1. **La calibrazione c'è ed è reale.**
   `data/perception/inventory-panel-roi.calibration` porta i diciotto slot
   misurati sul client vivo — riga 2: `1024 768 2026-09-06T22:18:27Z`.
2. **Il filo dice cosa è indossato.** `DecodeEquip`
   (`src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs:606-630`)
   emette `WornEquipment(entityId, slots, EquipmentWireOpcode.Equip)`, e uno slot
   con vnum `0` **viene saltato** (riga 621): uno slot svuotato **sparisce**
   dall'insieme, non compare a zero. È il segnale su cui si costruisce il verdetto.

`LoadoutPlanner` produce candidati `Unequip` dal 2026-09-06 (Q-033) e non ha mai
avuto un'esecuzione. Questo blocco fornisce l'attuazione. **Non la collega**: il
collegamento è un blocco successivo, e cablarlo qui di nascosto sarebbe peggio del
problema.

---

## Parte 1 — misurare, prima di scrivere l'esecutore

**Obbligatoria, e può concludere il task da sola.**

Con `--wire-inspect data/equip_test.noscap --timeline equip,eq,ivn` — e poi sulle
altre sette catture di `data/` — misura:

- quante righe `equip` compaiono, e in quali momenti;
- se dopo un cambio di equipaggiamento ne arriva **una nuova** — cioè due `equip`
  consecutive con contenuto diverso — e a quale distanza di tempo dalla precedente;
- quali slot compaiono, con quale vnum, e se qualche slot **sparisce** fra due
  righe consecutive.

Il verdetto sul filo esiste **solo se** il server rimanda `equip` dopo un cambio.
**Se la misura dice di no, l'esecutore non si scrive**: consegni la misura, il
motivo, e il task chiude lì. È un risultato — chiude una possibilità invece di
lasciarla aperta — e vale più di un esecutore che si dichiara verificato
guardando un filo che non risponde.

Metti la tabella nel rapporto in ogni caso, anche quando la misura conclude bene.

---

## Parte 2 — dal nome dello slot al pixel

`InventoryPanelRoiCalibration.Resolve(PixelRect clientArea)`
(`src/NosAi.Runtime/Perception/InventoryPanelRoiCalibration.cs:151-166`) scala i
diciotto rettangoli normalizzati su **qualunque** area gli passi, senza controllare
che sia quella calibrata. Il pannello del client è un elemento a dimensione fissa:
una coordinata normalizzata vale alla risoluzione in cui è stata misurata, e
altrove è un pixel plausibile e sbagliato.

Quindi l'esecutore **rifiuta con un motivo proprio** quando l'area client non è
`1024×768` (quella dichiarata dalla calibrazione, letta dal file — **mai** un
letterale nel codice), e rifiuta con un motivo distinto quando la calibrazione
manca del tutto — `InventoryPanelRoiCalibration.NotCalibratedReason` esiste già
(`:63`), riusalo.

**Non toccare `Resolve`**: la usa anche il pannello, e il controllo che serve qui è
dell'esecutore, non della calibrazione.

---

## Parte 3 — l'esecutore

Sul modello di `ClickTargetExecutor`
(`src/NosAi.Runtime/Tactical/ClickTargetExecutor.cs`, consegnato in S5):
**guardalo prima**, e segui quella sequenza — verifica, proiezione, confinamento
dentro la finestra, **una sola** apertura di scope, clic, e poi il verdetto dal
filo invece che da una convinzione. Riusa `GatedInputBackend`
(`Click(MouseButton, delayBetweenDownUpMs)` a `:362`, `MoveAbsolute` a `:360`) e i
suoi rifiuti. **Mai scendere sotto il commit point.**

Quattro esiti, tenuti distinti come i quattro di `TargetSelectionOutcome`:

- **`Confirmed`** — un `equip` nuovo entro la finestra, e lo slot richiesto **non
  c'è più**;
- **`StillWorn`** — un `equip` nuovo è arrivato e lo slot è **ancora occupato**: il
  clic ha fatto qualcos'altro. È il difetto che questo task esiste per rendere
  visibile;
- **`NotConfirmed`** — nessun `equip` entro la finestra: può essere latenza o un
  clic che non ha fatto nulla, e **non è la stessa cosa** di `StillWorn`.
  Appiattirli nasconderebbe l'unico dei due che dice che la mira era sbagliata;
- **`NotAttempted`** — niente è stato emesso, non c'è niente da verificare.

### Il gesto non è noto, e non va indovinato

Nessuna registrazione dice con quale gesto si toglie un pezzo. Il gesto è quindi
un **argomento dell'operatore**, fra quelli che il gate sa esprimere: clic
singolo, doppio clic, clic destro. **Il trascinamento non è esprimibile** con
l'API attuale del gate — scrivilo nel rapporto, non aggirarlo scendendo sotto il
gate.

**Nessun valore predefinito.** Senza un default "probabile", l'operatore dichiara
cosa sta provando e il rapporto dice quale gesto ha prodotto quale esito. Un
default scelto a tavolino trasformerebbe un `NotConfirmed` in una conclusione sul
gioco invece che sulla nostra scommessa.

---

## Parte 4 — il comando

`--unequip <slot> [--gesture single|double|right] [--verify-ms <n>]`, registrato
in `Program.cs` (l'elenco delle opzioni note è a `Program.cs:1162`).

`[REFUSED]` con il nome esatto di ciò che non va, come ogni altro comando del
runtime: uno slot che l'enum `EquipmentSlot` non nomina è un **rifiuto**, non
un'eccezione; un gesto sconosciuto pure; `--verify-ms` non numerico pure. La conta
degli slot si deriva dall'enum, **mai** scritta a mano (la stessa regola già
applicata da `--calibrate-inventory-panel`, `Program.cs:131-142`).

---

## Parte 5 — il bottone

Regola dell'utente, senza eccezioni: **se serve un test reale, il pannello è
pronto prima, e non si digita niente.** Card nella vista Equipaggiamento di
`NosAi.ControlPanel`, sul modello già in uso lì (bottone → `ToolRunner.RunAsync` in
sottoprocesso → log):

- la scelta dello slot fra i diciotto **letti dalla calibrazione**, non da un
  elenco scritto a mano;
- la scelta del gesto;
- l'esito del filo per esteso — `Confirmed` / `StillWorn` / `NotConfirmed` col
  motivo — **mai** una spunta verde generica;
- se la calibrazione manca, o la risoluzione del client non è quella calibrata, la
  card **lo dice prima** invece di offrire un bottone che rifiuterà.

---

## Fuori scope

- **Nessun equip dallo zaino.** Solo i diciotto slot equipaggiamento sono
  calibrati; i riquadri dello zaino no. Metterceli per somiglianza è
  esattamente ciò che questo progetto chiama indovinare.
- Nessun collegamento a `LoadoutPlanner`, nessun automatismo che scelga da solo
  cosa togliere.
- Nessuna modifica a `Resolve`, a `GatedInputBackend`, al decoder.

---

## Test

Runtime (due file nuovi), su righe di filo costruite a mano — è come sono provati
gli altri, **guardali**:

1. Calibrazione assente → rifiuto con `NotCalibratedReason`, e **nessun** clic
   emesso.
2. Area client diversa da quella calibrata → rifiuto con il motivo proprio, e
   nessun clic emesso.
3. Slot fuori dall'enum → `[REFUSED]` col nome, nessuna eccezione.
4. Gesto sconosciuto → `[REFUSED]`, nessun clic.
5. Clic emesso, poi un `equip` in cui lo slot **non compare** → `Confirmed`.
6. Clic emesso, poi un `equip` in cui lo slot **compare ancora** → `StillWorn`,
   **non** `NotConfirmed`.
7. Clic emesso, nessun `equip` entro la finestra → `NotConfirmed`, con il tempo
   atteso riportato.
8. Scope rifiutato dal gate → `NotAttempted`, e la finestra di verifica **non**
   si apre nemmeno.
9. Un solo scope aperto per un solo clic (contatore del gate).

ControlPanel (un file nuovo):

10. Con calibrazione assente la card mostra il motivo e **non** offre il bottone.
11. L'elenco degli slot ha esattamente i valori letti dal file di calibrazione.

**Mai un `Assert.All` o un `foreach` su una raccolta che potrebbe essere vuota**
senza prima asserire che non lo sia.

---

## Il limite da dichiarare, e non è un difetto

Che il **clic** provochi davvero l'unequip resta da verificare sul client vivo con
l'operatore: qui si prova che, dato un certo filo, il verdetto è quello giusto.
**Scrivilo nel rapporto**: è la differenza fra `Integrated` e `Verified`, e
dichiararla è parte della consegna.

---

## Cosa scrivere nel rapporto

`docs/agents/reports/RAPPORTO_S8.md`, in italiano, fatti e numeri:

1. **La tabella della Parte 1**, compresa la misura che non conclude, col perché.
2. File creati e modificati, uno per riga.
3. Build e test: comandi esatti e risultato numerico.
4. Cosa hai lasciato `Unknown` e con quale motivo.
5. Dove ti sei fermato, se ti sei fermato: quale file ti serviva e di chi era.
6. Se la specifica e il codice non concordavano, cosa hai trovato. **Ha sempre
   ragione il codice.**

**Il criterio con cui sarà controllato**: non che il comando esista, ma che i
quattro esiti restino quattro — e che un clic non verificato non venga mai
riportato come un pezzo tolto.
