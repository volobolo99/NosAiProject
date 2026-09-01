# Controllo del personaggio — ordine dei lavori e ripartizione Claude / Cursor

**Versione:** 1.1
**Data:** 1 settembre 2026
**Riferimenti:** `CONTROLLO_PERSONAGGIO_ARCHITETTURA.md`,
`CONTROLLO_PERSONAGGIO_ATTUAZIONE.md`, `docs/adr/ADR-0019-*`

---

## 1. Perché questo ordine

La prima versione di questo piano partiva da zero: finestra, posizione, griglia, calibrazione,
poi il primo passo. Il confronto con il repository ha mostrato che **tre di quelle cinque
tappe sono già fatte**. Il backend di input esiste ed è già dietro una barriera; il probe
esiste; la calibrazione esiste, ed è più raffinata di quella che il piano proponeva.

Quello che manca non è l'infrastruttura per agire: è **la garanzia che l'atto arrivi dove è
stato autorizzato**, e la conoscenza di dove si può camminare. Il piano riparte da lì.

La regola di ordinamento resta una sola:

> Non si emette un input prima di poter osservare il suo effetto — e non si emette un input
> prima di poter garantire che finisca nella finestra giusta.

```
P0 verifiche ──► P1 griglia ─────────┐
                                     ├─► P4 PRIMO PASSO ─► P5 percorso ─► P6 bersaglio ─► P7 skill
P2a geometria ─► P2 commit point ─► P3 autorità┘                          │
                                                          └─► P8 resilienza
```

`P0`, `P1` e `P2` sono indipendenti e vanno in parallelo.

---

## 2. Criterio di ripartizione

Quello di `PIANO_GATE_A` § 1: non « Claude fa le cose difficili », ma **cosa succede se una
riga è sbagliata**.

| | Claude | Cursor |
|---|---|---|
| Prende | ciò che se sbagliato **fallisce in silenzio**: guardie e loro composizione, commit point, semantica della griglia, macchine a stati, verifier | ciò che se sbagliato **non compila o rompe un test**: progetti, wiring, P/Invoke, estrattori, CLI, propagazione, scaffolding dei test |
| Non fa | modifiche meccaniche su decine di file | soglie di sicurezza, criteri di rifiuto, regole di autorizzazione, scelte di canale |

Se Cursor deve **decidere** qualcosa di sicurezza, si ferma e la domanda torna a Claude.

---

## 3. Le tappe

### P0 — Le quattro verifiche aperte — **CHIUSA il 1 settembre 2026**

Erano le domande di `CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` § 6, dove restano con le risposte.
Tre su quattro erano negative, e due hanno cambiato il piano:

- il ritorno dall'arresto contava i fallimenti **consecutivi**, e i due gate si
  riassegnavano `Normal` su qualunque ciclo confermato: con esiti alternati la scala non
  saliva nemmeno il primo gradino. **Corretto** — finestra scorrevole, stato di prova a una
  azione per volta, cooldown esponenziale, controllo d'ammissione;
- l'epoca di geometria non esisteva, e il Runtime non era consapevole del DPI. Da qui **P2a**;
- il difetto del `Math.Clamp` in `MoveAbsolute`, trovato leggendo, è in P2.

Sono rimaste le quattro righe qui sotto perché il criterio resta valido per le verifiche
future: si legge il codice prima di pianificare contro di esso.

| Verifica | Chi |
|---|---|
| Manifest: consapevolezza DPI per monitor dichiarata? | Cursor |
| L'epoca di geometria incrementa su cambio DPI e cambio monitor? | Cursor |
| Le coordinate assolute coprono il desktop virtuale o solo il primario? | Cursor |
| Il ritorno dallo stato di arresto usa una finestra scorrevole o un contatore? | Claude |

**DoD** — quattro risposte scritte, con il file e la riga che le sostiene. Ogni risposta
negativa diventa un lavoro in P2.

---

### P1 — Griglia di mappa dal client

