# DeepSeek — consegna del 2026-09-08, per continuare da solo

Claude non è disponibile per qualche ora. Questo file dice **dov'è arrivato il
lavoro** e **cosa c'è da fare**, in sessioni indipendenti.

---

## REGOLE DI QUESTA CONSEGNA — valgono per ogni sessione

1. **Solo codice e test.** Nessun documento, nessun aggiornamento a
   `EXECUTION_QUEUE.md`, `PROTOCOLLO_NOSTALE.md`, `TEST_RIMANDATI.md` o
   `STATO_IMPLEMENTAZIONE.md`. Li aggiorna Claude. (REGOLA ASSOLUTA #2 di
   `docs/agents/DEEPSEEK_TASKS.md`.)
2. **L'unica eccezione**: alla fine scrivi **un solo file**
   `docs/agents/reports/RAPPORTO_<SIGLA>.md` (esempio: `RAPPORTO_S1.md`) con
   quello che hai fatto — vedi «Cosa scrivere nel rapporto» in fondo. Serve a
   Claude per controllare senza rileggere tutto il diff.
3. **Un agente = una sessione = i file elencati in quella sessione, e nessun
   altro.** Se ti serve un file di un'altra sessione, **fermati e scrivilo nel
   rapporto**. Mai toccarlo.
4. **Compila solo i progetti della tua sessione**, mai `dotnet build NosAi.sln`.
   Altri agenti compilano in parallelo: `error MSB3027 … il file è bloccato` **è
   contesa, non una tua regressione** — aspetta qualche secondo e riprova.
5. **`git pull` prima, `git push` tu alla fine.** Un commit per sessione, con la
   sigla nel titolo (`S1: …`). **Committa tutto ciò che hai scritto**: la
   consegna precedente ha lasciato tre file di produzione e tre di test fuori da
   git, e i test sarebbero stati verdi in locale e rossi su un clone.
6. **Niente impalcature lasciate indietro.** Se scrivi file di misura usa e
   getta, cancellali prima del commit. La consegna precedente ne ha lasciati
   quattro (`ZzSkillMeasure*.cs`).
7. **Misura prima di decidere.** Un campo che le registrazioni non distinguono
   resta `Unknown` con il motivo. Un «non decidibile, ecco i numeri» vale più di
   una scelta plausibile: è già successo col costo MP ed era la risposta giusta.
8. **Mai un `return` anticipato al posto di un salto.** Usa
   `[RecordedCaptureFact]` / `[NosTaleClientFact]`: saltano visibilmente dove la
   registrazione o il client mancano.
