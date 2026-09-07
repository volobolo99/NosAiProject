# AP-08 / A2+A4 — DeepSeek — publish the progression the wire already carries

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — the three absolute rules. **REGOLA #3
   applies to every line of this file too**: everything below was measured
   against the real recording on 2026-09-07, and the exact command that measured
   it is given. If something here disagrees with the code you are reading,
   **the code wins and you say so in the completion report** — do not bend the
   code to match this document.
2. `docs/PROTOCOLLO_NOSTALE.md` § `lev` and § *What this gives the runtime today*.
3. `CLAUDE.md` § *Architecture invariants* — in particular *Unknown is not zero,
   false or empty*.

**Working folder**: `C:\Users\volob\Desktop\NosAiProject`. It is now the only
copy. Pull before you start; **push when you are done — there is no automatic
push**, see the note in `DEEPSEEK_TASKS.md`.

---

## Why this task exists

`docs/PROTOCOLLO_NOSTALE.md:236` says it in one line:

> **Progression** — level and XP from `lev` (catalogued, not yet published).

The opcode is catalogued, its fields are classified by confidence, the
recordings carry it — and `NosTaleWorldProtocolDecoder` does not read it. The
census in `WorldChannelReplay` counts it among the packets the chain throws
away: today `--world-replay data/nostale_combat.noscap` prints

```
    => 8147/8211 pacchetti portano un opcode che il decoder legge
```

and the 64 missing ones include **23 `lev`**. AP-08 is progression, and the one
observation channel that already carries a level and an experience total is
being dropped on the floor.

This is the whole task: read it, classify it honestly, show it, and let the
census move. Nothing here actuates anything.

---

## The evidence — measured, not remembered

`lev` is in **three** of the five recordings, **38 packets in total**. All of
them, decoded through the shipping chain (`CaptureFile.Open` →
`GameTrafficCaptureEngine` → `NosTaleWorldDecoder.Decode`) on 2026-09-07:

```
data/nostale_combat.noscap   23 packets
   1: lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0
  23: lev 56 9690657 39 43754 18247900 185500 35106 7 0 0 1 0

data/certificazione.noscap   11 packets
   1: lev 56 9728169 39 46778 18247900 185500 35106 7 0 0 1 0
  11: lev 56 9731949 39 47018 18247900 185500 35106 7 0 0 1 0

data/nostale_live.noscap      4 packets
   1: lev 56 9708129 39 44858 18247900 185500 35106 7 0 0 1 0
   4: lev 56 9709347 39 44930 18247900 185500 35106 7 0 0 1 0
```

`data/nostale_01.noscap` (idle) and `data/equip_test.noscap` carry **zero**. That
is not an error — it is the other half of the evidence, and a test asserts it.

| # | Field | Across the 38 | Reading | Confidence |
|---:|---|---|---|---|
| 1 | level | `56` in all three sessions | character level | **probable** |
| 2 | xp | rises **strictly, every packet, in each session** | experience | **probable** |
| 3 | jobLevel | `39` in all three | job level | **probable** |
| 4 | jobXp | rises **strictly, every packet, in each session** | job experience | **probable** |
| 5 | xpMax | `18247900`, identical in all three | experience for the next level | **probable** |
| 6 | jobXpMax | `185500`, identical in all three | job experience for the next job level | **probable** |
| 7 | — | `35106`, identical in all three | candidate reputation | **unknown** |
| 8–12 | — | `7 0 0 1 0`, identical in all three | — | **unknown** |

**Fields 7 to 12 are not to be decoded**, and the three sessions are the reason
rather than an excuse. Fields 2 and 4 move within every session *and* between
sessions; fields 7 to 12 never move at all, in 38 packets across three separate
recordings. A value that has never once changed cannot be told apart from a
constant the server always sends, so the capture cannot confirm any meaning for
it. Anything you cannot read from the table above stays out of the contract
entirely — not `0`, not a nullable set to null "for now": absent.

## OWN (new files only)

- `tests/NosAi.Runtime.Tests/ProgressionFromLevTests.cs`

## MODIFY (nobody else is in these files — Claude will not touch them until you report)

- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`
- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- `src/NosAi.Runtime/LiveIntegration/Capture/WorldChannelReplay.cs`
- `src/NosAi.Runtime/Observability/WorldReplayCommand.cs`

**Do not touch anything else.** In particular: no file under `src/NosAi.Core/`
(that is Claude's lane), no `Program.cs` (this task adds no flag), nothing under
`src/NosAi.ControlPanel/`.

---

## 1. `PlayerProgression` — six fields, and no seventh

In `GameTrafficObserver.cs`, beside the other reading records
(`PlayerVitals`, `SkillReady`, `InventorySlotReading`, …), add:

```csharp
/// <summary>What the wire says about the character's progression.</summary>
/// <remarks>
/// Six fields, and deliberately not the other six the packet carries. Fields 7
/// through 12 of `lev` (`35106 7 0 0 1 0`) are identical in all 38 packets of
/// the three recordings that carry any, while fields 2 and 4 move in every one
/// of them. A value that has never once changed cannot be told apart from a
/// constant the server always sends, so nothing here can confirm a meaning for
/// it: it is not in this contract at all -- not zero, not a null "for now".
/// See docs/PROTOCOLLO_NOSTALE.md § lev.
/// </remarks>
public readonly record struct PlayerProgression(
    int Level,
    long Experience,
    long ExperienceForNextLevel,
    int JobLevel,
    long JobExperience,
    long JobExperienceForNextJobLevel);
```

Then extend `DecodedObservations` **additively**, as the last parameter:

```csharp
    PlayerTargetSelection? PlayerTarget = null,
    PlayerProgression? Progression = null)
```

and add `&& Progression is null` to `IsEmpty`.

Every existing construction site keeps compiling untouched — that is the point
of putting it last. If the compiler disagrees, stop and report it rather than
editing call sites to make room.

## 2. `DecodeProgression` — whole or nothing

In `NosTaleWorldProtocolDecoder.cs`, add to the dispatch (line ~113):

```csharp
            "lev" => DecodeProgression(fields, source, at),
```

The method:

- needs **at least 7 tokens** (opcode + 6 read fields); fewer → `DecodedObservations.Empty`;
- parses fields 1–6 with `int.TryParse` / `long.TryParse` and
  `CultureInfo.InvariantCulture`, exactly as the neighbouring decoders do;
- **refuses the packet whole** if any one of the six fails to parse, or if
  `Level <= 0`, `JobLevel <= 0`, `Experience < 0`, `JobExperience < 0`,
  `ExperienceForNextLevel <= 0`, `JobExperienceForNextJobLevel <= 0`. A half-read
  progression is the failure mode that matters here: a level with a garbage XP
  would be published as a fact;
- ignores every token after the sixth without inspecting it;
- returns `DecodedObservations.Empty with { Progression = … }` — no sighting, no
  event. `lev` describes the player, not an entity in view.

Follow `DecodeSkillReady` for the shape of "refuse whole"; it is the closest
existing sibling.

## 3. Carry it to the report

`NetworkObservationReport` already carries `PlayerEntityId`,
`PlayerMovementSpeed`, `Vitals` and the rest. Add `Progression` the same way,
with the same "most recent wins within a batch" rule the vitals use. Do not
invent a new aggregation: whatever `Vitals` does in `ObservePending`,
`Progression` does.

## 4. The census must move, and the operator must see it

**`WorldChannelReplay.cs`** — add `"lev"` to `ReadOpcodes`. Read the comment
above that array first: it says a stale entry there understates the chain in the
direction nobody checks. After this change the combat recording must report
**8170/8211**, not 8147. If it reports anything else, stop and report the number
you actually got.

Extend `WorldChannelReplaySummary` with the progression the replay saw — the
same shape as the vitals block already there: how many readings, and the range.
Level and job level are constant in this capture, so record the **set** of
values seen (like `maxHpValues`), not a min/max pair that would hide a second
level appearing.

**`WorldReplayCommand.cs`** — print it, under the vitals block:

```
  progressione (lev)     : 23 letture
    livello              : [56]
    esperienza           : 9688533..9690657  (su 18247900)
    livello di lavoro    : [39]
    esperienza lavoro    : 43226..43754  (su 185500)