| Contenuto | Chi |
|---|---|
| ~~Semantica dei bit, `IsWalkable`, `BlocksAttack`, `HasLineOfSight`, regola fuori-griglia~~ **fatto** | Claude |
| ~~Innesto in `TileType` e confine con `Unobserved`~~ **fatto** (`StaticGeometryLayer`) | Claude |
| ~~Contributo dell'hash all'identità della build e invalidazione~~ **fatto** (`MapGridSetIdentity`) | Claude |
| ~~Estrattore dagli archivi via `NosArchive`~~ **fatto** — archivio `NStcData` | Cursor |
| ~~Loader del formato, `--extract-maps`, `--map-info <mapId>`~~ **fatto** — 777 mappe, 0 rifiutate | Cursor |
| ~~Test di caricamento su tutte le mappe estratte~~ **fatto** | Cursor |
| `--extract-maps` sul volume `NOSAI-SSD` vero, una volta | operatore |
| **Trovare `mapId`** — `--find-mapid`, poi un portale, poi di nuovo, **poi un riavvio del client**: deve sopravvivere un solo candidato, e ancorato a una base che il runtime risolve. Il comando esce `0` solo con entrambe le prove | operatore |
| **Prova della cella su cui si sta** — `--grid-check` col client aperto; la cella sotto il personaggio deve risultare calpestabile | operatore |

**Stato: contenitore verificato, semantica dei bit no.** Il vincolo
`larghezza × altezza + 4 == lunghezza` regge su 777 entrate reali, quindi si sa **dove** stanno
i numeri. Che il bit `0x01` significhi « camminata vietata » su questa build no.

**DoD** — due prove, in ordine di costo:

1. **la cella su cui si sta**: personaggio fermo, `player.x` e `player.y` noti, quella cella
   deve risultare calpestabile. Falsifica in un campione, e su una mappa quadrata è l'unica
   delle due che coglie una griglia trasposta — le dimensioni non la colgono mai;
2. **il bordo bloccato**, prova attiva etichettata su almeno 3 mappe: si cammina fino al bordo
   di una zona che la griglia dichiara bloccata e il client ferma il personaggio **esattamente
   lì**. Se non coincide, la griglia non è promossa.

---

### P2a — Geometria: consapevolezza DPI ed epoca

Emersa da P0 e **prerequisito di P2**: il commit point confronta un'epoca di geometria che
oggi non esiste, e la misura da cui parte è in un'unità che dipende da un'assunzione
d'ambiente mai dichiarata.

| Contenuto | Chi |
|---|---|
| Decisione sull'unità di coordinate e sulla invalidazione delle calibrazioni memorizzate | Claude |
| Registrazione del **regime di consapevolezza DPI** dentro la calibrazione, e rifiuto del riuso sotto un regime diverso: il manifest sta sull'apphost, quindi `dotnet exec` dà un regime diverso da `NosAi.Runtime.exe` | Claude |
| `GeometryEpoch`: che cosa la fa incrementare, chi la possiede, come viaggia nell'envelope | Claude |
| Osservazione di cambio DPI e cambio monitor, oltre a spostamento e ridimensionamento | Claude |
| Sostituzione del campo `_geometry` mai aggiornato di `Win32ProcessAdapter` | Claude |
| ~~`app.manifest` con `PerMonitorV2` e `ApplicationManifest` nel csproj~~ **fatto** | Cursor |
| ~~Comando che stampa rect, dpi, monitor~~ **fatto** (`--window-probe`); manca l'epoca | Cursor |
| Test: cambio scala e spostamento fra monitor con scale diverse | Cursor |

**DoD** — con il client su un monitor al 100 % e uno al 150 %, il rect letto è in pixel
fisici su entrambi, l'epoca incrementa passando dall'uno all'altro, e ogni calibrazione
stimata prima del passaggio risulta scaduta.

---

### P2 — Commit point, occlusione, precedenza umana

I tre buchi dell'atto. È la tappa che rende sicuro tutto ciò che viene dopo.
Dipende da P2a per l'epoca.

| Contenuto | Chi |
|---|---|
| `CommitPointValidator`: rivalidazione atomica delle quattro condizioni, misura del ritardo, abort sopra soglia | Claude |
| Macchina di abort con rilascio garantito dei tasti | Claude |
| Controllo di occlusione puntuale (`WindowFromPoint` + `GetAncestor` + primo piano + attributo DWM) | Claude |
| `HumanInputMonitor`: hook di basso livello, scarto degli eventi iniettati, finestra di cortesia | Claude |
| Innesto in `GatedInputBackend` senza aprire una via che lo aggiri | Claude + Cursor |
| P/Invoke, wiring, comando `--input-guards` che stampa lo stato delle quattro condizioni | Cursor |
| Test: occlusione, presa umana, geometria mutata a metà atto | Cursor |
| Sostituzione del `Math.Clamp` finale di `Win32InputBackend.MoveAbsolute` con un rifiuto motivato: un punto fuori dal desktop virtuale è impossibile, non da riportare al bordo | Claude |

