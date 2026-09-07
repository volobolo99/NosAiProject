# AP-09 / A2+A4 — DeepSeek — il registro degli esiti si scrive e non si legge

**Non in parallelo col task `wire_inspect`**: entrambi toccano `Program.cs`.
Indipendente dagli altri tre (nessun file in comune).

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — le tre regole assolute.
2. `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` — il commento in testa.
3. `CLAUDE.md` § *Autonomy requirements* — «Prediction is advisory only».

**REGOLA #3 su ogni riga**: quanto segue è stato verificato leggendo il codice il
2026-09-07. Se il codice dice altro, **vince il codice**.

**Cartella**: `C:\Users\volob\Desktop\NosAiProject`. `git pull` prima, **`git
push` tu alla fine.**

---

## Il fatto misurato

`ActionOutcomeLedgerStore` è un registro durevole, append-only, su SQLite, con
WAL e `synchronous=FULL`. **Quattro comandi ci scrivono davvero**:
`--scout` (`ScoutCommand.cs:324`), `--autoplay` (`AutoplayCommand.cs:457`),
`--engage` (`EngageCommand.cs:498`), `--recover` (`RecoverCommand.cs:247`). Ogni
atto reale del runtime lascia una riga.

Lo store espone due letture: `LoadByContext(string context)` e nient'altro.

```
grep -rn "LoadByContext" src/ --include=*.cs
```

**Nessun chiamante di produzione.** Il runtime scrive la propria storia in un
registro durevole e non ha modo di rileggerla. È la capacità che `CLAUDE.md`
chiama «learn from failures» con il percorso di scrittura completo e quello di
lettura assente.

### Il secondo fatto, che il rapporto deve saper dire

Su questa macchina **non esiste il volume `NOSAI-SSD`** (`Get-Volume` mostra il
solo `C:` etichettato `Acer`), e `SqliteJournalOptions` lo cerca per etichetta.
Quindi `TryOpenFromVolume` fallisce sempre qui, e il registro è **vuoto per
costruzione**: nessun atto mai eseguito su questa macchina ha lasciato una riga.

I quattro comandi lo gestiscono già bene e non vanno toccati — avvisano con
`[WARN] action_outcome_ledger_unavailable:<motivo>` e proseguono, e il commento
accanto dice perché non deve mai diventare un rifiuto. **Non cambiare quel
comportamento.**

Ma il rapporto nuovo deve distinguere per nome quattro situazioni che da fuori
si somigliano e non sono la stessa cosa:

| Situazione | Cosa significa |
|---|---|
| volume assente | il registro non esiste e non è mai esistito qui |
| volume presente, file assente | il volume c'è, nessuna esecuzione ha ancora scritto |
| store aperto, zero righe | è stato creato e nessun atto è stato registrato |
| store aperto, N righe | c'è una storia, ed è questa |

Collassarle in «nessun dato» è esattamente ciò che questo progetto rifiuta
ovunque: *unknown is not zero, false or empty*.

---

## Cosa fare

### 1. Una lettura che manca allo store

`ActionOutcomeLedgerStore` sa caricare **un** contesto ma non sa dire **quali**
contesti esistono, quindi un rapporto non può enumerarli. Aggiungi, in modo
additivo:

```csharp
/// <summary>Ogni contesto che ha almeno una riga, in ordine.</summary>
public EquatableArray<string> Contexts()
```

Una `SELECT DISTINCT`, ordinata, niente altro. Nessuna firma esistente cambia.

### 2. `--outcome-report`

```
--outcome-report                     un riepilogo per contesto
--outcome-report --context <nome>    le righe di quel contesto
```

Per ogni contesto: quante righe, la distribuzione degli esiti
(`WorldFact<ActionOutcome>.Value`), quante righe portano un esito **Unknown**
contate a parte, il primo e l'ultimo `RecordedAtUtc`, e la provenienza degli
esiti. Un esito `Unknown` non è un fallimento e non va sommato ai fallimenti: è
un atto il cui esito nessuno ha osservato, e il rapporto lo dice come tale.

Con `--context`, le righe di quel contesto in ordine di tempo:
`RecordedAtUtc`, `ActionId`, esito, provenienza.

