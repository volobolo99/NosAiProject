# AP-09 / A2+A4 — DeepSeek — il runtime impara a ogni ciclo e dimentica a ogni chiusura

**Indipendente da AP-05 `su_target_vitals`.** Tocca `NosAi.Storage/`,
`Runtime/Learning/`, `Runtime/Gate3/Gate3Runtime.cs`, un comando nuovo in
`Runtime/Observability/` e due righe di `Program.cs`. **Nessun file in comune**
con l'altro task: si possono lanciare insieme.

## Prima di tutto

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `src/NosAi.Runtime/Learning/PredictionLedger.cs` — **leggi le sue `remarks`
   prima del codice**: contengono la regola che rende onesto tutto il resto.
3. `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` — **il modello per lo store**.
4. `src/NosAi.Runtime/Observability/OutcomeReportCommand.cs` — **il modello per
   il comando**, e in particolare `LedgerNotCreatedReason`: un lettore non crea
   ciò che dichiara di leggere.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull`, **`git push` tu**
(non c'è hook: `.git/hooks/` ha solo file `.sample`).

**Se la specifica e il codice non concordano, ha ragione il codice**: fermati e
riferisci la discordanza invece di piegare il codice alla specifica.

---

## Il fatto

`Gate3ExecutionOrchestrator` **scrive** nel registro delle previsioni a ogni
ciclo:

```
src/NosAi.Runtime/Gate3/Gate3Runtime.cs:1414   Prediction prediction = _learning.Predict(
src/NosAi.Runtime/Gate3/Gate3Runtime.cs:1474   _learning.Record(new Observation(
src/NosAi.Runtime/Gate3/Gate3Runtime.cs:1105   public PredictionLedger Learning => _learning;
```

E nessuno legge. `CalibrationOf` e `Weakest` — i due metodi che rispondono
«quanto sono affidabile qui» — **non hanno un solo chiamante in `src/`**:

```
grep -rn "CalibrationOf\|\.Weakest(\|\.Learning\b" src/ --include=*.cs
   ->  solo la dichiarazione in PredictionLedger.cs e l'alias in Gate3Runtime.cs
```

Peggio: lo stato è tre `Dictionary` in memoria (`_open`, `_evidence`, `_tally`,
righe 96-99). Nessuna persistenza. **La calibrazione muore con il processo.** Il
runtime rifà da capo, a ogni avvio, l'apprendimento del giorno prima — e non
può dirlo a nessuno perché non c'è nulla da cui rileggerlo.

Non è un buco di documentazione: è metà di un componente che la
`ModuleReachability` (riga 129) dichiara raggiungibile.

---

## Parte 1 — `PredictionLedger`: esportare e reimportare (nessun I/O)

Due metodi puri, nello stesso file:

- **`ExportCalibration()`** — una fotografia dello stato *già risolto*: per ogni
  `ContextKey`, l'evidenza Beta-Binomiale e i conteggi (`Confirmed`, `Refuted`,
  `Ignored`, somma degli errori). Scegli tu il tipo di ritorno, ma dev'essere un
  record immutabile, non i `Dictionary` interni.
- **`ImportCalibration(snapshot)`** — rimette quello stato dentro un registro.

**Le previsioni aperte non si esportano.** Una previsione aperta riguarda un
istante che non c'è più; riportarla in un altro processo la farebbe risolvere
contro un mondo diverso da quello su cui era stata fatta — cioè esattamente il
contrario di ciò che le `remarks` del file chiamano onestà («scritta prima
dell'azione, mai dopo»). Scrivi questo motivo nel commento, con parole tue.

Decidi tu se `ImportCalibration` **rimpiazza** o **somma** all'evidenza già
presente, ma la scelta va argomentata nel commento e coperta da un test: sommare
due volte lo stesso file raddoppierebbe i conteggi, e un registro che si crede
il doppio più esperto di quanto sia è peggio di uno che riparte da zero.

## Parte 2 — `PredictionCalibrationStore` (nuovo, in `NosAi.Storage/`)

Stessa forma di `ActionOutcomeLedgerStore`, senza inventarne un'altra:

- costruttore `(string databasePath, SqliteJournalOptions options)`;
- `OpenFromVolume(options)` e **`TryOpenFromVolume(options, out string? failureReason)`**;
- `Save(snapshot)` e `Load()`;
- `CREATE TABLE IF NOT EXISTS` con la chiave sul `ContextKey`;
- `IDisposable`.

Il volume è quello documentato: `VolumeLocator` risolve
`<NOSAI-SSD>:\NosAi\data\db\`. Usa `TryResolveDatabasePath(options, out path,
out reason)` dove non devi creare nulla.

## Parte 3 — `--learning-report` (nuovo, in `Runtime/Observability/`)

Sul modello di `OutcomeReportCommand`:

- senza opzioni: una riga per contesto — chiave, accuratezza attesa, **numero di
  prove**, confermate, smentite, ignorate, errore medio assoluto;
- `--context <chiave>`: solo quel contesto, con una ragione esplicita se non
  esiste;
- **ordine**: peggio calibrato per primo, come `Weakest` già fa e per la ragione
  che il suo commento dà;
- **l'accuratezza non si stampa mai da sola.** `Calibration.Trials` esiste
  apposta: un 100% su due prove e un 100% su duecento sono due affermazioni
  diverse, e una tabella che le mostra uguali verrà creduta uguale.

**Il caso che conta di più**: se il file non esiste, il comando **dice che non è
mai stato scritto e non lo crea** — con una costante di ragione, come
`LedgerNotCreatedReason`. Un lettore che crea un archivio vuoto e poi riferisce
«nessun dato» ha appena cancellato la differenza fra *non ho mai imparato* e
*non ho mai salvato*.

## Parte 4 — chi scrive, e quando

Sull'orchestrator (`Gate3Runtime.cs`), due metodi **additivi**, niente altro:

- `RestoreCalibration(...)` — chiama `ImportCalibration` con ciò che lo store ha;
- `SaveCalibration(...)` — chiama `ExportCalibration` e lo passa allo store.

**Nessuna scrittura dentro il ciclo di decisione.** `Record` resta senza I/O:
una `SELECT` di SQLite dentro `Observe → … → Execute` mette il disco sul
percorso critico della sicurezza.

Poi **guarda** dove il processo vivo si ferma: `Gate1BootstrapHost.StartAsync`
(riga 392) ha un `finally` dopo il ciclo (riga 1019). Se è un punto di chiusura
reale, aggancia lì `SaveCalibration` e `RestoreCalibration` all'avvio. **Se non
lo è, non inventare un ciclo di vita**: lascia i due metodi pubblici e testati,
non toccare `Gate1BootstrapHost.cs`, e scrivi nel report cosa hai trovato. Un
salvataggio agganciato al punto sbagliato perde i dati e sembra funzionare.

---

## Test (file nuovi in `tests/`)

1. Esporta, importa in un registro nuovo: `CalibrationOf` dà gli stessi numeri.
2. Le previsioni aperte **non** sopravvivono al giro: `OpenPredictions == 0`
   dopo l'import.
3. La scelta della Parte 1 (rimpiazza o somma) fa quello che dice: importa due
   volte lo stesso snapshot e asserisci il risultato voluto.
4. Un outcome non-`Live` resta contato e **non** sposta l'evidenza — anche
   dopo un giro di salvataggio e ricarica. È la regola del file, e un archivio
   che la perdesse la farebbe perdere a tutto il progetto.
5. Store: salva e ricarica su un file temporaneo; contesti con caratteri
   scomodi (spazi, `'`, non-ASCII) tornano identici.
6. `--learning-report` su un percorso dove il file non esiste: esce con la
   ragione «mai creato» **e il file continua a non esistere** — asserisci
   `File.Exists == false` dopo il comando.
7. `--learning-report --context <inesistente>`: ragione esplicita, non tabella
   vuota.
8. La tabella riporta `Trials` per ogni riga.

Dove serve un volume vero usa `[QuiescedMachineFact]` / gli attributi che
saltano **visibilmente**; **mai** `if (…) return`.

## Fuori scope

- **Nessun consumatore della calibrazione.** Non far leggere `Weakest` al
  pianificatore o al ranker: dare a un numero appena reso persistente autorità
  sulle decisioni è una scelta di architettura, non un'aggiunta.
- **Nessuna modifica al ciclo di `Gate3ExecutionOrchestrator`** oltre ai due
  metodi additivi.
- **Nessun aggiornamento ai documenti** — REGOLA #2.

## Definition of done

- Build 0/0; `NosAi.Runtime.Tests`, `NosAi.Core.Tests`, `NosAi.Storage` (se ha
  suite) — 0 falliti, totali riportati.
- L'esito della Parte 4: punto di chiusura trovato o no, e cosa hai deciso.
- La scelta rimpiazza/somma, con il motivo.
- L'output reale di `--learning-report` in due situazioni: archivio assente, e
  archivio con almeno due contesti.
- Livello: **Integrated** (`Verified` vorrebbe una sessione vera che chiude e
  riapre ritrovando la propria calibrazione).
