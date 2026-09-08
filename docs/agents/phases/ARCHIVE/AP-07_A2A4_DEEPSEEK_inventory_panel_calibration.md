# AP-07 / A2+A4 — DeepSeek — `--calibrate-inventory-panel`

## The decision this task executes

`AP-07_A1_STATUS.md` §"AP-07/A2+A4" found AP-07's Equip/Unequip execution
genuinely blocked on two fronts, re-verified 2026-09-06 with one real
crack found: no screen-space layout exists for the equipment panel's
eighteen slots (equip is a drag/click on a fixed UI panel, not a hotkey),
and the verification channel's `InventoryKind` field had no documented
meaning at all. The second is now partially resolved (Claude, this
session): `GameTrafficObserver.cs`'s `InventorySlotReading.InventoryKind`
remarks now cite a real external source (OpenNos, GPL,
`third_party/sources/opennos`) for candidate meanings, with one value
cross-checked against this project's own capture (`Etc=2`) and two
(`Equipment=0`, `Wear=8`) documented but **not yet** cross-checked. The
first is still unresolved — no code in this repository can say where the
panel's slots are on screen without an operator confirming a real crop,
exactly the same problem `TargetRoiCalibration`/`DialogRoiCalibration`
already solved for one region each.

**What this task buys, and what it deliberately does not:** it gives the
operator a way to record where each `EquipmentSlot` icon sits on their
own client, the same way `--hud-probe --calibrate-target` already does
for the target-HP-bar region. It does **not** click anything, does
**not** execute an Equip/Unequip, and does **not** read what currently
occupies a slot. Building `--equip`/`--unequip` themselves is explicitly
future work, gated on this calibration existing *and* a dedicated real
capture confirming `InventoryKind` flips to `8` (`Wear`) on a real equip
— neither exists yet, so do not attempt execution here.

## Already built and real — read before writing code

- `src/NosAi.Runtime/Perception/InventoryPanelRoiCalibration.cs` (new,
  this session, Claude/A1) — read it in full. `Confirmed(IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois, int clientWidth, int clientHeight, DateTime calibratedAtUtc)`
  requires **exactly one entry per `EquipmentSlot` value, all eighteen
  together** — there is no partial-calibration state, matching the panel
  being either fully open or not. `Load`/`Save`/`Resolve` mirror
  `DialogRoiCalibration`'s exact API shape (same `IsCalibrated`/
  `NotCalibratedReason`/gitignored `data/` path pattern) — do not
  redesign this contract, use it as given.
- `src/NosAi.Core/WorldModel/InventoryContracts.cs`: `EquipmentSlot`
  enum — exactly the eighteen values `Confirmed` requires
  (`Weapon, Armor, Hat, Gloves, Boots, SecondaryWeapon, Necklace, Ring,
  Bracelet, Mask, Fairy, Amulet, SpecialistCard, CostumeSuit, CostumeHat,
  WeaponSkin, CostumeWings, MiniPet` — cross-checked against a real
  third-party NosTale item database, see the enum's own doc comment for
  the source and evidence).
- `src/NosAi.Runtime/Perception/HudProbe.cs` (read-only reference) — the
  exact calibration UX to mirror: capture a frame, write a preview bitmap
  the operator looks at, accept fractions on the command line as the
  operator's confirmation, call `<Type>.Confirmed(...)`, save, print the
  result. `HudCropWriter.TrySave` (same file's sibling, find it by its
  usage here) is the crop-writing utility — reuse it rather than writing
  a second bitmap writer; if it only writes the specific named crops
  `HudProbe` uses today, add a **new** narrowly-scoped save call for a
  whole-client-area preview alongside the existing ones, do not change
  what `HudProbe` itself writes or how it calls the writer.
- `src/NosAi.Runtime/Perception/TargetRoiCalibration.cs` /
  `DialogRoiCalibration.cs` (read-only reference) — same recipe,
  single-region version; `InventoryPanelRoiCalibration` already is this
  recipe extended to eighteen, do not re-derive it from scratch.
- `src/NosAi.Runtime/LiveIntegration` client-attach/frame-capture path
  already used by `--hud-probe` (read `HudProbe.cs`'s own composition,
  cite the exact lines you mirror) — this task needs the same "attach to
  the client, capture one frame, resolve the client area" sequence,
  nothing new.

Do not touch `Gate3Runtime.cs`, `ActionType`, `InputActionEffector.cs`,
`Gate3/PostConditions.cs`, or `LoadoutContracts.cs`/`LoadoutPlanner.cs`.
None of AP-04/05/06/08's real commands (`--scout`/`--engage`/`--collect`/
`--autoplay`) go through the Gate3/`ActionType` machinery — it is legacy
and not the extension point for new capabilities, confirmed by grep
(zero references to it in any of those four command files). This task
does not touch it either.

## OWN (new files only)

- `src/NosAi.Runtime/Perception/InventoryPanelCalibrationProbe.cs` (A4) —
  or fold into `HudProbe.cs` if, once you have read it in full, its own
  existing structure makes that a cleaner fit than a new file; if you add
  a new file, name it for what it does, not for this task number.
- `tests/NosAi.Runtime.Tests/InventoryPanelCalibrationProbeTests.cs` (or
  matching name for wherever the parsing logic in §1 below lives).

