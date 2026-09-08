# Q-107 — AP-09/A2+A4: Action-outcome ledger persistence + wiring

**Worker**: Qwen 2.5 Coder 7B local  
**Stato**: READY  
**Data**: 2026-09-09 00:15  

## Identificativo e obiettivo

Completare la persistenza e il wiring dell'Action-outcome Ledger per chiudere AP-09 da Present a Integrated. Tutti i contratti (`WorldActionProjector`, `ActionOutcomeLedgerEntry`) sono pronti su main. Manca solo:
1. `ActionOutcomeLedgerStore` (SQLite, pattern `MapModelStore`)
2. `ActionOutcomeRecorder` (compositore per gli outcome)
3. Cablaggio in `EngageCommand`, `RecoverCommand`, `ScoutCommand`, `AutoplayCommand`

## Stato di partenza

- `WorldActionProjector` esiste già: `src/NosAi.Core/WorldModel/WorldActionProjector.cs`
- `ActionOutcomeLedgerEntry` ha contratto finale: `src/NosAi.Core/Memory/ActionOutcomeLedger.cs`
- `MapModelStore` esiste come template di persistenza: `src/NosAi.Storage/MapModelStore.cs`
- `EngageCommand`, `RecoverCommand`, `ScoutCommand` compilano e passano i test senza il ledger
- File di specifica: `docs/agents/phases/ARCHIVE/AP-09_A2A4_DEEPSEEK_ledger_wiring.md`

## Perimetro

**Incluso (A2+A4)**:
- `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` (new, ~150 righe, specchio di `MapModelStore`)
- `src/NosAi.Runtime/WorldModel/Fusion/ActionOutcomeRecorder.cs` (new, ~80 righe)
- Modifiche chirurgiche: `EngageCommand.RunWindows`, `RecoverCommand.RunWindows`, `ScoutCommand.RunWindows`, `AutoplayCommand.RunWindows` (solo `WorldActionProjector.From*` + `recorder.Record`)
- `tests/NosAi.Core.Tests/ActionOutcomeLedgerStoreTests.cs` (new, ~60 righe, pattern `MapModelStoreTests`)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/ActionOutcomeRecorderTests.cs` (new, ~40 righe)

**Escluso**:
- Nessun cambio a WorldModel, contratti, WorldActionProjector, EngageCommand.Run (non-Windows)
- Nessun nuovo flag CLI o comando dell'operatore
- Nessuna modifica a quanto non nominato in "Incluso"

## Requisiti e interfacce

### ActionOutcomeLedgerStore (specchio di MapModelStore)

```csharp
namespace NosAi.Storage;

public sealed class ActionOutcomeLedgerStore : IDisposable
{
    /// <summary>Apri da volume dichiarato, o null con motivo se fallisce.</summary>
    public static ActionOutcomeLedgerStore? TryOpenFromVolume(
        SqliteJournalOptions options, out string? failureReason);
    
    /// <summary>Inserisci una riga ledger.</summary>
    public bool TryRecordEntry(ActionOutcomeLedgerEntry entry, out string? failureReason);
    
    /// <summary>Leggi tutte le righe in ordine (filtro opzionale).</summary>
    public IReadOnlyList<ActionOutcomeLedgerEntry> ReadAll(string? memoryTypeFilter = null);
    
    public void Dispose();
}
```

**Implementazione richiesta:**
- Schema SQLite: `CREATE TABLE IF NOT EXISTS ActionOutcomeLedger (id, entry_json, recorded_at_utc, memory_type, context)`
- Applica `ApplyPolicyOrThrow` identico a `MapModelStore` (WAL/FULL/busy_timeout=5000)
- Usa `VolumeLocator.ResolveDatabasePath` con stessa label "NOSAI-SSD", stesso file `nosai.db`
- Coppia `EnsureSchema`/`Execute`/`ExecuteScalarInt64` privata (identica a MapModelStore, non condivisa)
- TryRecordEntry serializza in JSON e fallisce gracefully con motivo su eccezione SQLite

### ActionOutcomeRecorder

```csharp
namespace NosAi.Runtime.WorldModel.Fusion;

