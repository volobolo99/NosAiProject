# AP-09 / A2+A4 — DeepSeek — Action-outcome ledger: persistence + wiring

## Why this task exists

`AP-09_A1_STATUS.md` §"AP-09/A2+A4 — indagine mirata" found the ledger
genuinely blocked by two contract gaps, not by missing wiring: no
producer of `WorldAction` existed anywhere in `src/`, and
`ActionOutcomeLedgerEntry.Outcome` (a naked `ActionOutcome`) had no room
for `Unobserved`/`Aborted` — the real outcome of most rounds against the
armed production gate today. §"Correzione di contratto" (same file, done
this session, on `main`) fixed both: `ActionOutcomeLedgerEntry.Outcome`
is now `WorldFact<ActionOutcome>`, and
`src/NosAi.Core/WorldModel/WorldActionProjector.cs` (new) projects the
two execution-evidence shapes this project already produces for real
(`CombatExecutionEvidence`, `MovementExecutionEvidence`) into a real
`WorldAction`.

**This task is the wiring that fix unblocked**: a durable SQLite store
for `ActionOutcomeLedgerEntry` (the same pattern `MapModelStore` already
proves, AP-03), and a call to `WorldActionProjector` + that store after
each round of `--engage`, `--recover` and `--scout` — including when
`--autoplay` (AP-08) dispatches those same rounds. No new operator flag,
no new console command: this is pure runtime wiring inside commands that
are already built, audited and integrated.

## Already built and real — read before writing code

- `src/NosAi.Core/WorldModel/WorldActionProjector.cs` (already on
  `main`): `FromCombat(ActionId, CombatActionCandidate, DateTime
  issuedAtUtc, CombatExecutionEvidence)`, `FromMovement(ActionId, string
  kind, DateTime issuedAtUtc, MovementExecutionEvidence)`,
  `ToLedgerEntry(WorldAction, MemoryType, string context, DateTime
  recordedAtUtc, Guid? entryId = null)`. Read all three in full — this
  task calls them, never reimplements their mapping.
- `src/NosAi.Core/Memory/ActionOutcomeLedger.cs`: `ActionOutcomeLedgerEntry`
  (now `WorldFact<ActionOutcome> Outcome`), `MemoryType` (10 values,
  `Combat`/`Spatial` are the two this task uses).
- `src/NosAi.Storage/MapModelStore.cs` (already on `main`): the exact
  persistence template to mirror — `SqliteConnection` opened with
  `Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false`,
  `ApplyPolicyOrThrow` (WAL/FULL/busy_timeout=5000, independently
  re-read and verified, never assumed), `EnsureSchema` via `CREATE TABLE
  IF NOT EXISTS`, `OpenFromVolume(SqliteJournalOptions)` via
  `VolumeLocator.ResolveDatabasePath`. Copy `ApplyPolicyOrThrow`/
  `Execute`/`ExecuteScalarString`/`ExecuteScalarInt64` verbatim (private,
  identical bodies) — do not deviate from this project's own already-audited
  durability discipline.
- `src/NosAi.Storage/SqliteJournalOptions.cs`/`VolumeLocator.cs`: the
  shared labeled-volume resolution (`VolumeLabel = "NOSAI-SSD"`,
  `FileName = "nosai.db"` by default — the same physical file
  `MapModelStore`/`SqliteEventJournal` already use; a new table in the
  same file is the correct choice, not a second database file).
- `src/NosAi.Runtime/Navigation/CollectCommand.cs`, static nested class
  `LiveScope`, method `TryOpen(int processId, out string? failureReason)`
  → `LiveScope?`: the exact naming/shape convention to mirror for a
  "try, return null with a reason on failure" static factory (§1 below
  uses the identical shape for `ActionOutcomeLedgerStore.TryOpenFromVolume`).
- `src/NosAi.Runtime/Tactical/EngageCommand.cs`, `RecoverCommand.cs`,
  `src/NosAi.Runtime/Navigation/ScoutCommand.cs`,
  `src/NosAi.Runtime/Tactical/AutoplayCommand.cs`: read every `RunWindows`
  method in full before touching it. §3 gives exact anchors; do not
  restructure anything these anchors do not name.

