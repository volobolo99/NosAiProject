# AP-05 / A2+A4 — DeepSeek — misurare un opcode invece di indovinarlo

**Indipendente dai due task in coda.** `progression_from_lev` e
`worn_equipment_from_wire` possiedono `GameTrafficObserver.cs`,
`NosTaleWorldProtocolDecoder.cs`, `WorldChannelReplay.cs` e
`WorldReplayCommand.cs`; questo non tocca nessuno dei quattro. Può essere preso
prima, dopo o in parallelo. **Se lo prendi in parallelo, dillo nel report**, così
chi integra sa che due consegne sono aperte insieme.

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `docs/PROTOCOLLO_NOSTALE.md` — il catalogo degli opcode.
3. `CLAUDE.md` § *Architecture invariants* — in particolare *Real, derived,
   cached and simulated data remain explicitly distinguishable*.

**REGOLA #3 vale su ogni riga di questo file**: ogni numero qui sotto è stato
misurato il 2026-09-07 su `data/nostale_combat.noscap` attraverso la catena
reale. Se il codice ti dice altro, **vince il codice** e lo scrivi nel report.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull` prima, **`git
push` tu alla fine — non c'è nessun hook.**

---

## Perché questo task esiste

Per scrivere le due specifiche in coda è servito sapere cosa contengono davvero
i pacchetti `lev` e `eq`. **Non esiste un modo di saperlo**: `--world-replay` dà
un censimento per opcode ma non una riga; `--live-decode` stampa le righe ma
vuole il driver WinDivert e una sessione viva; `--decide-replay` legge un file ma
per un'altra domanda. L'unico modo è stato scrivere un test usa-e-getta,
compilare la suite, leggerne l'output e cancellarlo. Tre volte in un giorno.

Il costo vero non è la scomodità: è che **senza una misura, un campo si
indovina**. `docs/PROTOCOLLO_NOSTALE.md` classifica ogni campo come *confirmed*,
*probable* o *unknown*, e quella classificazione è stata fatta a mano. Questo
task la rende meccanica.

---

## Il fatto che rende metà del lavoro già fatto

`LiveWireMonitor.Monitor(IPacketSource source, TextWriter output, …)` è **già
puro**: prende un `IPacketSource` e scrive su un `TextWriter`, senza driver,
senza console, senza elevazione — il suo stesso doc-comment lo dice, e un test
lo esercita con `InMemoryPacketSource`. E `CaptureFile.Open(path)` **restituisce
un `IPacketSource`**.

Cioè: la stampa riga-per-riga di una registrazione è a una riga di distanza.
**Non riscriverla.** Riscrivere quella logica in un secondo posto sarebbe
esattamente il difetto che `PIANO_DI_RIORDINO.md § R1` passa il tempo a togliere.

### L'unica cosa che va cambiata lì dentro, e perché

`Monitor` costruisce il framer con `NosTaleWorldFramer.Factory(DataSourceKind.Live)`
— corretto oggi, perché l'unico chiamante è il percorso vivo. Nel momento in cui
gli si dà un file, quella riga **etichetterebbe come `Live` dei byte registrati**:
è il laundering di provenienza che `CLAUDE.md` vieta per nome e che
`WorldChannelReplay` evita usando `DataSourceKind.Cached`.

Quindi: `Monitor` prende un parametro `DataSourceKind source` (default `Live`,
così nessun chiamante esistente cambia), e il percorso da file passa `Cached`.
Un test lo fissa: le righe prodotte da un file non sono mai `Live`.

---

## Il comando

```
--wire-inspect <file.noscap>                  censimento delle forme
--wire-inspect <file.noscap> --opcode lev     le righe grezze, fino a --max
--wire-inspect <file.noscap> --opcode lev --max 5
```

Sola lettura, offline, nessun driver, nessuna elevazione, nessuna attuazione.

### Metà 1 — le righe grezze (`--opcode`)

Apre il file, lo passa a `LiveWireMonitor.Monitor` con provenienza `Cached`,
filtra le righe il cui primo token è l'opcode chiesto, e si ferma dopo `--max`
righe (default 20, `0` = tutte). Il filtro è sul testo della riga: nessuna
interpretazione, nessun campo estratto.

### Metà 2 — il censimento delle forme (senza `--opcode`)

Questa è la parte nuova, ed è il motivo del task. Per ogni opcode della
registrazione:

- quante volte compare;
- quanti campi porta (e **tutte** le arietà viste, se non è sempre la stessa —
  un opcode a lunghezza variabile è un fatto, non un dettaglio);
- per ogni posizione: se il valore è **sempre lo stesso**, quel valore; se
  cambia, **quanti valori distinti** ha assunto.

L'ultima riga è tutto il senso dell'attrezzo: **un campo che non è mai cambiato
non si distingue da una costante che il server manda sempre**, quindi la cattura
non può confermargli alcun significato. Un campo che cambia è un campo su cui si
può ragionare. Questa è la regola che `CLAUDE.md` § *External reference data*
enuncia a parole; qui diventa una colonna.

### Uscita attesa — misurata, non immaginata

`--wire-inspect data/nostale_combat.noscap`, forma libera ma **questi numeri**:

```
mv       n= 7685  campi=5
su       n=  117  campi=17
ct       n=  108  campi=7
cond     n=   72  campi=5
         1=1  2=3443217  3=0  4=0  5=11