9. **Mai un `Assert.All` o un `foreach` su una raccolta che potrebbe essere
   vuota** senza prima asserire che non lo sia: passerebbe a vuoto.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`.

---

## Dove è arrivato il lavoro

Fatto e su GitHub oggi (`d1a6bc5`, `df8c726`, `0f54183` e precedenti):

- il decoder legge **sedici** opcode: `stat st in mv die out su cond lev eq equip
  sr ivn get drop ct`. `out` è entrato con un genere di evento proprio
  (`GameEventKind.EntityLeft = 5`), **mai** `EntityDeath`;
- il campo 5 di `su` e il campo 7 di `ct` sono il **vnum dell'abilità**,
  confermati su 282 pacchetti contro il catalogo del client;
- `--wire-inspect` ha `--timeline [op1,op2,…]`: i pacchetti **in ordine
  cronologico attraverso opcode diversi**. È lo strumento con cui si misura tutto
  ciò che segue;
- `SkillCatalogue` e `--skill-report` esistono: quattro campi confermati
  (`TYPE[1]` posizione, `TYPE[2]` classe, `DATA[5]` ricarica in **decimi di
  secondo**, `TARGET[3]` bersagli d'area), tre lasciati provvisori col motivo;
- il costo MP resta **indeciso fra `COST[0]` e `DATA[8]`**: le registrazioni non
  li distinguono. Chiude con la cattura descritta in `docs/TEST_RIMANDATI.md`
  § T-17, che deve farla l'operatore. **Non deciderlo a tavolino.**

---

# S1 — gli opcode che il filo porta e nessuno legge

**File tuoi** (4):
- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**, nomi a tua scelta

Il censimento di `data/messaggi.noscap` elenca **trentuno** opcode; il decoder ne
legge sedici. Restano fuori:

```
say  script  cancel  qsti  gidx  sayi2  qstlist  sayi  fd  guri  msgi
rest  eff  icon  npc_req  pidx  qr  targetoff
```

**Il lavoro è in due tempi, e il primo è obbligatorio.**

**Primo tempo — misurare.** Con `--wire-inspect <file> --fields` su **tutte** le
otto catture di `data/`, per ognuno di quei diciotto opcode: quante occorrenze,
quanti campi, e quali campi **variano** e quali sono costanti. Un campo costante
su tutte le occorrenze **non è decodificabile per misura**: dire cosa significa
sarebbe indovinare. Metti la tabella nel rapporto.

**Secondo tempo — decodificare solo ciò che regge.** Un campo si decodifica solo
se hai un **riscontro indipendente**: un altro opcode che dice la stessa cosa,
oppure il catalogo del client. Tre piste già viste, da verificare con
`--timeline`, non da assumere:

- `icon` campo 4 sembra il vnum dell'oggetto raccolto: in `messaggi.noscap` vale
  `8`, e nello stesso momento `sayi` porta `8` come argomento e `get` segue un
  `drop 8`. **Un'occorrenza sola non è una regola**: cercane altre nelle altre
  catture, e se non ce ne sono, dillo;
- `msgi` sembra portare gli stessi campi di `sayi` spostati: `msgi[2]` dove
  `sayi[4]` ha l'id del messaggio. In `messaggi.noscap` ci sono **due** `msgi` e
  tutti i campi tranne il secondo sono a zero: probabilmente non basta. Misura e
  riferisci;
- `cancel` campo 2 sembra l'id dell'oggetto a terra di `drop`. In
  `messaggi.noscap` i dieci `cancel` sono **tutti costanti** (`0 0 -1`), quindi lì
  non si misura: guarda nelle altre catture.

**Ciò che non si misura resta non letto**, e il rapporto dice quali e perché. È
un risultato: chiude una possibilità invece di lasciarla aperta.

**Vincoli.** Ogni opcode nuovo che leggi deve emettere un contratto esistente o
un genere di evento nuovo — mai riusare `EntityDeath` o `CombatHit` per qualcosa
che non lo è, come `out` non è una morte. Un campo assente resta assente, mai
zero.

---

# S2 — il pannello ignora nove campi su quattordici che riceve

**File tuoi** (7):
- `src/NosAi.ControlPanel/MainWindow.xaml`
- `src/NosAi.ControlPanel/MainWindow.xaml.cs`
- `src/NosAi.ControlPanel/GameplayWireReader.cs`
- `src/NosAi.ControlPanel/CombatInspect.cs`
- `src/NosAi.ControlPanel/InventoryPanelInspect.cs` — **nuovo**
- `tests/NosAi.ControlPanel.Tests/` — due file **nuovi**

**Compila solo** `src/NosAi.ControlPanel` e `tests/NosAi.ControlPanel.Tests`.

`GameplayObservation.ToWire()` (`src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs:279-340`)
pubblica quattordici campi. `GameplayWireReader` ne legge **cinque**: `entities`,
`hitBy`, `hasTarget`, `mapId`, `standingCell`. Non legge:

```
inventory   selectedTarget   groundItems   lastPickup   skillsReady   inCombat
```

Sono tutti popolati **dal vivo** da `NetworkGameplayProvider` (`:827-840`), non
sono segnaposto. Due conseguenze concrete, misurate:

- la pagina **Equipaggiamento** fa registrare trenta secondi di filo su file per
  scoprire un `InventoryKind` che lo snapshot **sta già pubblicando dal vivo**;
- `CombatInspect` dice «Bersaglio: presente» senza poter dire **quale**, mentre
  `selectedTarget.entityId` è nel JSON.

**Cosa fare**: leggerli e mostrarli, con la stessa disciplina del resto del
pannello — ogni campo non osservato è `UNKNOWN` col proprio motivo, mai zero né
cella vuota, e la provenienza è quella che il JSON dichiara, non una stringa
scelta a mano.

**Seconda metà — la ROI dell'inventario.** Il file
`data/perception/inventory-panel-roi.calibration` contiene **diciotto** riquadri
di slot equipaggiamento misurati sul client reale (1024×768, 2026-09-06), e
`grep -rn "inventory-panel-roi" src/NosAi.ControlPanel/` non dà **nessun**
risultato: è un dato vero, versionato, e invisibile. Mostralo — quali slot sono
calibrati, quando, su che risoluzione — sul modello di `TargetInspect.cs:249-257`,
che per la ROI bersaglio lo fa già.

**Fuori scope**: nessuna modifica sotto `src/NosAi.Runtime/`. Se un dato ti serve
e il runtime non lo pubblica, **fermati e scrivilo nel rapporto**.

---

# S3 — il catalogo dei mostri contro il filo

**File tuoi** (6):
- `src/NosAi.Runtime/GameData/MonsterReferenceDecoder.cs`
- `src/NosAi.Runtime/GameData/MonsterCatalogue.cs` — **nuovo**
- `src/NosAi.Runtime/Observability/MonsterReportCommand.cs` — **nuovo**
- `src/NosAi.Runtime/Program.cs` — **solo** la registrazione dell'opzione nuova
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**

**Sei l'unico che tocca `Program.cs`.**

Stesso schema di `SkillCatalogue` e `--skill-report`, già consegnati: **guardali
prima**, e segui quella forma — `ClassifiedValue<T>` per distinguere confermato da
provvisorio, catalogo assente distinto da vnum assente, `[REFUSED]` con il nome
dell'opzione sconosciuta.

**Quello che è già confermato e su cui appoggiarti**: il campo 3 di `st` è il
**livello** dell'entità — 26 confronti fra il filo e `entity.level` del catalogo,
26 concordi, zero discordi. Da lì in poi, misura tu.

Un `st` reale, per orientarti:

```
st 3 313816 8 0 66 100 198 52 310 52 0
   ty id     lv ?  ?   ?  ?   ?  ?   ?  ?