## OWN (new files only)

- `src/NosAi.Storage/ActionOutcomeLedgerStore.cs` (A4)
- `src/NosAi.Runtime/WorldModel/Fusion/ActionOutcomeRecorder.cs` (A2)
- `tests/NosAi.Core.Tests/ActionOutcomeLedgerStoreTests.cs` (same
  directory as the existing `MapModelStoreTests.cs`, same real-temp-file
  pattern — no test project changes needed, `NosAi.Core.Tests` already
  references `NosAi.Storage`)
- `tests/NosAi.Runtime.Tests/WorldModel/Fusion/ActionOutcomeRecorderTests.cs`
  (same directory as `CombatVerificationProjectorTests.cs`)

## MODIFY (existing files, surgical additions only)

- `src/NosAi.Runtime/Tactical/EngageCommand.cs`
- `src/NosAi.Runtime/Tactical/RecoverCommand.cs`
- `src/NosAi.Runtime/Navigation/ScoutCommand.cs`
- `src/NosAi.Runtime/Tactical/AutoplayCommand.cs`

Every change to these four files is inside their own `RunWindows`
method only — the same "untested-by-design shell" every one of these
files' own doc comments already says has no unit test
(`ScoutCommand.RunWindows`'s own remark: "a thin, untested-by-design
shell, exactly like `WalkCommand.RunWindows`"). **Do not change any
`ExecuteOneRound`/`ExecuteOneCycle` signature, parameter, or return
type in any of the four files.** No existing test in
`EngageCommandTests.cs`/`RecoverCommandTests.cs`/`ScoutCommandTests.cs`
(if it exists)/`AutoplayCommandTests.cs` needs to change because of
this task — if you find yourself editing one of those test files,
stop and re-read this section.

## Scope, stated explicitly

- Covers exactly three commands' rounds/steps: `--engage` (per round),
  `--recover` (per round), `--scout` (per emitted step) — including both
  of the latter two when dispatched by `--autoplay`.
- **`CollectCommand` is deliberately out of scope.** Its
  `ExecuteOneRound` returns `(WalkRun, WorldFact<int> Before, WorldFact<int>
  After)` — a shape `WorldActionProjector` has no method for
  (`FromCombat`/`FromMovement` both expect a `CombatExecutionEvidence`/
  `MovementExecutionEvidence`, neither of which `CollectCommand` produces).
  Wiring it would need a third `WorldActionProjector` method, a further
  contract decision outside this task's authorization — do not add one.
  Do not touch `CollectCommand.cs`.
- Recording is **opportunistic, never blocking**: if the `NOSAI-SSD`
  volume is not attached, every one of `--engage`/`--recover`/`--scout`/
  `--autoplay` must keep running exactly as it does today (a `[WARN]`
  line, nothing more) — ledger persistence is history, not a gate, and
  must never turn into a fourth reason one of these commands refuses.
- **No read path is wired anywhere in this task.**
  `ActionOutcomeLedgerStore.LoadByContext` exists (§1) and is tested
  directly, but no command calls it and nothing calls
  `LocalOutcomeSimulator.Predict` against a loaded ledger — deciding
  which command should consult a prediction, and for what decision, is
  a future task, not this one.

## 1. `ActionOutcomeLedgerStore` (A4, `NosAi.Storage`)

Append-only, one row per `ActionOutcomeLedgerEntry`. Unlike
`MapModelStore`, **no JSON/DTO layer is needed**:
`ActionOutcomeLedgerEntry` has no nested `EquatableArray<T>` — every
field is a scalar or one flat `WorldFact<ActionOutcome>`, so every field
maps to its own plain SQL column.

```csharp
using Microsoft.Data.Sqlite;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;

namespace NosAi.Storage;

/// <summary>
/// Durable, append-only store for <see cref="ActionOutcomeLedgerEntry"/>
/// (docs/ROADMAP_ESECUTIVA.md S:AP-09 "Action-outcome ledger") -- the
/// persistence <see cref="LocalOutcomeSimulator"/>'s own remarks name as
/// "a future runtime-wiring concern". Same durability discipline as
/// <see cref="MapModelStore"/>/<see cref="SqliteEventJournal"/>: WAL,
/// FULL synchronous, busy_timeout=5000, applied and independently
/// re-verified before any table is touched.
/// </summary>
public sealed class ActionOutcomeLedgerStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public ActionOutcomeLedgerStore(string databasePath, SqliteJournalOptions options)
    {
        // Identical shape to MapModelStore's constructor: validate args,
        // create the directory, open with Mode=ReadWriteCreate/Pooling=false,
        // ApplyPolicyOrThrow, EnsureSchema, dispose-and-rethrow on failure.
    }

    /// <summary>Opens the store at the path resolved from <paramref name="options"/>'s labeled volume.</summary>
    /// <exception cref="InvalidOperationException">The labeled volume is not attached.</exception>
    public static ActionOutcomeLedgerStore OpenFromVolume(SqliteJournalOptions options) =>
        new(VolumeLocator.ResolveDatabasePath(options), options);

    /// <summary>
    /// <see cref="OpenFromVolume"/>, but returns <see langword="null"/>
    /// with a reason instead of throwing when the volume is not attached --
    /// the same "Try, return null with a reason" shape
    /// <c>NosAi.Runtime.Navigation.CollectCommand.LiveScope.TryOpen</c>
    /// already uses for an equally optional live resource.
    /// </summary>
    public static ActionOutcomeLedgerStore? TryOpenFromVolume(SqliteJournalOptions options, out string? failureReason)
    {
        try
        {
            failureReason = null;
            return OpenFromVolume(options);
        }
        catch (InvalidOperationException ex)
        {
            failureReason = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Appends <paramref name="entry"/>. The ledger is append-only --
    /// unlike <see cref="MapModelStore.Save"/>'s intentional upsert, a
    /// second call with the same <see cref="ActionOutcomeLedgerEntry.EntryId"/>
    /// is a caller bug and is rejected by the primary key, never silently
    /// overwritten.
    /// </summary>
    public void Append(ActionOutcomeLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO action_outcome_ledger
                    (entry_id, action_id, category, context,
                     outcome_has_observed_value, outcome_value, outcome_source,
                     outcome_confidence, outcome_observed_at_unix_millis, outcome_reason,
                     recorded_at_unix_millis)
                VALUES
                    ($entryId, $actionId, $category, $context,
                     $outcomeHasObservedValue, $outcomeValue, $outcomeSource,
                     $outcomeConfidence, $outcomeObservedAt, $outcomeReason,
                     $recordedAt)
                """;
            command.Parameters.AddWithValue("$entryId", entry.EntryId.ToString());
            command.Parameters.AddWithValue("$actionId", entry.ActionId.Value);
            command.Parameters.AddWithValue("$category", (int)entry.Category);
            command.Parameters.AddWithValue("$context", entry.Context);
            command.Parameters.AddWithValue("$outcomeHasObservedValue", entry.Outcome.HasObservedValue ? 1 : 0);
            command.Parameters.AddWithValue("$outcomeValue", entry.Outcome.HasObservedValue ? (int)entry.Outcome.Value : DBNull.Value);
            command.Parameters.AddWithValue("$outcomeSource", (int)entry.Outcome.Source);
            command.Parameters.AddWithValue("$outcomeConfidence", entry.Outcome.Confidence);
            command.Parameters.AddWithValue("$outcomeObservedAt", ToUnixMillis(entry.Outcome.ObservedAtUtc));
            command.Parameters.AddWithValue("$outcomeReason", (object?)entry.Outcome.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$recordedAt", ToUnixMillis(entry.RecordedAtUtc));
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Loads every entry recorded for <paramref name="context"/> (ordinal
    /// match, same comparison <see cref="LocalOutcomeSimulator.Predict"/>
    /// already uses), oldest first. Not called by any command in this
    /// task -- see the task's own "no read path" scope note.
    /// </summary>
    public EquatableArray<ActionOutcomeLedgerEntry> LoadByContext(string context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        lock (_lock)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = """
                SELECT entry_id, action_id, category, context,
                       outcome_has_observed_value, outcome_value, outcome_source,
                       outcome_confidence, outcome_observed_at_unix_millis, outcome_reason,
                       recorded_at_unix_millis
                FROM action_outcome_ledger
                WHERE context = $context
                ORDER BY recorded_at_unix_millis ASC
                """;
            command.Parameters.AddWithValue("$context", context);

            var entries = new List<ActionOutcomeLedgerEntry>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                entries.Add(ReadEntry(reader));

            return EquatableArray<ActionOutcomeLedgerEntry>.From(entries);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }

    // Read every column by name via reader.GetOrdinal("..."), never by a
    // positional integer literal -- a column-order slip is exactly the kind
    // of silent bug a hardcoded index would hide.
    private static ActionOutcomeLedgerEntry ReadEntry(SqliteDataReader reader)
    {
        bool hasObservedValue = reader.GetInt32(reader.GetOrdinal("outcome_has_observed_value")) != 0;
        var outcome = new WorldFact<ActionOutcome>(
            hasObservedValue ? (ActionOutcome)reader.GetInt32(reader.GetOrdinal("outcome_value")) : default,
            (DataSourceKind)reader.GetInt32(reader.GetOrdinal("outcome_source")),
            reader.GetDouble(reader.GetOrdinal("outcome_confidence")),
            FromUnixMillis(reader.GetInt64(reader.GetOrdinal("outcome_observed_at_unix_millis"))),
            hasObservedValue,
            reader.IsDBNull(reader.GetOrdinal("outcome_reason")) ? null : reader.GetString(reader.GetOrdinal("outcome_reason")));

        return new ActionOutcomeLedgerEntry(
            Guid.Parse(reader.GetString(reader.GetOrdinal("entry_id"))),
            new ActionId(reader.GetString(reader.GetOrdinal("action_id"))),
            (MemoryType)reader.GetInt32(reader.GetOrdinal("category")),
            outcome,
            reader.GetString(reader.GetOrdinal("context")),
            FromUnixMillis(reader.GetInt64(reader.GetOrdinal("recorded_at_unix_millis"))));
    }

    private static long ToUnixMillis(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime FromUnixMillis(long unixMillis) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMillis).UtcDateTime;

    private void EnsureSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS action_outcome_ledger (
                entry_id                       TEXT PRIMARY KEY,
                action_id                       TEXT NOT NULL,
                category                        INTEGER NOT NULL,
                context                         TEXT NOT NULL,
                outcome_has_observed_value      INTEGER NOT NULL,
                outcome_value                   INTEGER NULL,
                outcome_source                  INTEGER NOT NULL,
                outcome_confidence              REAL NOT NULL,
                outcome_observed_at_unix_millis INTEGER NOT NULL,
                outcome_reason                  TEXT NULL,
                recorded_at_unix_millis         INTEGER NOT NULL
            )
            """);
        Execute("CREATE INDEX IF NOT EXISTS idx_action_outcome_ledger_context ON action_outcome_ledger(context)");
    }

    // ApplyPolicyOrThrow(SqliteJournalOptions)/Execute(string)/
    // ExecuteScalarString(string)/ExecuteScalarInt64(string): copy verbatim
    // from MapModelStore.cs (private, byte-for-byte identical bodies --
    // this store's durability policy must be the exact same one already
    // audited there, not a re-derivation).
}
```

**Test** (`tests/NosAi.Core.Tests/ActionOutcomeLedgerStoreTests.cs`, same
shape as `MapModelStoreTests.cs`: real temp-file database, never
`:memory:`, `Dispose()` calls `SqliteConnection.ClearAllPools()` then
deletes the file):

- Round-trips a fully-populated entry (`Outcome.HasValue == true`,
  every field distinct) through `Append` + `LoadByContext` unchanged,
  field for field.
- Round-trips an entry whose `Outcome` is `WorldFact<ActionOutcome>.Unknown(reason)`
  — `HasObservedValue == false`, `Reason` preserved, `Value` never read
  back as a fabricated `ActionOutcome` member. **This is the exact case
  the AP-09 contract fix exists for — do not skip it.**
- `LoadByContext` returns entries for one context in `RecordedAtUtc`
  ascending order, and excludes entries recorded under a different
  context (ordinal, case-sensitive — mirror
  `LocalOutcomeSimulatorTests.Predict_ContextMatchIsOrdinal_CaseSensitive`).
- `LoadByContext` for a context with no entries returns
  `EquatableArray<ActionOutcomeLedgerEntry>.Empty`, never throws.
- Constructor verifies `journal_mode=WAL`/`synchronous=FULL`/
  `busy_timeout=5000` were actually applied (same assertions
  `MapModelStoreTests` already makes against `MapModelStore`).

## 2. `ActionOutcomeRecorder` (A2, `NosAi.Runtime.WorldModel.Fusion`)

```csharp
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Storage;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Records one already-executed act's evidence into the durable
/// action-outcome ledger, via <see cref="NosAi.Core.WorldModel.WorldActionProjector"/>
/// and <see cref="ActionOutcomeLedgerStore"/>. A <see langword="null"/>
/// <paramref name="store"/> is a documented no-op: ledger recording is
/// opportunistic history, never a reason to fail a command whose actual
/// act already ran and was already verified before this call runs.
/// </summary>
public static class ActionOutcomeRecorder
{
    public static void RecordCombat(
        ActionOutcomeLedgerStore? store,
        ActionId id,
        CombatActionCandidate candidate,
        DateTime issuedAtUtc,
        CombatExecutionEvidence evidence,
        MemoryType category,
        string context,
        DateTime recordedAtUtc)
    {
        if (store is null) return;

        WorldAction action = NosAi.Core.WorldModel.WorldActionProjector.FromCombat(id, candidate, issuedAtUtc, evidence);
        store.Append(NosAi.Core.WorldModel.WorldActionProjector.ToLedgerEntry(action, category, context, recordedAtUtc));
    }

    public static void RecordMovement(
        ActionOutcomeLedgerStore? store,
        ActionId id,
        string kind,
        DateTime issuedAtUtc,
        MovementExecutionEvidence evidence,
        MemoryType category,
        string context,
        DateTime recordedAtUtc)
    {
        if (store is null) return;

        WorldAction action = NosAi.Core.WorldModel.WorldActionProjector.FromMovement(id, kind, issuedAtUtc, evidence);
        store.Append(NosAi.Core.WorldModel.WorldActionProjector.ToLedgerEntry(action, category, context, recordedAtUtc));
    }
}
```

**Test** (`tests/NosAi.Runtime.Tests/WorldModel/Fusion/ActionOutcomeRecorderTests.cs`,
a real temp-file `ActionOutcomeLedgerStore`, same disposal pattern as §1):

- `RecordCombat`/`RecordMovement` with a `null` store: no exception, no
  file created.
- `RecordCombat` with a real store: `store.LoadByContext(context)` then
  returns exactly one entry whose `ActionId`/`Category`/`Context`/
  `Outcome` match what `WorldActionProjector.FromCombat`/`ToLedgerEntry`
  would produce directly for the same inputs (compute the expected
  value the same way `WorldActionProjectorTests.cs` already does, then
  assert equality — do not re-derive the mapping a second, independent
  way).
- Same for `RecordMovement`.

## 3. Wiring the four commands

Every insertion below is additive: existing lines are never removed or
reordered except where explicitly shown. Add `using NosAi.Core.Memory;`
and `using NosAi.Storage;` to every file in this section that does not
already have them (none currently do).

### 3a. `EngageCommand.cs`

In `RunWindows`, immediately after this existing line:

```csharp
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
```

insert:

```csharp

            using ActionOutcomeLedgerStore? ledgerStore =
                ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? ledgerFailure);
            if (ledgerStore is null)
                Console.WriteLine($"[WARN] action_outcome_ledger_unavailable:{ledgerFailure}");
```

Inside the `for (int round = 1; round <= rounds; round++)` loop, change:

```csharp
                CombatExecutionEvidence evidence = ExecuteOneRound(
                    candidate,
                    keybinds,
                    gated,
                    readVitals,
                    verificationDelay: () => Thread.Sleep(VerificationDelayMs),
                    in authority,
                    nowUtc: TimeProvider.System.GetUtcNow().UtcDateTime);

                PrintEvidence(evidence);
```

to:

```csharp
                DateTime nowUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
                CombatExecutionEvidence evidence = ExecuteOneRound(
                    candidate,
                    keybinds,
                    gated,
                    readVitals,
                    verificationDelay: () => Thread.Sleep(VerificationDelayMs),
                    in authority,
                    nowUtc);

                PrintEvidence(evidence);

                NosAi.Runtime.WorldModel.Fusion.ActionOutcomeRecorder.RecordCombat(
                    ledgerStore,
                    new ActionId(Guid.NewGuid().ToString("N")),
                    candidate,
                    issuedAtUtc: nowUtc,
                    evidence,
                    MemoryType.Combat,
                    context: $"skill:{skillId}",
                    recordedAtUtc: nowUtc);
```

(`EngageCommand.cs` has no `using NosAi.Runtime.WorldModel.Fusion;` —
call `ActionOutcomeRecorder` fully qualified, the same convention this
file already uses one line below for
`NosAi.Runtime.WorldModel.Fusion.CombatVerificationProjector.Project`.)

### 3b. `RecoverCommand.cs`

Same two changes, same anchors, same reasoning as 3a:

1. After `ActuationAuthority authority = ActuationAuthority.Commanded(Flag);`,
   insert the identical `using ActionOutcomeLedgerStore? ledgerStore = ...`
   block from 3a.
2. Inside the round loop, change the `nowUtc: TimeProvider.System.GetUtcNow().UtcDateTime`
   inline argument to a `DateTime nowUtc = ...;` local declared just
   before the `ExecuteOneRound` call (reusing it as the plain `nowUtc`
   argument), then after `PrintEvidence(evidence);` add:

```csharp
                NosAi.Runtime.WorldModel.Fusion.ActionOutcomeRecorder.RecordCombat(
                    ledgerStore,
                    new ActionId(Guid.NewGuid().ToString("N")),
                    candidate,
                    issuedAtUtc: nowUtc,
                    evidence,
                    MemoryType.Combat,
                    context: $"consumable-slot:{slot}",
                    recordedAtUtc: nowUtc);
```

(`candidate` here is the one already built earlier in this same loop
iteration — `RecoverCommand.RunWindows` builds it fresh per round,
unlike `EngageCommand` which builds it once outside the loop; use
whichever the file already has in scope at that point, do not build a
second one.)

### 3c. `ScoutCommand.cs`

In `RunWindows`, immediately after:

```csharp
            ExplorationFootprint footprint = ExplorationFootprint.Empty(
                new MapId("unknown-map"), "scout_command_session_start");
```

insert:

```csharp

            using ActionOutcomeLedgerStore? ledgerStore =
                ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? ledgerFailure);
            if (ledgerStore is null)
                Console.WriteLine($"[WARN] action_outcome_ledger_unavailable:{ledgerFailure}");
```

Change the existing `onEvidence` lambda passed to `ExecuteOneRound`
from:

```csharp
                    onEvidence: evidence =>
                    {
                        string detail = evidence.Detail is { } named ? $" ({named})" : string.Empty;
                        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"step-evidence: {evidence.Result} requested={evidence.Requested.Column},{evidence.Requested.Row}{detail}"));
                    },
```

to:

```csharp
                    onEvidence: evidence =>
                    {
                        string detail = evidence.Detail is { } named ? $" ({named})" : string.Empty;
                        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"step-evidence: {evidence.Result} requested={evidence.Requested.Column},{evidence.Requested.Row}{detail}"));

                        ActionOutcomeRecorder.RecordMovement(
                            ledgerStore,
                            new ActionId(Guid.NewGuid().ToString("N")),
                            "scout-step",
                            issuedAtUtc: now,
                            evidence,
                            MemoryType.Spatial,
                            context: $"scout:{roundMap.Id.Value}",
                            recordedAtUtc: now);
                    },
```

(`ScoutCommand.cs` already has `using NosAi.Runtime.WorldModel.Fusion;`
— call `ActionOutcomeRecorder` unqualified, matching the file's
existing use of `MovementVerificationProjector` a few lines above. Both
`now` and `roundMap` are already in scope at this exact point — do not
introduce new locals for them.)

### 3d. `AutoplayCommand.cs`

In `RunWindows`, immediately after:

```csharp
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
```

insert the identical `using ActionOutcomeLedgerStore? ledgerStore = ...`
block from 3a.

Change the existing `onEvidence` lambda passed to `ExecuteOneCycle`
(movement side) the same way as 3c — same added `RecordMovement` call,
`context: $"scout:{map.Id.Value}"` (this file's per-cycle map variable
is named `map`, not `roundMap`), `issuedAtUtc`/`recordedAtUtc: now`
(this file's per-cycle timestamp local).

In the `case AutoplayDispatch.Recovered:` block, change:

```csharp
                    case AutoplayDispatch.Recovered:
                    {
                        CombatExecutionEvidence evidence = result.RecoverEvidence!;
                        RecoverCommand.PrintEvidence(evidence);
```

to:

```csharp
                    case AutoplayDispatch.Recovered:
                    {
                        CombatExecutionEvidence evidence = result.RecoverEvidence!;
                        RecoverCommand.PrintEvidence(evidence);

                        // evidence.Candidate is the exact CombatActionCandidate
                        // ExecuteOneCycle's own Survival branch built from
                        // recoverSlot -- reused here rather than rebuilt, so the
                        // recorded action is provably the one that was actually
                        // pressed, not a second, independently-constructed guess.
                        ActionOutcomeRecorder.RecordCombat(
                            ledgerStore,
                            new ActionId(Guid.NewGuid().ToString("N")),
                            evidence.Candidate,
                            issuedAtUtc: now,
                            evidence,
                            MemoryType.Combat,
                            context: $"consumable-slot:{recoverSlot!.Value}",
                            recordedAtUtc: now);
```

leaving the rest of that `case` block (the `ResourceGainConfirmed`/
`Aborted` checks) exactly as it is today. `AutoplayCommand.cs` already
has `using NosAi.Runtime.WorldModel.Fusion;` — call `ActionOutcomeRecorder`
unqualified. Do not add a new field to `AutoplayCycleResult` — it
already carries everything this needs (`evidence.Candidate`).

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build NosAi.sln -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ActionOutcomeLedgerStoreTests"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~ActionOutcomeRecorderTests"
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
```

All green, 0 warnings/0 errors, no regression in either full suite —
in particular, zero changes expected in
`EngageCommandTests`/`RecoverCommandTests`/`AutoplayCommandTests`'
own pass counts (§"MODIFY" above only touches untested `RunWindows`
shells).

## Known limitation, stated plainly

`RunWindows` in all four files has no unit test by design (same
convention `WalkCommand.RunWindows`/`ScoutCommand.RunWindows` already
document) — this task's wiring inside those methods is therefore
verified by (a) a clean build, (b) `ActionOutcomeLedgerStoreTests`/
`ActionOutcomeRecorderTests` proving the two new pure-enough units are
correct in isolation, and (c) no regression in the full suites. It is
**not** verified end-to-end against a real client in this task — same
honest limit every other command in this project already states about
its own `RunWindows`. Report `Present` for the two new files,
`Integrated` only once a human confirms a real `--engage`/`--recover`/
`--scout`/`--autoplay` run actually leaves rows in `action_outcome_ledger`
on a real machine with the `NOSAI-SSD` volume attached — do not claim
that here.

## Report back

Files created/modified; build/test evidence with exact pass counts;
verification level (`Present`) and why not `Integrated`; anything found
in `WorldActionProjector.cs`/`MapModelStore.cs`/the four `RunWindows`
methods that looks wrong (do not fix it yourself — report it).