stat     n=   62  campi=6
st       n=   49  campi=11
in       n=   25  campi=31
lev      n=   23  campi=12
         1=56  2:23var  3=39  4:23var  5=18247900  6=185500  7=35106
         8=7  9=0  10=0  11=1  12=0
sayi     n=   13  campi=9
         1=1  2=3443217  3:3var  4:2var  5:2var  6:3var  7:2var  8=0  9=0
eff      n=    6  campi=3
         1=3  2:6var  3=5000
delay    n=    6  campi=3
         1=4000  2=4  3=#guri^400^3324
guri     n=    6  campi=4
         1=2  2=1  3=3443217  4=0
msgi     n=    5  campi=7
die      n=    4  campi=4
drop     n=    3  campi=7
ivn      n=    3  campi=2
icon     n=    2  campi=4
         1=1  2=3443217  3=1  4=2006
get      n=    2  campi=4
ms_c     n=    2  campi=1
         1=0
cancel   n=    1  campi=3
         1=1  2=1092257  3=-1
```

Due cose da notare, perché sono la prova che l'attrezzo dice il vero:

- **`cond` campi 3 e 4 sono `0` in tutti e 72.**
  `docs/PROTOCOLLO_NOSTALE.md` § `cond` lo dice a parole — «Both `0` throughout,
  so never observed asserted». Qui esce da solo. Se il tuo censimento dice
  altro, il difetto è nel censimento.
- **`lev` campi 2 e 4 variano, tutti gli altri no.** È l'analisi che ha deciso
  quali campi la specifica `progression_from_lev` fa decodificare e quali no.

Un campo con un valore non numerico (`delay` campo 3, `#guri^400^3324`) va
trattato come testo e riportato tale e quale: è l'unico pacchetto della cattura
con un payload non numerico e va visto, non normalizzato.

---

## OWN (file nuovi)

- `src/NosAi.Runtime/Observability/WireInspectCommand.cs`
- `tests/NosAi.Runtime.Tests/WireInspectTests.cs`

## MODIFY

- `src/NosAi.Runtime/LiveIntegration/Capture/LiveWireMonitor.cs` — **solo** il
  parametro di provenienza descritto sopra
- `src/NosAi.Runtime/Program.cs` — dispatch e `KnownProbeFlags`, nient'altro

**Nient'altro.** In particolare nessun file di `src/NosAi.Core/`, e nessuno dei
quattro posseduti dai task in coda.

---

## Struttura