```

This is the part that keeps the wiring from being inert: without a consumer, a
decoded fact that nothing prints is a fact nobody can check. Zero readings must
print a line saying zero, not nothing at all — the idle recording is a real case
and its silence has to be visible.

---

## Tests — `ProgressionFromLevTests.cs` (new)

Hand-built packets, using the `Packet(...)` helper idiom of
`CataloguedOpcodeTests.cs`:

1. The capture's own line decodes to the six values of the table above.
2. A `lev` with five fields is refused whole: `IsEmpty` and `Progression is null`.
3. `[Theory]` over malformed lines — a non-numeric level, a negative XP, a zero
   `xpMax`, a `jobLevel` of `0` — each refused whole. One `InlineData` per case,
   with the reason in a trailing comment.
4. The tokens after the sixth are ignored: the capture's line and the same line
   with fields 7–12 replaced by other numbers decode to the **same**
   `PlayerProgression`.
5. `Progression` does not make an otherwise empty observation non-empty by
   accident: a packet with an unknown opcode still yields `IsEmpty`.

Against the real recording, with the attribute that already exists for this
(`RecordedCaptureFactAttribute`, `tests/NosAi.Runtime.Tests/`) — **not** an
`if (…) return`, which xUnit counts as a pass:

6. `[RecordedCaptureFact("nostale_combat.noscap")]` — replaying the file yields
   **exactly 23** progression readings; the level is `56` in all of them; the
   experience of each reading is **strictly greater** than the previous one, and
   likewise the job experience. First and last are `9688533` and `9690657`.
   This is the cross-check CLAUDE.md § *External reference data* requires: a
   decoded field checked against a real observed value, not against the document
   that describes it.
7. The same, `[RecordedCaptureFact("certificazione.noscap")]`: **11** readings,
   first `9728169`, last `9731949`, strictly rising.
8. The same, `[RecordedCaptureFact("nostale_live.noscap")]`: **4** readings,
   first `9708129`, last `9709347`, strictly rising.
   Three sessions rather than one because a single capture cannot distinguish a
   field that is constant from a field that never had the chance to move — which
   is the entire argument for leaving fields 7 to 12 undecoded, and it deserves
   to be a test rather than a paragraph.
9. `[RecordedCaptureFact("nostale_01.noscap")]` — the idle recording yields
   **zero** progression readings, and the replay still reports `2490/2490`.

---

## Explicitly out of scope

- **No new operator flag.** `--world-replay` already exists; this only adds lines
  to what it prints.
- **No actuation, no Gate 4 wiring, no `WorldModelSnapshot`.** Publishing
  progression into the World Model contract is A1/A3 and belongs to Claude; doing
  it here would put two agents in `NosAi.Core`.
- **No decoding of fields 7–12**, and no "reputation" field however tempting
  `35106` looks.
- **No touching the other unread opcodes** (`sayi`, `eff`, `guri`, `msgi`,
  `icon`, `delay`, `cancel`, `ms_c`). They are a separate task and most are
  marked **unknown** in the protocol document; taking them now would mean
  guessing.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errors, 0 warnings**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` — 0 failures; report the
  numbers. The baseline before you start is **2361 passed / 9 skipped / 0
  failed**; the skips are platform and recording guards and must stay skips.
- `dotnet test tests/NosAi.Core.Tests -c Release` — unchanged, **708 / 1 / 0**.
- `dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --world-replay data/nostale_combat.noscap`
  run for real, and its progression block pasted into the completion report,
  together with the census line showing **8170/8211**.
- The same command on `data/nostale_01.noscap`, showing zero readings and
  `2490/2490`, and on `data/certificazione.noscap` (11 readings, census
  `11508/11528`) and `data/nostale_live.noscap` (4 readings, census
  `1905/1913`).
- `docs/PROTOCOLLO_NOSTALE.md:236` still says "catalogued, not yet published" —
  **leave it**: correcting the document is Claude's half (REGOLA #2), and it is
  the check that the two halves met.

Report, as always: files created/modified, what each test proves, the real
command output, verification level (this one can reach **Integrated** — never
`Verified`, because no operator has watched a level rise on a live client), and
any point where this document disagreed with the code.