public sealed class ActionOutcomeRecorder
{
    /// <summary>Inietta lo store. Nessun fallimento su null (skip gracefully).</summary>
    public ActionOutcomeRecorder(ActionOutcomeLedgerStore? store = null) { }
    
    /// <summary>Registra un atto di combattimento.</summary>
    public void RecordCombatAction(
        ActionId id, CombatActionCandidate candidate, 
        CombatExecutionEvidence evidence, DateTime issuedAtUtc);
    
    /// <summary>Registra un movimento.</summary>
    public void RecordMovement(
        ActionId id, string kind, MovementExecutionEvidence evidence, DateTime issuedAtUtc);
}
```

### Modifiche in EngageCommand, RecoverCommand, ScoutCommand

Ogni `RunWindows` (non `Run`) aggiunge:
```csharp
// Nel metodo, dopo aver ottenuto evidence e prima di return:
var projector = new WorldActionProjector();
var action = kind == "combat" 
    ? projector.FromCombat(actionId, candidate, issuedAtUtc, evidence)
    : projector.FromMovement(...);
recorder.Record(action);  // recorder passato nel costruttore o statico
```

Vincolo: nessuna eccezione da recorder blocca il comando (fail-closed: se lo store non c'è, si salta la registrazione).

## Criteri di accettazione

1. ✓ Build: `dotnet build NosAi.sln -c Release` → 0 errori/warning (warning preesistenti OK)
2. ✓ Test ActionOutcomeLedgerStore: 10+ test, tutti verdi
3. ✓ Test ActionOutcomeRecorder: 5+ test, tutti verdi
4. ✓ Nessuna regressione: `dotnet test tests/` rimane verde (2800 Runtime / 771 Core baseline)
5. ✓ Wiring verificato: almeno un test end-to-end che `--engage` → registrazione → lettura da store
6. ✓ Zero fallimenti su runtime privo di SSD etichettato (store = null, skip gracefully)

## Casi limite e comportamento atteso

- **Store assente**: TryOpenFromVolume ritorna null, recorder non lancia eccezioni, atto non registrato
- **Eccezione SQLite durante insert**: TryRecordEntry ritorna false, comando continua (non blocca)
- **Deserialization fallita**: entry omessa dal ReadAll, no crash
- **Comando senza action**: non registra nulla (comportamento OK)
- **Multiple runner concorrenti**: SQLite usa busy_timeout=5000, contesa risolta

## Materiale di riferimento

- Specifica completa: `docs/agents/phases/ARCHIVE/AP-09_A2A4_DEEPSEEK_ledger_wiring.md`
- Template MapModelStore: `src/NosAi.Storage/MapModelStore.cs` (~180 righe)
- Template test: `tests/NosAi.Core.Tests/MapModelStoreTests.cs` (~100 righe)
- WorldActionProjector: `src/NosAi.Core/WorldModel/WorldActionProjector.cs` (read-only)
- ActionOutcomeLedgerEntry: `src/NosAi.Core/Memory/ActionOutcomeLedger.cs` (contratto finale)

## Deliverable

1. `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` (completo)
2. `src/NosAi.Runtime/WorldModel/Fusion/ActionOutcomeRecorder.cs` (completo)
3. Modifiche in 4 command file (surgical, <5 righe ognuno)
4. 2 file test (completi)
5. Commit atomico con messaggio di chiusura AP-09/A2+A4

## Comandi di verifica

```bash
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release \
  --filter "ActionOutcomeLedgerStoreTests|ActionOutcomeRecorderTests"
dotnet test tests/ -c Release  # full suite
```

**Esito atteso**: Build 0/0, test >30 verdi, nessun fallito, nessuna regressione.