`WireInspectCommand` segue `DecideReplayCommand` e `LoadoutReportCommand`:

- una funzione **pura** che prende un `IPacketSource` e un `TextWriter` e fa
  tutto il lavoro — questa è quella che i test esercitano;
- un `Run(string[] args)` sottile che valida gli argomenti, apre il file e
  chiama la pura;
- rifiuti **nominati**, mai un'eccezione nuda: file assente, estensione non
  `.noscap`, `--max` negativo, `--opcode` senza valore. Ognuno stampa un
  `[REFUSED]` con il motivo e restituisce un codice d'uscita diverso da zero.
  Guarda `DecideReplayCommandTests` per la forma dei rifiuti già in uso —
  esercita `empty.noscap`, `absent.noscap`, `junk.noscap`.

---

## Test — `WireInspectTests.cs` (nuovo)

Con `InMemoryPacketSource`, senza toccare il disco:

1. Un opcode con un solo valore per campo è riportato costante; uno che cambia è
   riportato con il conteggio dei distinti.
2. Un opcode con arietà diversa fra due pacchetti riporta **entrambe** le
   arietà, non solo l'ultima.
3. `--max` tronca davvero, e la riga successiva non viene prodotta.
4. Un campo non numerico esce tale e quale.
5. **Provenienza**: le righe prodotte leggendo da un file portano `Cached`, mai
   `Live`. Questo test è il punto del cambiamento a `LiveWireMonitor`, non un
   contorno.
6. `[Theory]` sui rifiuti: file assente, estensione sbagliata, `--max -1`,
   `--opcode` senza valore. Uno per `InlineData`, ognuno con il motivo atteso.

Contro le registrazioni vere, con `RecordedCaptureFactAttribute` (**mai** un
`if (…) return`, che xUnit conta come superato):

7. `[RecordedCaptureFact("nostale_combat.noscap")]` — il censimento riporta
   `lev` con 23 occorrenze e 12 campi, il campo 1 costante `56`, i campi 2 e 4
   variabili, i campi da 5 a 12 costanti con i valori della tabella qui sopra.
8. Lo stesso file: `cond` compare 72 volte e i suoi campi 3 e 4 sono costanti
   `0`. **Questo test vale doppio**: conferma l'attrezzo *e* verifica una
   affermazione che `docs/PROTOCOLLO_NOSTALE.md` faceva a mano.
9. `--opcode lev --max 5` su quel file produce esattamente 5 righe, e la prima è
   `lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0`.

---

## Fuori scope

- **Nessuna decodifica.** Questo attrezzo misura le forme; assegnare un
  significato a un campo è un altro task e un'altra evidenza.
- **Nessuna modifica a `--world-replay` o `--live-decode`.** Il primo è di un
  altro task in questo momento; il secondo funziona.
- **Nessuna scrittura su disco**, nessun file di rapporto: stdout e basta.
- **Nessun aggiornamento a `docs/PROTOCOLLO_NOSTALE.md`** — REGOLA #2, quella
  metà è di Claude, e sarà lui a incrociare la tua uscita con la tabella scritta
  a mano.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errori, 0 avvisi**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` — 0 falliti, numeri
  riportati; gli skip esistenti restano skip.
- `dotnet test tests/NosAi.Core.Tests -c Release` — invariato.
- `--wire-inspect data/nostale_combat.noscap` eseguito davvero, uscita completa
  incollata nel report, e **il confronto esplicito** con la tabella qui sopra:
  dove coincide e dove no. Se non coincide da qualche parte, quello è il
  risultato più importante della consegna e va in cima al report.
- `--wire-inspect data/equip_test.noscap` eseguito, uscita incollata. Non ho
  scritto io la tabella attesa per questo file di proposito: è il primo uso
  reale dell'attrezzo, e serve a vedere se dice qualcosa che nessuno sapeva.
- Livello di verifica: **Integrated**. `Verified` richiederebbe un operatore su
  client vivo, e questo attrezzo non tocca il client.