### 3. Ciò che il rapporto non fa, e un test lo fissa

**Non alimenta niente.** Non tocca la pianificazione, il ranking, la Guard, la
Safety, `PredictionLedger`. `CLAUDE.md` dice che la previsione è consultiva; una
frequenza storica letta da un file lo è ancora di più. Questo è un rapporto per
un operatore, e il codice non deve offrire alcun modo di darlo in pasto a una
decisione.

---

## OWN (file nuovi)

- `src/NosAi.Runtime/Observability/OutcomeReportCommand.cs`
- `tests/NosAi.Runtime.Tests/OutcomeReportTests.cs`

## MODIFY

- `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` — **solo** `Contexts()`
- `src/NosAi.Runtime/Program.cs` — dispatch e `KnownProbeFlags`

**Nient'altro.** In particolare non i quattro comandi che scrivono nel registro,
niente in `src/NosAi.Core/`, e nessuno dei file degli altri task in coda.

---

## Struttura

Come `LoadoutReportCommand` e `DecideReplayCommand`:

- una funzione **pura** che prende uno `ActionOutcomeLedgerStore` già aperto (o
  `null`) più un `TextWriter`, e produce tutto il rapporto — è quella che i test
  esercitano, con uno store su file temporaneo;
- un `Run(string[] args)` sottile che prova ad aprire lo store dal volume,
  stampa il rifiuto nominato quando non ci riesce, e chiama la pura;
- rifiuti **nominati**, mai un'eccezione nuda: volume assente, `--context` senza
  valore, `--context` di un contesto che non esiste (che è diverso da un
  contesto esistente e vuoto).

Lo store ha un costruttore che prende un percorso esplicito
(`ActionOutcomeLedgerStore(string databasePath, SqliteJournalOptions)`): è
quello che rende i test interamente offline, senza volume e senza mock.

---

## Test — `OutcomeReportTests.cs` (nuovo)

Su un file temporaneo, veri `Append` e vera rilettura:

1. `Contexts()` su uno store vuoto risponde vuoto; con tre contesti risponde i
   tre, ordinati, senza duplicati.
2. Il riepilogo conta le righe per contesto e la distribuzione degli esiti.
3. **Un esito `Unknown` è contato a parte**, mai fra i fallimenti. Questo test
   vale più degli altri: è la riga che dice se il rapporto ha capito il contratto
   o ha solo sommato.
4. Primo e ultimo `RecordedAtUtc` sono quelli veri, anche con righe inserite
   fuori ordine di tempo.
5. `--context` di un contesto esistente stampa le sue righe in ordine di tempo;
   di uno inesistente rifiuta con un motivo **diverso** da quello di un contesto
   esistente e vuoto.
6. `[Theory]` sui rifiuti: `--context` senza valore, store non apribile.
7. Lo store è append-only: due `Append` con lo stesso `EntryId` non producono
   due righe, e il rapporto non le conta due volte. (Il rifiuto è già della
   chiave primaria — qui si verifica che il rapporto non lo mascheri.)

---

## Fuori scope

- **Non cambiare dove vive il database.** Che il registro stia su un volume
  dedicato per etichetta è una decisione di progetto: se ti sembra sbagliata,
  **scrivilo nel report** e lascia il codice com'è. Deciderla è di Claude e
  dell'utente.
- **Non toccare i quattro comandi che scrivono**, né il loro `[WARN]`.
- **Nessuna scrittura**: questo comando legge e basta. Nemmeno una migrazione.
- **Nessun consumo dei dati da parte di logica di decisione.**
- **Nessun aggiornamento ai documenti** — REGOLA #2.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errori, 0 avvisi**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` e
  `tests/NosAi.Core.Tests` — 0 falliti, numeri riportati.
- `--outcome-report` eseguito **davvero** su questa macchina: stamperà il
  rifiuto per volume assente, ed è l'esito atteso. Incolla l'uscita: è la prova
  che il percorso di rifiuto funziona ed è nominato.
- La stessa uscita per un percorso con uno store popolato dai test, così si vede
  anche il rapporto pieno.
- Una riga nel report che dice quante righe aveva il registro reale: **zero**, e
  perché.
- Livello: **Integrated**.
