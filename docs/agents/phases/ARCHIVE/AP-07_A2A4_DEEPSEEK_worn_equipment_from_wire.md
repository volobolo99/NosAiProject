# AP-07 / A2+A4 — DeepSeek — read what the character is wearing, from the wire

**Da prendere DOPO `AP-08_A2A4_DEEPSEEK_progression_from_lev.md`**, non insieme:
i due task modificano gli stessi quattro file e due agenti sullo stesso file
sono vietati (`AGENT_WORK_PROTOCOL.md`). Quando quello è consegnato e pushato,
questo è pronto.

## Read this section before anything else

1. `docs/agents/DEEPSEEK_TASKS.md` — the three absolute rules. **REGOLA #3
   applies to every line of this file too**: every packet quoted below was
   extracted from the real recording on 2026-09-07 through the shipping chain.
   If something here disagrees with the code you read, **the code wins and you
   say so in the completion report**.
2. `docs/PROTOCOLLO_NOSTALE.md` — the opcode catalogue.
3. `CLAUDE.md` § *Architecture invariants* and § *External reference data*.

**Working folder**: `C:\Users\volob\Desktop\NosAiProject`. Pull first; **push
yourself at the end — there is no hook.**

---

## Why this task exists

`ADR-0027` measured it and `Q-104` wrote it down: **no production site has ever
populated `Player.Equipment`**. Every snapshot the runtime has produced says the
character is wearing nothing — not "unknown", *nothing* — and `LoadoutPlanner`
plans against that.

`Q-091` concluded the equipment channel needed an operator to run a test and
send back real lines. **That test was already recorded.**
`data/equip_test.noscap` is a session captured while equipping and unequipping,
and it carries 3 584 packets of which the decoder reads 3 541. The 43 it drops
are the equipment channel:

```
    letto ivn           6      eq            6      equip         6
          sc            6      pairy         6      bf            6
          dir           5      inv           3      eff           3
```

Nothing needs to be guessed and no operator needs to be waiting: the evidence is
on disk.

---

## The evidence — measured, not remembered

### `eq` — what is worn, as one line

All six are byte-identical:

```
eq 3443217 0 0 1 2 1 221.-1.262.157.224.279.-1.-1.-1.-1.-1 25 0 100
   ^own id                     ^ eleven dotted slots, -1 = empty
```

`3443217` is the session's own entity id — the same one `cond` reports and
`docs/PROTOCOLLO_NOSTALE.md` § *Entity type 1* confirms. The dotted group holds
**eleven** positions; six carry a vnum and five carry `-1`.

### `equip` — the same worn set, per slot, with detail

```
equip 25 0 0.262.5.2.0.0.0 2.221.0.0.0.0.0 4.715.0.2.0.0.0 5.157.0.0.0.0.0 \
         6.309.0.0.0.0.0 9.224.0.0.0.0.0 10.279.0.0.0.0.0 11.284.0.0.0.0.0 \
         12.902.0.5.0.0.0
```

Each group is `slot.vnum.<five more>`. Across the six `equip` packets the set of
groups **changes**, and that is what makes this capture worth more than a
document:

| # | groups present | difference from the previous |
|---:|---|---|
| 1 | 0, 2, 4, 5, 6, 9, 10, 11, 12 | — |
| 2 | 0, 2, 4, 5, 9, 10, 11, 12 | **slot 6 (vnum 309) gone** |
| 3 | 0, 2, 4, 5, 8, 9, 10, 11, 12 | slot 8 (vnum 518) appeared |
| 4 | 0, 2, 4, 5, 9, 10, 11, 12 | slot 8 gone |
| 5 | 0, 2, 4, 5, 9, 10, 12 | slot 11 (vnum 284) gone |
| 6 | 0, 2, 4, 5, 9, 10, 11, 12 | slot 11 back |

### `ivn` — and the same items arriving in the bag