**DoD** — sposti la finestra durante un atto ⇒ nessun pixel emesso. Porti un'altra finestra
davanti ⇒ abort. Tocchi il mouse ⇒ abort entro la finestra di cortesia. Ogni abort con il
proprio codice e l'ultimo punto valido.

---

### P3 — Autorità legata alla sessione

| Contenuto | Chi |
|---|---|
| Confronto dei livelli di integrità e legame dell'esito allo stato della sessione | Claude |
| Regola « sessione non attuante »: nessuna capacità di attuazione esposta | Claude |
| Riuso di `InputEnvironmentProbe`, validità temporale del probe, ripetizione al ripristino del primo piano | Claude |
| Superficie CLI e dashboard che mostra lo stato di autorità | Cursor |

**DoD** — client elevato e runtime no ⇒ sessione non attuante con il proprio codice,
osservazione che continua, e **nessun ritentativo**.

---

### P4 — Il primo passo

Una cella. Il percorso completo su un atto minimo.

| Contenuto | Chi |
|---|---|
| Composizione finale delle guardie e ordine di corto circuito | Claude |
| `MovementVerifier`: griglia contro griglia, 350 ms ± 20 ms, esiti riuscita / stallo / abort | Claude |
| **Condizione di freschezza dell'occupazione all'atto**: l'atto richiede un'osservazione che copra la cella di destinazione entro la soglia dichiarata. Assenza di osservazione e osservazione scaduta sono lo stesso ingresso, e non sono « libero ». Oggi manca: l'unico confronto d'età è in `TargetSelector` e riguarda il bersaglio | Claude |
| Comando `--step <dx> <dy>` con stampa dell'esito di ogni guardia | Cursor |
| Eventi di audit dell'intera catena | Cursor |

**DoD** — 100 passi consecutivi su cella adiacente calpestabile; tasso di successo dichiarato;
zero campioni fuori dal client; zero atti con la finestra non in primo piano; ogni fallimento
etichettato.

---

### P5 — Percorso

| Contenuto | Chi |
|---|---|
| Rivalidazione per segmento contro griglia statica e ostacoli dinamici | Claude |
| Politica di replan e limite ai replan consecutivi | Claude |
| Valutazione di Jump Point Search **solo** se la rivalidazione continua misura un costo | Claude |
| `--walk <gx> <gy>`, visualizzazione del percorso | Cursor |

**DoD** — 20 percorsi da ≥ 15 celle su ≥ 3 mappe. Nessun input per un percorso che attraversa
una cella bloccata.

---

### P6 — Bersaglio e interazione

| Contenuto | Chi |
|---|---|
| Politica di selezione, criterio di irraggiungibilità e suo decadimento | Claude |
| Verifier: oggetto rimosso dalla lista, dialogo aperto, timeout | Claude |
| Catalogo azioni, comandi, test negativi | Cursor |

**DoD** — 50 raccolte verificate sulla sparizione dell'oggetto, non su un'ipotesi visiva.

---

### P7 — Skill

| Contenuto | Chi |
|---|---|
| Precondizioni: cooldown, MP, bersaglio vivo e in raggio, linea di vista sul bit `0x02` | Claude |
| Verifier: cooldown attivo e MP decrementato entro 250 ms | Claude |
| Divieto di rinvio automatico dal backend | Claude |
| Profilo della barra come dato, comando, cinque test negativi | Cursor |

**DoD** — cinque test negativi verdi, uno per precondizione.

---

### P8 — Resilienza

**Ridotta.** Il nucleo — finestra scorrevole, stato di prova, cooldown esponenziale,
controllo d'ammissione — è stato scritto chiudendo P0, con la sua suite di test. Restano:

| Contenuto | Chi |
|---|---|
| Budget a due livelli — azioni al secondo ed eventi d'input al secondo — per stato | Claude |
| Taratura dei valori di default (finestra 20, 3 successi di prova, 5 s base) su dati di esercizio | Claude |
| Dump diagnostico, comando di arresto immediato, esposizione nella dashboard | Cursor |

