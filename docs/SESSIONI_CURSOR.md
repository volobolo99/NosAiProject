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

Ordine: `S1` è sbloccata adesso. `S2` e `S3` sono indipendenti da `S1` e possono partire in
parallelo se si lavora su copie separate. `S4` è bloccata e dice da cosa.

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

**Sbloccata.** Il nucleo di resilienza è stato scritto chiudendo `P0`
(`RecoveryCircuitBreakerTests`): resta la superficie.

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

## 6. `S4` — Il primo passo (`X-P4`) — **bloccata**

Non va aperta finché non sono vere tutte e tre:

1. `C-P4` scritto: composizione finale delle guardie, `MovementVerifier`, condizione di
   freschezza dell'occupazione all'atto. È lavoro di Claude e non esiste ancora.
2. Le tre prove di `P2` viste **sul client vivo**, non sul desktop fittizio:
   `NosAi.Runtime.exe --input-guards --watch 20`, finestra spostata a metà atto, finestra
   terza interposta sul punto, mano sul mouse. Ogni prova deve nominare il proprio rifiuto.
3. La prova d'operatore di `P3`: client elevato ⇒ sessione non attuante, terminale,
   puntatore fermo; client non elevato ⇒ sessione attuante.

Il motivo per cui è bloccata non è procedurale. `--step` emette il primo input reale
diretto al client: se una delle guardie non è stata vista rifiutare sul sistema vero, il
primo passo è anche la prima occasione in cui si scopre che non rifiutava.

---

## 7. Come riportare

Alla fine di ogni sessione, e non prima:

- ID della sessione (`S1`…`S4`);
- file creati e modificati;
- riepilogo dell'implementazione in poche righe;
- comando di build e **esito reale**;
- comando di test e **esito reale**, con il numero di test;
- livello di verifica raggiunto: `Present`, `Integrated`, `Done`, `Verified` — e
  `Verified` solo con l'evidenza reale che lo sostiene;
- rischi e blocchi rimasti, comprese le domande su cui ti sei fermato invece di decidere.