## MODIFY

- `src/NosAi.Runtime/Program.cs`: register the new flag (see §3).
  Additive only, same pattern as `--hud-probe`'s own wiring.
- `src/NosAi.Runtime/Perception/HudCropWriter.cs` (or wherever the crop
  writer lives): only if it needs a new, additive method to save one more
  named whole-client-area bitmap; do not change any existing method's
  signature or behavior.

Do not modify `InventoryPanelRoiCalibration.cs`, `InventoryContracts.cs`,
or `GameTrafficObserver.cs` — everything this task needs from them
already exists.

## 1. Parsing the operator's confirmation — testable, no I/O

The operator passes each slot as one token, `<EquipmentSlot>:<x>,<y>,<w>,<h>`
(fractions of the client area, same convention as `--calibrate-target`'s
four separate numbers, packed one-slot-per-token because there are
eighteen of them). All eighteen must be present in one invocation — there
is no partial state to accumulate across runs.

```csharp
public static bool TryParseSlots(
    IReadOnlyList<string> tokens,
    out IReadOnlyDictionary<EquipmentSlot, InventorySlotRoi> rois,
    out string? failureReason)
```

- Splits each token on `:` then the right-hand side on `,`; four
  `double`s required per token (`CultureInfo.InvariantCulture`).
- An unparseable token, a token naming a slot outside `EquipmentSlot`, a
  duplicate slot, or fewer/more than eighteen tokens: return `false` with a
  specific `failureReason` (name which token and why) — never silently
  drop or default a slot.
- Does not call `Confirmed` itself (that can throw for a
  region-outside-client-area/zero-extent reason `TryParseSlots` cannot
  know about before the client area is resolved) — the caller does, per
  §2.
- Cover in tests: all eighteen valid tokens in `EquipmentSlot`'s declared
  order and in a shuffled order (order must not matter — `Confirmed`
  takes a dictionary); a missing slot; a duplicate slot; a malformed
  fraction; an unknown slot name; nineteen tokens.

## 2. The console command — live composition

Mirror `HudProbe`'s `--hud-probe --calibrate-target` handling shape
exactly (read it before writing this):

- `--calibrate-inventory-panel [<slot:x,y,w,h> ... x18]`: with **zero**
  extra tokens, only report the current calibration state (loaded via
  `InventoryPanelRoiCalibration.Load`) and, if uncalibrated, print the
  same kind of guidance `HudProbe` prints for `--calibrate-target`
  (what file to look at, the exact command to re-run with all eighteen
  tokens). With tokens present, they must number exactly eighteen or the
  command refuses with `[REFUSED]` and a non-zero exit code before
  attempting to attach to any client.
- Attach to the client, capture one frame, resolve the client area —
  same sequence as `HudProbe`, same failure handling (client not found,
  no frame acquired within the attempt budget: print why, non-zero exit,
  no fabricated area).
- Write a whole-client-area preview bitmap (see "OWN"/"MODIFY" above) the
  operator can open to read off each slot's fractions, named
  `inventory_panel_latest.bmp`, same directory `HudCropWriter` already
  uses for its other named crops.
- With eighteen valid tokens: parse via §1, then call
  `InventoryPanelRoiCalibration.Confirmed(rois, area.Width, area.Height, DateTime.UtcNow)`
  inside a `try`/`catch (ArgumentException)` (covers both the "wrong slot
  set" and "region outside client area" refusal shapes `Confirmed`
  throws), `.Save(path)` on success, print the written path and a
  one-line reminder that each slot is only as right as its crop (same
  spirit as `HudProbe`'s own target-ROI reminder). On the caught
  exception, print `[REFUSED] {ex.Message}` and a non-zero exit code —
  do not write a partial file.
- `path = Path.Combine(repoRoot, InventoryPanelRoiCalibration.RelativePath)`,
  `repoRoot` resolved the same way `HudProbe.cs` resolves it (cite the
  exact call).

## 3. Wiring `Program.cs`

Additive, alongside the existing `--hud-probe` block. Register the flag
in `KnownProbeFlags` (the `HashSet<string>` near the bottom of
`Program.cs`) the same way every other probe flag is, so a typo of it
is caught as `Unknown suite or probe flag` rather than falling through to
the Gate 1 host bootstrap.

## Tests / build

```
export PATH="$PATH:/root/.dotnet"
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release --filter "FullyQualifiedName~InventoryPanelCalibration"
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj -c Release
dotnet test tests/NosAi.Core.Tests/NosAi.Core.Tests.csproj -c Release
```

All green, 0 warnings/0 errors, no regression in either full test suite
(record exact pass counts).

## Report back

Files created/modified; whether you folded the probe into `HudProbe.cs`
or made a new file, and why; the exact frame-capture/client-attach lines
you mirrored (file/lines); build/test evidence with exact pass counts;
verification level (`Present` — this cannot become `Integrated`/`Verified`
without an operator actually running it against a real client and
confirming the eighteen crops, which is not something this task can do
itself, same as `TargetRoiCalibration`/`DialogRoiCalibration`'s own
first delivery). Name explicitly, as a known follow-up and not something
to attempt here: `--equip`/`--unequip` themselves remain blocked until
(a) an operator has run this command against a real client and (b) a
dedicated capture confirms `InventoryKind` flips to `8` on a real equip.