**DoD già raggiunta** — dieci successi alternati a nove fallimenti non riportano il sistema
a piena velocità (`RecoveryCircuitBreakerTests`). Livello di verifica: `Done`, non
`Verified`: nessuna di quelle transizioni è ancora stata osservata su un runtime che agisce
davvero sul client.

---

## 4. Preambolo comune ai comandi

Da citare in `CLAUDE.md` e in `.cursor/rules/`.

```
CONTESTO — controllo del personaggio

Normativi: CONTROLLO_PERSONAGGIO_ARCHITETTURA.md,
CONTROLLO_PERSONAGGIO_ATTUAZIONE.md, docs/adr/ADR-0019, ADR-0014, ADR-0003.

Vincoli:
1. Percorso critico deterministico. Nessun LLM ha autorità di esecuzione.
2. Fail-closed: timeout o anomalia bloccano, non aprono.
3. Niente mock o dati sintetici sul percorso critico.
4. Attuazione solo per input del sistema operativo. Niente messaggi postati
   alla finestra. Il filo e la memoria restano osservazione.
5. Zero-allocation .NET 8 sul percorso critico.
6. Durate con orologio monotono, mai con DateTime.UtcNow.
7. Nessun motivo di guasto come stringa letterale.
8. Sconosciuto non è sicuro: chiude.
9. Un programma d'input ha al massimo un passo irreversibile, ed è l'ultimo.
10. Contraddizione fra due documenti: fermarsi e aprire un ADR.

Prima di scrivere, leggere il codice esistente citato nella tappa: molto di
ciò che sembra da fare è già fatto.

Codice completo, nessun placeholder. Identificatori in inglese,
documentazione in italiano.
```

---

## 5. Comandi

### Claude

**C-P0** — « Rispondi alla quarta verifica di P0: leggi il codice che gestisce il ritorno
dallo stato di arresto e dimmi se usa una finestra scorrevole o un contatore, citando file e
riga. Se è un contatore, scrivi la sostituzione con finestra scorrevole e stato di prova a
una azione per volta, e il test in cui dieci successi alternati a nove fallimenti non
riportano il sistema a piena velocità. »

**C-P1** — « Scrivi `MapGrid` in `NosAi.Runtime.Navigation`: struct readonly con larghezza,
altezza e buffer di byte; semantica dei bit `0x01` camminata vietata, `0x02` attraversamento
degli attacchi bloccato, `0x04` raid, `0x08` aggro, `0x10` PvP; `IsWalkable`, `BlocksAttack`,
`HasLineOfSight` che traccia il segmento sul bit `0x02`. Fuori griglia è bloccato, non
libero. Definisci come la griglia alimenta `TileType` in `NavigationPathfinding` senza che
`Unobserved` perda il suo significato per gli ostacoli dinamici, e come l'hash entra
nell'identità della build. Il loader lo scrive Cursor: consegna il contratto, la semantica e
i test che il loader deve superare. »

**C-P2** — « Scrivi `CommitPointValidator` e `HumanInputMonitor`. Il primo rivalida
atomicamente epoca di geometria, primo piano, `WindowFromPoint` risalito alla finestra di
sessione e assenza di input umano, immediatamente prima del passo irreversibile, misura il
ritardo fra ultima verifica ed emissione e aborta sopra soglia. Il secondo usa hook di basso
livello scartando gli eventi marcati come iniettati; spiega perché `GetLastInputInfo` non
basta. Definisci l'innesto in `GatedInputBackend` in modo che non esista una via che lo
aggiri, e la macchina di abort che rilascia sempre i tasti. »

**C-P3** — « Lega l'esito di `InputEnvironmentProbe` allo stato della sessione: confronto dei
livelli di integrità, validità temporale del probe, ripetizione al ripristino del primo
piano, e regola per cui una sessione non attuante non espone alcuna capacità di attuazione al
livello decisionale pur restando valida per l'osservazione. Motiva la tolleranza scelta. »

**C-P4** — « Componi le guardie nell'ordine di corto circuito definitivo e scrivi
`MovementVerifier`: confronto griglia contro griglia sulla posizione osservata, finestra
350 ms con tolleranza 20 ms misurata con orologio monotono, esiti riuscita / stallo / abort.
Definisci l'interfaccia che il comando `--step` deve rispettare. »