```

Il vnum arriva da `in`, il livello dal campo 3, e il resto è da misurare. I campi
7 e 9 sembrano vita corrente e massima del bersaglio (198 su 310) — **sembrano**:
verifica con `--timeline` che il 7 cali quando `su` colpisce e che il 9 resti
costante per id, e **cerca nel catalogo un campo che valga 310 per il vnum 45**.
Se non c'è, dillo: è un risultato.

**Attenzione a una trappola già trovata**: il catalogo **non** distingue mostri da
NPC, varchi e pet. Le quattro entità di tipo 2 osservate hanno RaceType 0, 3, 2 e
3, e nessuna l'8 che `MonsterReference.IsSpecialNonMonsterEntity` cerca. Il
catalogo le chiama tutte mostri. **Non costruire nessuna classificazione
«attaccabile / non attaccabile» sul catalogo**: l'unico discriminante misurato è
la specie letta sul filo, ed è già in `EntitySighting.Kind`.

**Fuori scope**: nessun consumatore. Non collegare il catalogo alla selezione del
bersaglio né alla pianificazione.

---

# S4 — il catalogo degli oggetti contro il filo

**File tuoi** (4):
- `src/NosAi.Runtime/GameData/ItemReferenceDecoder.cs`
- `src/NosAi.Runtime/GameData/ItemCatalogue.cs` — **nuovo**
- `tests/NosAi.Runtime.Tests/` — due file **nuovi**

**Non toccare `Program.cs`**: è di S3. Questa sessione non aggiunge comandi.

`ItemReferenceDecoder` decodifica `Item.dat` e il suo tracciato viene da una fonte
di comunità: le sue stesse *remarks* dicono quali campi sono incrociati e quali
restano provvisori. Il filo porta vnum di oggetti in quattro punti:

- `drop <vnum> <id> <x> <y> <quantità> …` — oggetto a terra;
- `get … <id> …` — raccolto;
- `ivn <tipo> <slot>.<vnum>.<rarità>.<…>` — riga di zaino;
- `sayi` campo 6, quando il campo 5 vale `2` — l'argomento è un vnum di oggetto.

**Il riscontro che vale**: i vnum che le otto catture nominano davvero esistono
nel catalogo, e i loro campi decodificati sono coerenti fra le quattro fonti —
per esempio il `drop 8` e il `sayi … 2 8` della stessa cattura devono dare lo
stesso oggetto («Fionda in legno»).

Come S3: `ItemCatalogue` sul modello di `SkillCatalogue`, ogni campo marcato
confermato o provvisorio, e **niente riempito per somiglianza** con un campo
adiacente confermato.

**Fuori scope**: nessun consumatore, nessun comando nuovo.

---

## Cosa scrivere nel rapporto

`docs/agents/reports/RAPPORTO_<SIGLA>.md`, in italiano, **fatti e numeri, non
prosa**:

1. **File creati e modificati**, uno per riga.
2. **Build e test**: i comandi esatti e il risultato numerico
   (`Superati: N. Non superati: 0`).
3. **Le misure**, in tabella. Comprese quelle che **non** hanno concluso, con
   scritto perché.
4. **Cosa hai lasciato `Unknown`** e con quale motivo.
5. **Dove ti sei fermato**, se ti sei fermato: quale file ti sarebbe servito e di
   chi era.
6. **Se la specifica e il codice non concordavano**, cosa hai trovato. Ha sempre
   ragione il codice.

**Il criterio con cui sarà controllato**: non quanti campi hai decodificato, ma
quanti con una misura che regge, e quanto onestamente hai riportato quelli che
non reggono.