```
ivn 0 15.309.0.0.0.0.0     <- vnum 309, the one that left equip slot 6
ivn 0 15.0.0.0.0.0.0       <- slot 15 emptied
ivn 0 16.518.0.0.0.0.0     <- vnum 518, the one that appeared in equip slot 8
ivn 0 16.0.0.0.0.0.0
ivn 0 18.284.0.0.0.0.0     <- vnum 284, the one that left equip slot 11
ivn 0 18.0.0.0.0.0.0
```

**This is the cross-check, and it is the point of the whole task.** Three vnums —
`309`, `518`, `284` — each leave an `equip` slot and appear in an `ivn` slot, or
the reverse, within the same capture. Nothing external is being trusted: the two
opcodes confirm each other. A decoder that reads either one wrongly breaks that
correspondence, and the test below is written to notice.

`ivn`'s first field is `0` in all six. `docs/PROTOCOLLO_NOSTALE.md` reads `ivn`
as `slot.vnum.amount.rarity` from a different capture (`ivn 2 34.2006.1.0`), so
**the first field is an inventory-kind selector and the group is not always four
parts long**. The existing `DecodeInventorySlot` already handles that line; do
not break it. Read what this capture adds and leave the rest as it is.

### What is **not** in scope, and why

| Opcode | Seen | Why it stays unread |
|---|---:|---|
| `sc` | 6 | 24 numeric fields, **byte-identical in all six**, nothing else in the capture moves with them. Unconfirmable. |
| `pairy` | 6 | identical in all six. Unconfirmable. |
| `bf` | 6 | five identical, one differs (`3.62.3` → `0.62.0`) with no second observation to pair it with. One difference is not evidence. |
| `dir` | 5 | three distinct entity ids, one trailing `1`. Nothing to check it against. |
| `inv`, `eff` | 3 | too few, and `eff` is another entity's visual effect. |

**Do not decode any of these.** Reporting them as unread is a complete delivery;
guessing at them is not.

---

## OWN (new files only)

- `tests/NosAi.Runtime.Tests/WornEquipmentFromWireTests.cs`

## MODIFY

- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`
- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- `src/NosAi.Runtime/LiveIntegration/Capture/WorldChannelReplay.cs`
- `src/NosAi.Runtime/Observability/WorldReplayCommand.cs`

**Nothing else.** In particular no `src/NosAi.Core/` (Claude's lane — including
`Player.Equipment` itself, see *Out of scope*), no `Program.cs`, no
`src/NosAi.ControlPanel/`.

---

## 1. `WornEquipment` — slots and vnums, nothing interpreted

In `GameTrafficObserver.cs`:

```csharp
/// <summary>One equipment slot the wire reported as occupied.</summary>
/// <remarks>
/// The slot is the number the packet carries, not a member of
/// <c>NosAi.Economy.Inventory.EquipmentSlot</c>. Mapping the two is a separate
/// decision with its own evidence (Item.dat, via ItemReferenceDecoder), and
/// doing it here would bury a guess inside an observation.
/// </remarks>
public readonly record struct WornEquipmentSlot(int Slot, int Vnum);

/// <summary>What the character is wearing, as the wire reported it.</summary>
public sealed record WornEquipment(
    long EntityId,
    ImmutableArray<WornEquipmentSlot> Slots);
```

Extend `DecodedObservations` additively, last parameter, as `lev` did:
`WornEquipment? Equipment = null`, and add it to `IsEmpty`.

## 2. Decode `eq` and `equip`

- **`eq`**: read the own entity id (field 1) and the dotted group (field 7).
  Split on `.`; a position holding `-1` is **not** an empty slot in the contract
  — it is a slot that is not worn, so it produces **no** `WornEquipmentSlot` at
  all. The position index is the slot number. Refuse the packet whole if the id
  does not parse, if the group has no separator, or if any position is neither
  `-1` nor a non-negative integer.
- **`equip`**: each group is `slot.vnum.…`; read the first two numbers of each
  group and ignore the rest. A group whose vnum is `0` produces no slot. Refuse
  the packet whole on any unparseable group — a partially-read equipment set is
  exactly the reading that would make the planner act on a weapon that is not
  there.
- Both return `DecodedObservations.Empty with { Equipment = … }`.
- `equip` carries no entity id: use the id the decoder already tracks for the
  player (`cond`), and when it has none, **refuse** rather than attributing the
  equipment to nobody. There is a precedent for this refusal shape — read how
  `DecodeSkillReady` and `CheckSkillReady` handle "not observed yet".

## 3. Report and census

`NetworkObservationReport` carries `Equipment` the way it carries `Vitals`, most
recent wins. `WorldChannelReplay.ReadOpcodes` gains `"eq"` and `"equip"`, taking
`equip_test.noscap` from **3541/3584 to 3553/3584**. `--world-replay` prints:

```
  equipaggiamento (eq/equip): 12 letture
    slot occupati (ultima)  : 0=262 2=221 4=715 5=157 9=224 10=279 11=284 12=902
    slot visti nella sessione: 0 2 4 5 6 8 9 10 11 12