**C-P5** — « Scrivi la rivalidazione per segmento e la politica di replan con limite ai
replan consecutivi. Poi misura: se la rivalidazione continua domina il costo, valuta Jump
Point Search sulla griglia a costo uniforme e motiva con il numero di nodi espansi, non con
la velocità in astratto. Se non domina, dillo e non toccare A\*. »

**C-P6** — « Politica di selezione del bersaglio, criterio di irraggiungibilità e suo
decadimento; verifier dell'interazione basato sulla rimozione dell'oggetto dalla lista entità
e sull'apertura del dialogo, con timeout. »

**C-P7** — « `PreconditionEvaluator` con cooldown, MP, bersaglio vivo e in raggio, linea di
vista sul bit `0x02`, ciascuna con il proprio codice di guasto; verifier del cast su cooldown
attivo e MP decrementato; regola che vieta al backend di rinviare un cast non osservato, con
la ragione. »

**C-P8** — « Applica l'esito di C-P0 e definisci il budget a due livelli, azioni al secondo
ed eventi d'input al secondo, per ciascuno stato. »

### Cursor

**X-P0** — « @Codebase Rispondi a tre domande citando file e riga: (1) il manifest dichiara
la consapevolezza DPI per monitor? (2) l'epoca di geometria incrementa su cambio DPI e cambio
monitor, oltre che su spostamento e ridimensionamento? (3) la conversione a coordinate
assolute normalizzate copre il desktop virtuale o solo il monitor primario? Non correggere
nulla: rispondi e basta. »

**X-P1** — « @Codebase Estrattore delle griglie di mappa dagli archivi del client usando
`NosArchive`, verso `<NOSAI-SSD>\NosAi\data\maps\<mapId>.grid` con manifesto e hash per file.
Loader del formato `uint16` LE larghezza, `uint16` LE altezza, poi larghezza per altezza
byte, senza copie inutili. Comandi `--extract-maps` e `--map-info <mapId>`. Test di
caricamento su tutte le mappe estratte. Non toccare la semantica dei bit: è in `MapGrid`. »

**X-P2** — « @Codebase Cabla `CommitPointValidator` e `HumanInputMonitor` dentro il percorso
di emissione, aggiungi le P/Invoke mancanti con `LibraryImport`, e il comando
`--input-guards` che stampa lo stato delle quattro condizioni del commit point. Test contro
il client reale: finestra spostata a metà atto, finestra terza interposta sul punto di click,
input umano durante un programma d'input. Non modificare soglie né tolleranze. »

**X-P3** — « @Codebase Esponi lo stato di autorità della sessione nella CLI e nella
dashboard, con il codice di guasto quando la sessione non è attuante. »

**X-P4** — « @Codebase Comando `--step <dx> <dy>`: un solo passo su cella adiacente, stampa
dell'esito di ogni guardia e del codice in caso di rifiuto. Emissione degli eventi di audit
dell'intera catena. Test end-to-end contro il client reale. »

**X-P5** — « @Codebase Comando `--walk <gx> <gy>` con stampa del percorso, dei nodi espansi e
dei replan; visualizzazione del percorso e della griglia nella dashboard; test del percorso
che attraversa una cella bloccata. »

**X-P6** — « @Codebase Aggiungi selezione bersaglio, interazione e raccolta al catalogo azioni
con il loro osservabile di verifica; comandi corrispondenti; test negativi. Nessuna azione
entra nel catalogo senza un osservabile. »

**X-P7** — « @Codebase Profilo della barra come dato con schema e caricatore; comando di uso
skill; i cinque test negativi, uno per precondizione. »

**X-P8** — « @Codebase Dump diagnostico alla transizione di arresto, comando di arresto
immediato, esposizione dello stato e dei budget nella dashboard e nelle metriche. »

---

## 6. Cosa fare adesso

1. `X-P0` a Cursor e `C-P0` a Claude: tre domande e una lettura, nessuna modifica.
2. In parallelo `C-P1` e `C-P2` a Claude: sono le due tappe che portano il valore.
3. `P2` prima di `P4`. Il primo passo non si emette finché il commit point non esiste.
