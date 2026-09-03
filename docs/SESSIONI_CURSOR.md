# Sessioni di lavoro per Cursor

**Versione:** 1.0
**Data:** 2 settembre 2026
**Riferimenti:** `docs/CONTROLLO_PERSONAGGIO_ROADMAP.md` (ripartizione e tappe),
`docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md`, `docs/adr/ADR-0019`, `ADR-0014`, `ADR-0003`.

---

## 1. Perché questo documento

`CONTROLLO_PERSONAGGIO_ROADMAP.md` § 5 elenca i comandi per tappa. Sono corti perché
descrivono *cosa* fare, non *quanto* fare in una volta. Questo documento li raggruppa in
**sessioni lunghe e autosufficienti**: ogni blocco qui sotto è un solo messaggio da
incollare in Cursor, contiene già il contesto normativo, e arriva fino a una condizione di
uscita verificabile senza dover tornare a chiedere.

Il criterio di raggruppamento è uno solo: **una sessione non deve mai fermarsi ad
aspettare una decisione di sicurezza.** Tutto ciò che richiede di scegliere una soglia, un
criterio di rifiuto o una regola di autorizzazione è già stato deciso e sta nel codice; a
Cursor resta ciò che, se sbagliato, non compila o rompe un test.

Ordine: `S1`, `S2` e `S3` sono chiuse il 2 settembre 2026. `S4` e `S5` sono aperte, e
sono state scelte per non incrociarsi: `S4` sta nel runtime, `S5` nel pannello.

---

## 2. Preambolo comune

Va incollato **in testa a ogni sessione**. È il preambolo di
`CONTROLLO_PERSONAGGIO_ROADMAP.md` § 4 con l'aggiunta di quel che è cambiato dopo P2 e P3.

```
CONTESTO — controllo del personaggio

Normativi: docs/CONTROLLO_PERSONAGGIO_ARCHITETTURA.md,
docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md, docs/CONTROLLO_PERSONAGGIO_ROADMAP.md,
docs/adr/ADR-0019, ADR-0014, ADR-0003.

Vincoli:
1. Percorso critico deterministico. Nessun LLM ha autorità di esecuzione.
2. Fail-closed: timeout o anomalia bloccano, non aprono.
3. Niente mock o dati sintetici sul percorso critico.
4. Attuazione solo per input del sistema operativo. Niente messaggi postati
   alla finestra. Il filo e la memoria restano osservazione.
5. Zero-allocation .NET 8 sul percorso critico.
6. Durate con orologio monotono, mai con DateTime.UtcNow.
7. Nessun motivo di guasto come stringa letterale sparsa: le costanti dei
   motivi stanno sulla classe che li produce, e si citano da lì.
8. Sconosciuto non è sicuro: chiude.
9. Un programma d'input ha al massimo un passo irreversibile, ed è l'ultimo.
10. Contraddizione fra due documenti: fermarsi e aprire un ADR.

Stato che devi conoscere prima di scrivere:
- il commit point esiste ed è cablato (CommitPointValidator, HumanInputMonitor,
  ActuationScope, GatedInputBackend). Cinque condizioni, non quattro: la quinta
  è la scala. Non toccare soglie né tolleranze;
- l'autorità di sessione esiste (SessionActuationAuthority): confronto dei
  livelli di integrità, atto innocuo con ripristino del puntatore, verdetto con
  validità di 60 s, latch sui rifiuti terminali;
- l'effettore Gate 3 (InputActionEffector) non espone la capacità di attuazione
  quando la sessione non è attuante.

Prima di scrivere, leggi il codice esistente citato nella sessione: molto di ciò
che sembra da fare è già fatto.

Codice completo, nessun placeholder. Identificatori in inglese, documentazione
XML in inglese come nel resto del runtime.

DOVE VANNO I TOKEN — regola vincolante, prevale su ogni abitudine.

Nella chat: niente. Non scrivere prosa, piani, "ora faccio", riepiloghi di ciò
che hai letto, elenchi di file prima di scriverli, spiegazioni di scelte ovvie,
ripetizioni della consegna. In chat è ammesso SOLO il rapporto finale nella
forma richiesta dalla sessione.

Nel codice e nei file: NESSUN LIMITE. Non accorciare un'implementazione, non
omettere un caso, non tagliare la documentazione XML, non ridurre i test per
risparmiare. Il divieto riguarda ciò che non finisce nel repository; tutto ciò
che ci finisce si scrive per esteso.

Le due metà stanno insieme e la seconda è la più importante: "non sprecare
token" non significa "scrivere meno codice". Una implementazione incompleta o
un test in meno costano molto più di qualunque paragrafo in chat.

Se una richiesta ti costringe a DECIDERE una soglia, un criterio di rifiuto o
una regola di autorizzazione: fermati, non inventarla, e riporta la domanda.
```