```

The second line is what makes the reading checkable at a glance: it is the union
across the session, and it must show the slots that came and went.

---

## Tests — `WornEquipmentFromWireTests.cs` (new)

Hand-built, `Packet(...)` idiom of `CataloguedOpcodeTests.cs`:

1. The capture's `eq` line yields exactly six slots, and the `-1` positions
   produce none.
2. The capture's first `equip` line yields exactly nine slots, `0=262` first.
3. A `equip` group with vnum `0` produces no slot.
4. `[Theory]` over malformed lines — a non-numeric slot, a group with one part,
   an `eq` with no dotted group — each refused whole (`IsEmpty`,
   `Equipment is null`).
5. Fields after the vnum inside a group are ignored: the capture's line and the
   same line with those fields changed decode identically.
6. An `equip` arriving before any `cond` is refused, and the refusal is
   observable — not silently attributed to entity 0.

Against the real recording, with `RecordedCaptureFactAttribute` (**never**
`if (…) return`):

7. `[RecordedCaptureFact("equip_test.noscap")]` — the replay yields **6** `eq`
   readings and **6** `equip` readings; every `eq` reading has the same six
   slots; the entity id is `3443217` in all of them.
8. **The correspondence test, the one that matters.** In the same replay, for
   each of the vnums `309`, `518` and `284`: it appears in at least one `equip`
   reading and in at least one `ivn` reading, and the `equip` readings that
   contain it are not all of them. Written as a `[Theory]` with one
   `InlineData` per vnum. If this test cannot be made to pass, **stop and
   report** — it means one of the two decoders is reading the capture wrongly,
   and that is a finding worth more than the feature.
9. `[RecordedCaptureFact("nostale_combat.noscap")]` — the combat recording has
   no `eq`/`equip`, so zero equipment readings, and its census is unchanged by
   this task.

---

## Explicitly out of scope

- **`Player.Equipment` stays untouched.** Publishing this into the World Model
  contract is A1/A3 and belongs to Claude; doing it here would put two agents in
  `NosAi.Core`. This task stops at the observation report.
- **No mapping slot number → `EquipmentSlot`.** The enum has 18 values and the
  wire shows 11 positions; reconciling them needs `Item.dat` and its own
  evidence, and a wrong mapping here would be invisible.
- **No `--equip` / `--unequip`.** Nothing in this task actuates.
- **`sc`, `pairy`, `bf`, `dir`, `inv`, `eff`** stay unread, per the table above.

---

## Definition of done

- `dotnet build NosAi.sln -c Release` — **0 errors, 0 warnings**.
- `dotnet test tests/NosAi.Runtime.Tests -c Release` — 0 failures, numbers
  reported; the existing skips stay skips.
- `dotnet test tests/NosAi.Core.Tests -c Release` — unchanged.
- `--world-replay data/equip_test.noscap` run for real, its equipment block and
  its census line (**3553/3584**) pasted into the report.
- The completion report says, in one line each, which of `sc`, `pairy`, `bf`,
  `dir`, `inv` you left unread and why — so the next person does not re-derive it.
- Verification level: **Integrated** at most. `Verified` needs an operator
  watching a real character change a real item, and this task cannot reach it.