---

## 3. `S1` — Autorità di sessione: superficie e ciclo (`X-P3`)

**Sbloccata.** Dipende solo da codice già scritto e testato.

### Cosa esiste già

| Elemento | Dove |
|---|---|
| `SessionActuationAuthority`, `SessionAuthorityVerdict`, `IntegrityLevel`, `Win32ProcessIntegrityReader` | `src/NosAi.Runtime/LowLevel/SessionActuationAuthority.cs` |
| Composizione (`CreateSafe` costruisce l'autorità e la mette in `RuntimeComponents.SessionAuthority`) | `src/NosAi.Runtime/Orchestration/RuntimeComposition.cs` |
| Prima presa del verdetto all'attach | `Gate1BootstrapHost.RefreshActuationAuthority` |
| Rifiuto verso il livello decisionale | `InputActionEffector.UnavailableReason` |
| Test | `tests/NosAi.Runtime.Tests/SessionAuthorityTests.cs` |

### Il comando

```
[PREAMBOLO COMUNE]

SESSIONE S1 — superficie e ciclo dell'autorità di sessione (X-P3).

@Codebase Leggi prima src/NosAi.Runtime/LowLevel/SessionActuationAuthority.cs
per intero, poi Gate1BootstrapHost.RefreshActuationAuthority,
RuntimeComposition.CreateSafe e InputActionEffector.UnavailableReason. Non
modificare nessuno dei criteri che ci trovi: validità di 60 s, tolleranza di 2 px,
quali rifiuti sono terminali, l'ordine in cui i controlli parlano. Sono decisi.

Lavoro, in quest'ordine.

1. Comando CLI `--input-authority [--watch <n>]` in src/NosAi.Runtime/Program.cs,
   costruito come `--input-guards` e registrato dove sono registrati gli altri
   comandi diagnostici (compresa la lista dei comandi che non richiedono l'host
   completo). Stampa, per la sessione attaccata:
   - PID e handle della finestra del client, oppure il motivo per cui non c'è;
   - livello di integrità del runtime e livello del client, per nome
     (`IntegrityLevel.Name`), e `unknown` quando non leggibile;
   - se la sessione è attuante, e in caso contrario il motivo esatto
     (`SessionAuthorityVerdict.RefusalReason`), se il verdetto è terminale,
     l'errore in pixel del puntatore e da quanto tempo il verdetto sta in piedi;
   - con `--watch <n>`, ripete n volte a 1 s di distanza chiamando
     `EnsureVerified()`, così l'operatore può portare il client in primo piano e
     vedere il verdetto cambiare da solo.
   Uscita non-zero quando la sessione non è attuante: è un comando di verifica.

2. Chiamate di `EnsureVerified()` nei due punti dove il verdetto va rinfrescato:
   - all'inizio di ogni ciclo decisionale che possa produrre un atto, prima che
     il piano venga composto — cerca il punto dove l'effettore Gate 3 viene
     interrogato e mettila lì, non dentro l'effettore;
   - quando la finestra del client torna in primo piano. Se esiste già un punto
     che osserva il primo piano, aggancia `NoteForegroundRestored()` lì; se non
     esiste, usa il comando `--input-authority --watch` come unico innesco e
     dichiaralo nel commento invece di inventare un timer.
   `EnsureVerified()` è progettata per essere chiamata spesso: sul percorso già
   verificato legge un campo e torna.

3. Campi additivi nello snapshot dell'operatore, accanto a quelli di sicurezza
   esistenti (cerca dove finisce `executionMode`): `sessionActuating` (bool),
   `sessionAuthorityReason` (string?), `sessionAuthorityTerminal` (bool),
   `runtimeIntegrity`, `clientIntegrity` (string). Additivi: nessun campo
   esistente cambia nome o significato, e il contratto resta compatibile.

4. Control Panel (src/NosAi.ControlPanel): una riga di stato che dice se la
   sessione è attuante e, quando non lo è, il motivo per esteso. Quando il
   verdetto è terminale la riga deve dirlo, perché è la differenza fra « riprova
   fra un attimo » e « così non funzionerà mai »: il pannello non deve offrire un
   pulsante che riprovi. Il pannello chiede, non decide: nessun percorso del
   pannello può marcare una sessione attuante.

5. Test in tests/NosAi.Runtime.Tests:
   - il comando CLI formatta ciascuno dei casi (attuante, non attuante non
     terminale, non attuante terminale, nessuna sessione) e nomina il motivo;
   - il ciclo decisionale chiama `EnsureVerified()` prima di comporre il piano;
   - lo snapshot porta i quattro campi nuovi e non ne ha persi;
   - la serializzazione dello snapshot resta compatibile con il contratto
     esistente (aggiunta di campi, non rinomina).

Vincoli di questa sessione:
- non toccare SessionActuationAuthority.cs se non per aggiungere, e solo se
  qualcosa che ti serve non è esposto: in quel caso aggiungi una proprietà di
  sola lettura e dillo nel riepilogo;
- non introdurre un timer che chiami `Verify()` da solo. Il verdetto si prende
  quando serve o quando la finestra torna avanti, mai a intervalli: un probe che
  parte da solo muove il puntatore dell'operatore senza che nessuno lo abbia
  chiesto;
- `dotnet build -c Release` senza warning e `dotnet test` verdi prima di
  riportare. Riporta file toccati, comandi eseguiti e il loro esito reale.
```

### DoD di `S1`

1. `NosAi.Runtime.exe --input-authority` stampa i due livelli di integrità e il verdetto,
   e restituisce un codice di uscita non-zero quando la sessione non è attuante.
2. Con NosTale avviato **come amministratore** e il runtime no, la riga dice
   `authority_integrity_below_client:medium_under_high`, il verdetto è terminale, e in
   dieci ripetizioni **il puntatore non si muove nemmeno una volta**.
3. Con il client non elevato e in primo piano, `--input-authority --watch 10` mostra il
   passaggio a sessione attuante e il puntatore torna esattamente dov'era.
4. Il Control Panel mostra la stessa cosa dello snapshot, e non offre un ritentativo su un
   verdetto terminale.
5. Build senza warning, suite runtime verde.

---

## 4. `S2` — Diagnostica, freno e registro (`X-P8` + pannello)

**Chiusa il 2 settembre 2026.** Il nucleo di resilienza resta quello di `P0`
(`RecoveryCircuitBreakerTests`): questa sessione ha aggiunto solo la superficie.

### Il comando

```
[PREAMBOLO COMUNE]

SESSIONE S2 — diagnostica, arresto immediato e registro eventi nel pannello (X-P8).

@Codebase Leggi prima il controller di recovery e il circuit breaker (cerca
RecoveryController e i test in tests/NosAi.Runtime.Tests/RecoveryCircuitBreakerTests.cs),
poi EventLogDiagnostics e il suo comando `--event-log-report`. Il nucleo —
finestra scorrevole, stato di prova, cooldown esponenziale, controllo di
ammissione — è già scritto e tarato. Non cambiare nessuno di quei valori.

Lavoro, in quest'ordine.

1. Dump diagnostico alla transizione verso lo stato di arresto. Alla transizione,
   non a intervalli: quel che serve è la fotografia del momento in cui il sistema
   ha smesso di fidarsi di sé. Contenuto minimo: stato precedente e nuovo, il
   contenuto della finestra scorrevole dei fallimenti, l'ultimo rifiuto del
   commit point e il suo motivo, l'ultimo verdetto di autorità di sessione, e
   l'ultimo esito per stage della pipeline. Scritto in data/ (gitignored), un
   file per transizione, nome con marca temporale ordinabile. Nessun segreto,
   nessuna chiave, nessun percorso della macchina dell'operatore oltre a quelli
   già presenti nei log.

2. Comando di arresto immediato: `--halt` da CLI e comando corrispondente nel
   Control Panel, che disarma gli interruttori (esecuzione, input diretto,
   injection) e aborta lo scope di attuazione aperto, in quest'ordine —
   disarmare dopo aver abortito lascerebbe acceso proprio il pezzo pericoloso.
   Passa dal percorso di autorizzazione esistente: solo SecurityPrincipal.Operator.
   Deve essere idempotente: due arresti di fila non sono un errore.

3. Esposizione nello snapshot e nel pannello dello stato di resilienza: stato
   corrente del controller, quanti fallimenti nella finestra, quanto manca al
   prossimo tentativo ammesso, e i budget in vigore. Solo lettura di ciò che il
   controller già espone; se un valore non è esposto, aggiungi una proprietà di
   sola lettura e dillo.

4. Registro eventi nel pannello: chiama EventLogDiagnostics e mostra eventi
   totali, intervallo di sequenza, `IsComplete` e i gap registrati. Un registro
   incompleto va mostrato come incompleto, in modo visibile: è la differenza fra
   un audit trail e una lista di righe.

5. Test: la transizione ad arresto produce esattamente un dump con i campi
   dichiarati; `--halt` disarma e aborta nell'ordine giusto ed è idempotente; il
   pannello riporta un registro con gap come incompleto.

Vincoli: nessuna modifica alle soglie di recovery, nessun nuovo stato nella
macchina, nessun ritentativo automatico aggiunto da nessuna parte.
`dotnet build -c Release` senza warning e `dotnet test` verdi prima di riportare.
```

### DoD di `S2`

1. Una transizione ad arresto lascia un file di dump con i campi dichiarati, e uno solo.
2. `--halt` disarma e aborta, in quest'ordine, ed è idempotente.
3. Il pannello mostra stato di resilienza e salute del registro, e un registro con gap si
   vede come incompleto.
4. Build senza warning, suite runtime verde.

### Esito

Dump su transizione a `Halted` (un file in `data/halt-*.json`, campi dichiarati);
`--halt` e il bottone HALT del pannello disarmano poi abortiscono, idempotenti,
solo `SecurityPrincipal.Operator`; snapshot additivo `resilience`; registro con
gap etichettato `INCOMPLETO`. Soglie e stati del breaker invariati. Livello:
`Done`, non `Verified` — nessun halt è ancora stato osservato su un runtime che
agisce sul client.

---

## 5. `S3` — Geometria su due monitor (residuo di `P2a`)

**Sbloccata.** È l'ultima riga aperta di `P2a`, ed è quella che dà la DoD della tappa.

### Il comando

```
[PREAMBOLO COMUNE]

SESSIONE S3 — prova della geometria su monitor a scale diverse (residuo di P2a).

@Codebase Leggi src/NosAi.Runtime/Perception/GeometryEpoch.cs per intero,
ClientWindowDpiProbe, DpiAwarenessRegime, il comando `--window-probe` e
tests/NosAi.Runtime.Tests/GeometryEpochTests.cs. L'epoca è derivata e non
mantenuta: si legge, non si conserva. Non introdurre una cache.

Lavoro, in quest'ordine.

1. Test automatici che oggi mancano, contro la sola parte simulabile: l'epoca
   confrontata attraverso un cambio di DPI a parità di rettangolo, un cambio di
   monitor a parità di DPI, e uno spostamento a parità di tutto il resto. Ogni
   caso deve nominare il proprio motivo (`geometry_dpi_changed`,
   `geometry_monitor_changed`, `geometry_window_moved`), e un'epoca sconosciuta
   da un lato non deve mai comparare uguale.

2. Prova che una calibrazione stimata sotto un regime di consapevolezza DPI
   diverso venga rifiutata al riuso, e che il motivo lo dica. Il regime sta
   sull'apphost, quindi `dotnet exec` e `NosAi.Runtime.exe` danno regimi diversi:
   il test deve fissare quel comportamento, non aggirarlo.

3. Estendi `--window-probe` perché stampi, oltre a rect, dpi, monitor ed epoca,
   il regime di consapevolezza DPI del processo corrente e se la calibrazione
   memorizzata è utilizzabile sotto quel regime. Uscita non-zero quando non lo è.

4. Una procedura d'operatore scritta in docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md,
   in fondo alla sezione sulla geometria: due monitor a scale diverse, il client
   spostato dall'uno all'altro, cosa deve stampare `--window-probe` prima e dopo.
   Sei righe, non una pagina.

Vincoli: nessuna modifica alla semantica del confronto delle epoche, nessuna
cache, nessun valore di DPI predefinito quando la lettura fallisce — sconosciuto
resta sconosciuto. `dotnet build -c Release` senza warning e `dotnet test` verdi
prima di riportare.
```

### DoD di `S3`

Con il client su un monitor al 100 % e uno al 150 %: il rect letto è in pixel fisici su
entrambi, l'epoca cambia passando dall'uno all'altro nominando il componente che è
cambiato, e ogni calibrazione stimata prima del passaggio risulta scaduta. Le parti
simulabili sono coperte da test; la parte fisica è una procedura d'operatore scritta.

---

## 6. `S4` — Il primo passo (`X-P4`)

**Sbloccata a livello di codice il 2 settembre 2026**, quando `C-P4` è stato scritto.
Resta bloccata **l'emissione sul client vivo**, e la differenza è tutta qui: la sessione
scrive il comando, i suoi test e l'audit; *premerlo con NosTale aperto* richiede prima le
due prove d'operatore elencate in fondo.

### Cosa esiste già

| Elemento | Dove |
|---|---|
| Composizione delle guardie e ordine di corto circuito | `src/NosAi.Runtime/Navigation/StepGuardChain.cs` |
| Freschezza dell'occupazione all'atto | `src/NosAi.Runtime/Navigation/OccupancyFreshness.cs` |
| `MovementVerifier`, cinque esiti, finestra 350 ms + 20 ms | `src/NosAi.Runtime/Navigation/MovementVerifier.cs` |
| L'interfaccia che `--step` deve rispettare | `src/NosAi.Runtime/Navigation/SingleStepExecutor.cs` |
| L'autorita' che ogni atto deve nominare (ADR-0020 § 2) | `src/NosAi.Runtime/LowLevel/ActuationAuthority.cs` |
| Test | `tests/NosAi.Runtime.Tests/StepGuardTests.cs` (33), `CommitPointTests` |

### Il comando

```
[PREAMBOLO COMUNE]

SESSIONE S4 — il primo passo (X-P4).

@Codebase Leggi per intero, e prima di scrivere: StepGuardChain.cs,
OccupancyFreshness.cs, MovementVerifier.cs, SingleStepExecutor.cs. Poi guarda come
--input-authority e --input-guards sono costruiti in Program.cs.

Quello che segue non si tocca, si usa: l'ordine delle guardie, quali rifiuti sono
terminali, la finestra di 350 ms e la tolleranza di 20 ms, la regola per cui una
lettura vale solo se timbrata dopo l'emissione, le due soglie di freschezza. Se ti
sembra che una di queste sia sbagliata, fermati e riportalo invece di cambiarla.

Lavoro, in quest'ordine.

0. ADR-0020 e' gia' applicato: SingleStepExecutor.Step prende una ActuationAuthority e
   non esiste un overload senza. Passa ActuationAuthority.Commanded("--step"): l'atto e'
   comandato da una persona, e l'audit deve dirlo. Non inventare un SafetyToken per un
   comando d'operatore, e non aggirare il parametro con un default.

1. Comando `--step <dx> <dy>` in Program.cs. Un solo passo su cella adiacente.
   Costruisce StepGuardChain e SingleStepExecutor dai componenti gia composti nel
   runtime: non ricostruire il grafo a mano e non istanziare Win32InputBackend, il
   gate arriva da RuntimeComponents. Stampa, in ordine:
   - la richiesta: mappa, cella di partenza, cella di destinazione;
   - una riga per ciascuna delle sei guardie, con Passed / Refused / NotEvaluated e,
     per quella che ha rifiutato, il motivo esatto. Le NotEvaluated si stampano:
     "non valutata" e' un fatto diverso da "passata";
   - se autorizzato, il pixel di destinazione e la scala sotto cui e' stato calcolato;
   - se emesso, l'esito del verifier con i millisecondi misurati e il dettaglio;
   - se non emesso, il motivo del rifiuto di emissione.
   Uscita: 0 solo su MovementOutcome.Succeeded. Ogni altro esito e' non-zero, con
   codici distinti per rifiuto-guardia, non-emesso, e stallo/spostato/non-osservato.

2. --step non deve partire se l'input non e' armato o se la sessione non e' attuante,
   e non perche' lo ricontrolli tu: la catena lo dice gia'. Stampa il ladder ed esci.
   Non aggiungere un --force di nessun tipo.

3. Eventi di audit dell'intera catena, sul registro eventi esistente: uno per
   l'autorizzazione (con l'esito di ogni guardia), uno per l'emissione (pixel, scala,
   istante, e l'autorita' via ActuationAuthority.Describe()), uno per la verifica
   (esito, millisecondi, letture accettate). Payload
   JSON piatto, nessun segreto, nessun percorso di macchina. Rileggibili da
   EventLogReader nello stesso ordine in cui sono avvenuti.

4. Test in tests/NosAi.Runtime.Tests:
   - la formattazione del ladder per ciascun punto di rifiuto, righe NotEvaluated
     comprese;
   - i codici di uscita per ciascun esito;
   - i tre eventi di audit in ordine per un passo autorizzato, e solo quello di
     autorizzazione per un passo rifiutato. Un evento di emissione senza emissione
     sarebbe esattamente la bugia che questo audit esiste per impedire;
   - un passo rifiutato non produce nessun evento di input (RecordingInputBackend).

5. NON scrivere un test automatico che clicca sul client reale. La corsa dei 100 passi
   e' una procedura d'operatore: scrivila in dieci righe in fondo a
   docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md - cosa aprire, cosa armare, cosa lanciare,
   cosa deve stampare, e quando fermarsi.

Vincoli: nessun ritentativo automatico da nessuna parte, nemmeno uno. Nessun percorso
che raggiunga il backend d'input senza passare da SingleStepExecutor.
dotnet build -c Release senza warning e dotnet test verdi prima di riportare.
```

### DoD di `S4`

1. `--step 1 0` con l'input disarmato stampa il ladder, si ferma a `Policy`, esce
   non-zero e **non emette nulla**.
2. Con tutto armato e il client in primo piano, stampa sei righe di guardia, il pixel,
   e un esito del verifier con i millisecondi misurati.
3. Tre eventi di audit per un passo emesso, uno solo per un passo rifiutato, e
   **ognuno nomina l'autorità dell'atto** — nessun evento con quel campo vuoto.
4. Build senza warning, suite runtime verde.
5. **La corsa dei 100 passi resta aperta**: è la DoD di `P4`, ed è dell'operatore.

### Il blocco che resta, e perché

**Primo, e non è una prova d'operatore: l'autorità dell'atto.** `ADR-0020` (*proposto*)
chiede che `GatedInputBackend.TryBeginActuation` riceva sempre l'autorità sotto cui lo
scope è aperto — il `SafetyToken` del ciclo, oppure un comando d'operatore nominato — e
`SingleStepExecutor` oggi non ne porta nessuna. Non è la guardia `Authority` della
catena, che chiede se il runtime può guidare *questa sessione*; è chi risponde di
*questo atto*. Cablare `--step` prima significherebbe consegnare all'operatore un
comando che emette input reale mentre il gate non sa attribuirlo. La firma cambia di un
parametro, è **di Claude** perché è una regola di autorizzazione (`ROADMAP` § 2), e
`S4` la trova già fatta o si ferma e la chiede.

Poi due prove, d'operatore e non di Cursor:

1. le tre di `P2` sul client vivo — `--input-guards --watch 20`, finestra spostata a
   metà atto, finestra terza interposta sul punto, mano sul mouse. Ogni prova deve
   nominare il proprio rifiuto;
2. quella di `P3` — client elevato ⇒ sessione non attuante, terminale, puntatore fermo;
   client non elevato ⇒ sessione attuante.

Non è una formalità. `--step` emette il primo input reale diretto al client: se una
guardia non è mai stata vista rifiutare sul sistema vero, il primo passo è anche la
prima occasione per scoprire che non rifiutava.

---

## 7. `S5` — Griglia e cella di appoggio nel pannello

**Sbloccata**, e scelta apposta per non incrociare `S4`: sta in
`src/NosAi.ControlPanel/` e nei suoi test, e legge `MapGrid` senza scrivere nulla nel
runtime. È anche la prima delle due prove di `P1` — quella della cella su cui si sta —
resa ripetibile invece che fatta una volta a mano.

### Il comando

```
[PREAMBOLO COMUNE]

SESSIONE S5 — griglia e cella di appoggio nel Centro di controllo.

@Codebase Leggi src/NosAi.Runtime/Navigation/MapGrid.cs, StaticGeometryLayer.cs,
MapGridSetIdentity.cs, MapGridCheck.cs e il comando --grid-check; poi come il pannello
legge oggi lo snapshot (SnapshotView.cs, AttachedSnapshot.cs).

Questa sessione e' di sola lettura verso il runtime: non aggiunge comandi, non arma
niente, non tocca il percorso d'input. Se ti serve un dato che il runtime non espone,
aggiungi una proprieta' di sola lettura e dillo nel riepilogo.

Lavoro, in quest'ordine.

1. Una vista "Mappa" nel pannello che mostra, per la sessione attaccata:
   - l'id mappa e da dove viene, con il motivo quando e' UNKNOWN;
   - se una griglia e' caricata per quella mappa, le sue dimensioni, e l'identita'
     dell'insieme (MapGridSetIdentity) con l'hash della build;
   - la cella su cui sta il personaggio e SE QUELLA CELLA RISULTA CALPESTABILE.
     Questa riga e' la prova: una cella di appoggio non calpestabile significa che la
     griglia e il mondo non parlano della stessa mappa, e va mostrata come errore, non
     come dettaglio.

2. Un ritaglio della griglia intorno al personaggio - 31x31 celle bastano - disegnato
   con tre stati distinguibili anche in bianco e nero: calpestabile, bloccata, fuori
   griglia. Nessun quarto stato inventato: se l'occupancy dinamica non e' disponibile,
   non disegnarla affatto invece di disegnare "libero".

3. Stato UNKNOWN esplicito ovunque: nessuna griglia caricata, posizione sconosciuta,
   identita' della build non verificata. UNKNOWN non e' una cella vuota e non e' "fuori
   griglia": sono tre disegni diversi.

4. Test in tests/NosAi.ControlPanel.Tests: la riga della cella di appoggio nei quattro
   casi (calpestabile, non calpestabile, fuori griglia, posizione sconosciuta); il
   ritaglio ai bordi della mappa non va in eccezione e mostra il fuori-griglia; nessun
   percorso della vista scrive verso il runtime.

Vincoli: niente polling stretto, la vista si aggiorna quando lo snapshot cambia.
Nessun valore inventato quando la griglia non c'e'.
dotnet build -c Release senza warning e dotnet test verdi prima di riportare.
```

### DoD di `S5`

1. Con il client aperto e la griglia estratta: id mappa, dimensioni, hash della build e
   la cella di appoggio marcata calpestabile.
2. Senza griglia, tutto `UNKNOWN` con il motivo, e niente disegnato come libero.
3. Una cella di appoggio non calpestabile è mostrata come errore.
4. Build senza warning, suite pannello verde.

---

## 8. `S6` — Le due voci che mancano al menu

`--record-wire` e `--calibrate-vitals` esistono e sono verdi, ma si raggiungono solo
dalla riga di comando. Ogni altra sonda ha la sua voce nel menu operatore; queste due no,
e sono proprio quelle che l'operatore usa più spesso, perché la calibrazione va rifatta
a ogni riavvio del client.

Sessione piccola, tutta dentro un file più i suoi test. Non tocca la memoria, non tocca
il filo, non attua nulla: aggiunge due voci che chiamano codice già scritto e già testato.

### Cosa esiste già

- `src/NosAi.Runtime/LiveIntegration/Capture/WireRecorder.cs` — `Run(endpoint, path, seconds)`,
  flag `--record-wire <ip>:<port> [file.noscap] [--watch N]`.
- `src/NosAi.Runtime/LiveIntegration/PlayerVitalsCalibrator.cs` — `Run(endpoint, seconds)`,
  flag `--calibrate-vitals <ip>:<port> [--watch N]`.
- `src/NosAi.Runtime/Operator/OperatorMenu.cs` — il menu arriva alla voce `19`.
  Le prime libere sono `20` e `21`.

### Il comando

```
CONTESTO — voci di menu per registrazione del filo e calibrazione delle statistiche

Normativi: CLAUDE.md, docs/SPEC_ESTENSIONE_LAYOUT_MEMORIA.md, docs/adr/ADR-0014.

Aggiungi due voci al menu operatore in src/NosAi.Runtime/Operator/OperatorMenu.cs:

  20  Registra il filo             (una cattura .noscap della sessione in corso)
  21  Calibra HP e MP dal filo     (due giri; il filo dice i numeri, la memoria li mostra)

La voce 20 chiama NosAi.LiveIntegration.Capture.WireRecorder.Run.
La voce 21 chiama NosAi.LiveIntegration.PlayerVitalsCalibrator.Run.

Il meccanismo del menu, che devi rispettare:
1. Lo switch in Run() e l'elenco stampato in Draw() sono DUE blocchi separati e non
   generati dalla stessa fonte. Vanno modificati entrambi, o la voce esiste e non si
   vede, oppure si vede e non esiste.
2. Ogni gestore passa da Perform(titolo, Func<int>), che restituisce 0 per successo.
3. I gestori esistenti non ricevono nulla per iniezione: si procurano da soli quel che
   serve. Guarda RunEntityNames e RunReplay come modello.

Entrambe le voci hanno bisogno di un endpoint `<ip>:<porta>` che l'operatore non
conosce a memoria. Chiedilo con Console.ReadLine, e se la riga è vuota o malformata
NON inventare un valore di riposo: stampa il rifiuto e restituisci 2. Non serve che
tu validi l'IP a mano — WireRecorder.TryParseEndpoint lo fa già e nomina il motivo
(record_endpoint_missing, record_host_not_an_ip:{host}, record_port_implausible:{p}).
Passa la stringa così com'è e lascia rifiutare al codice che sa farlo.

Per la durata: chiedi i secondi, e se la riga è vuota usa il valore di riposo del
comando chiamato invece di scriverne uno tuo.

Vincoli:
- Entrambe richiedono una console elevata. Scrivilo nella riga di menu o subito
  prima di lanciare: il rifiuto che arriva senza elevazione è leggibile ma tardivo.
- Il test OperatorMenuTests.TheMenuOffersNothingThatActuates legge il sorgente del
  menu e vieta le stringhe "--arm-input", "ScreenProjectionAutoCalibrator" e
  "ActuationScope". Non introdurle.
- Apostrofi ASCII nelle stringhe italiane, come fa già il file (Entita', d'input).
- Non toccare nessun altro file di src/. In particolare NON toccare Program.cs,
  PlayerVitalsCalibrator.cs, PointerAnchorHunter.cs, NosTaleClientLayout.cs.

Test: aggiungi un file NUOVO in tests/NosAi.Runtime.Tests/ (non modificare
OperatorMenuTests.cs). Copri le funzioni pure che estrai — la lettura dei secondi
con riga vuota, e il fatto che il sorgente del menu contenga entrambe le voci in
entrambi i blocchi. xUnit, namespace NosAi.Runtime.Tests, classe public sealed,
nomi di metodo in prosa PascalCase, <summary> che dice perché il comportamento conta.

Build e test, entrambi verdi prima di riportare:
  dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release
  dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --configuration Release

Non fare push e non toccare main. Commit sul tuo branch, messaggio imperativo nello
stile del repo.
```

### DoD di `S6`

1. Le voci `20` e `21` compaiono nell'elenco e rispondono nello switch.
2. Un endpoint vuoto o malformato produce un rifiuto nominato e `2`, mai un valore
   di riposo inventato.
3. Nessuna delle tre stringhe vietate compare nel sorgente del menu.
4. Build senza warning, suite runtime verde, e il numero di test cresce solo dei tuoi.

---

## 9. Come riportare

Alla fine di ogni sessione, e non prima:

- ID della sessione (`S1`…`S5`);
- file creati e modificati;
- riepilogo dell'implementazione in poche righe;
- comando di build e **esito reale**;
- comando di test e **esito reale**, con il numero di test;
- livello di verifica raggiunto: `Present`, `Integrated`, `Done`, `Verified` — e
  `Verified` solo con l'evidenza reale che lo sostiene;
- rischi e blocchi rimasti, comprese le domande su cui ti sei fermato invece di decidere.
