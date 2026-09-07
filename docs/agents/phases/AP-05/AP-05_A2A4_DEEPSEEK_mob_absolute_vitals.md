# AP-05 / A2+A4 — DeepSeek — stop discarding a monster's absolute HP

## Read this section before anything else

`REGOLA ASSOLUTA`, `REGOLA ASSOLUTA #2` and `REGOLA ASSOLUTA #3` in
`docs/agents/DEEPSEEK_TASKS.md` apply unchanged: complete files only, code
and tests only, and **every claim below that ends up in a comment or test
must be re-verified against the real files cited, not copied on trust**.

This task is read-only with respect to the game: it decodes and displays.
It sends nothing, clicks nothing, changes no keybind.

## Why this task exists

`src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`,
`DecodeOtherVitals` (the `st` packet — another entity's vitals) already
reads a **non-player entity's absolute current and maximum HP** from
fields 7 and 9, and already validates them
(`if (maxHp <= 0 || hp < 0 || hp > maxHp) return DecodedObservations.Empty;`).
One line later, at `NosTaleWorldProtocolDecoder.cs:168`, it collapses them:

```csharp
double hpRatio = (double)hp / maxHp;
```

and the pair is gone, because `EntitySighting`
(`src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`, around
line 69) has no field to carry it. Everything downstream — the World
Model, the target selector, the operator's own `--world-replay` and
`--live-decode` output — therefore sees `0.64` where the wire actually
said `198/310`.

The real capture this repository already replays proves the pair is real,
not inferred. `tests/NosAi.Runtime.Tests/NosTaleWorldObservationTests.cs:251`
(and three other test files) feed:

```
st 3 313816 8 0 66 100 198 52 310 52 0
```

— monster `313816` at 198/310. Two independent corroborations, both
already in this repository:

- 198/310 is 63.87%, while the packet's own percentage field (field 5)
  says `66`. The decoder's existing doc comment states exactly this and is
  why it prefers the absolutes. Re-read that comment before you touch it.
- `docs/PROTOCOLLO_NOSTALE.md` § `su` independently records a monster with
  max HP `310` in the same capture.

The `in` packet (`docs/PROTOCOLLO_NOSTALE.md` § `in`) carries only `hp%`,
an integer 0-100 — for an entity seen only through `in`, the fraction is
genuinely all there is, and that case must keep working exactly as it does
today.

## Already built and real — read before writing code

`NosAi.Core.WorldModel.Resource` (already merged, already tested,
`tests/NosAi.Core.Tests/WorldModel/ResourceFractionTests.cs`) gained
`Fraction` and `Resource.FromObservedFraction(...)`, so the World Model can
now hold either shape honestly: absolutes when the wire gave them, a bare
fraction when it did not. **You do not need to touch `NosAi.Core`**, and
you are not projecting `Mob` records in this task — that is the follow-up
(see "Explicitly out of scope").

## OWN (new files only)

- `tests/NosAi.Runtime.Tests/EntitySightingAbsoluteVitalsTests.cs`

## MODIFY

- `src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs`
- `src/NosAi.Runtime/Perception/Network/NosTaleWorldProtocolDecoder.cs`
- `src/NosAi.Runtime/Observability/WorldReplayCommand.cs`
- `src/NosAi.Runtime/LiveIntegration/Capture/LiveWireMonitor.cs`

Nothing else. If you believe another file must change, stop and report it
instead of changing it.

## 1. `AbsoluteVitals` — one pair, never half a pair

In `GameTrafficObserver.cs`, beside `EntitySighting`:

```csharp
/// <summary>An entity's health as the wire stated it in absolute points, both bounds or neither.</summary>
public readonly record struct AbsoluteVitals(int Current, int Maximum);
```

One record, not two nullable ints on `EntitySighting`: the two numbers are
only ever observed together, in the same packet, and separate nullables
would let a later caller construct half of one. This is the same "all
together or none" rule `InventoryPanelRoiCalibration` already applies to
its ROI set.

Add exactly one optional parameter to `EntitySighting`, last, so every
existing construction site keeps compiling unchanged:

```csharp
AbsoluteVitals? Vitals = null
```

Document on the parameter, in your own words after re-reading the code:

- Null is the ordinary case, not an error: `in` and `mv` never carry the
  pair, only `st` does.
- It never contradicts `HpRatio`. Where `Vitals` is non-null, `HpRatio` was
  computed from it, in the same packet, at the same instant — so a consumer
  that reads only `HpRatio` stays correct and unchanged.
- It shares `HpObservedAtUtc`; there is no second instant, because there is
  no second observation.

Do **not** change `HpRatio`, its type, its meaning or its provenance.
Do **not** add a `bool HasVitals` beside the nullable — the existing
`HpRatio` doc comment explains, for exactly this record, why a sentinel or
a parallel flag is refused here.

## 2. Keep the pair through the decoder

In `NosTaleWorldProtocolDecoder.cs`:

- `TrackedEntity` (the private record struct near the end of the file)
  gains the same optional `AbsoluteVitals? Vitals` so a later `mv` that
  carries a *remembered* health also carries the remembered pair. It must
  age exactly like `HpRatio` does today: `mv` reuses it, stamped with the
  older `HpAtUtc`, and the sighting keeps whatever stale/live source label
  the existing code already computes. Do not invent a new label.
- The private `Sighting(...)` helper gains an `AbsoluteVitals? vitals`
  argument and passes it to `EntitySighting`.
- `DecodeOtherVitals` passes `new AbsoluteVitals(hp, maxHp)` — the two
  values it has already validated. Nothing else in that method changes.
- `DecodeEnter` (`in`) passes `null`: `hp%` is all that packet states, and
  reconstructing a maximum from a percentage would be a fabricated number.
  Say so in one comment line.
- Every other `Sighting(...)` call passes the remembered `previous.Vitals`
  where it already passes `previous.HpRatio`, and `null` where it already
  passes `null`.

## 3. Show it to the operator

Both sites already print a sighting's health as a bare ratio. Extend both
to print the real pair when it exists, and to keep printing exactly what
they print today when it does not.

- `src/NosAi.Runtime/Observability/WorldReplayCommand.cs`, `ToRow` (around
  line 379): when `Vitals` is present, the `hp` cell reads
  `0.64 (198/310)`; otherwise it is byte-for-byte what it is today
  (`0.64`, or `UNKNOWN (hp_not_on_sighting)`). `CultureInfo.InvariantCulture`
  throughout, as that method already does.
- `src/NosAi.Runtime/LiveIntegration/Capture/LiveWireMonitor.cs` (around
  line 192): same rule, `hp=0.64 (198/310)` when present, `hp=0.64` /
  `hp=assente` otherwise.

Do not change the column set of `WorldReplayEntityRow`, and do not
reformat any row that has no absolute pair — an existing test asserting
today's text must keep passing without being edited. If one does need
editing, you have changed behaviour you were not asked to change: stop and
report it.

## Tests — `EntitySightingAbsoluteVitalsTests.cs` (new)

Use the real capture lines, not invented ones. Feed the decoder directly,
the way `NosTaleWorldObservationTests.cs` already does.

1. `st 3 313816 8 0 66 100 198 52 310 52 0`, after an `in` that gave the
   entity a position → the sighting's `Vitals` is `(198, 310)` **and**
   `HpRatio` is still `198d/310d`. Assert both: the pair is added, the
   ratio is unchanged.
2. The same packet → `Vitals!.Value.Maximum` is `310`, not `100`, and not
   the `66` from field 5. This is the assertion that proves the decoder
   reads the absolutes rather than the percentage.
3. `in 3 36 313826 109 63 2 100 100 0 0 0 -1 1 0 -1 - 0 -1 0` alone → the
   sighting's `Vitals` is `null` while `HpRatio` is `1.00`. An `in`-only
   entity has a real fraction and no absolutes, and that is not a defect.
4. `in`, then `st`, then `mv` for the same entity → the `mv` sighting
   carries the remembered `Vitals` with the `st`'s own `HpObservedAtUtc`,
   proving the pair ages exactly like the ratio.
5. A malformed `st` the existing guard already refuses (for instance
   `maxHp` of `0`) → still `DecodedObservations.Empty`, so no sighting and
   therefore no `Vitals` at all. Do not add a new guard; assert the
   existing one still holds.

For the two display changes, extend the existing test files that already
assert those outputs rather than duplicating their setup, and only by
adding cases — never by weakening an existing assertion.

## Explicitly out of scope

- Projecting `Mob` records into `WorldModelSnapshot.Mobs`
  (`GameplayObservationProjector`). **Already done, after this spec was
  written** (Q-098): that projection exists, is wired into
  `WorldModelFusionLoop` and `Program.cs`, and carries each entity's health
  as `Resource.FromObservedFraction` — a fraction over two Unknown bounds,
  because the absolutes cannot reach it yet. Your task is what makes them
  reach it. Do not touch `GameplayObservationProjector` here: once
  `EntitySighting` carries the pair, a separate follow-up widens
  `SelectableEntity` and lets the projection prefer the absolutes over the
  fraction.
- `SelectableEntity` (`src/NosAi.Runtime/Autonomy/TargetSelector.cs`) and
  anything that ranks targets. Unchanged here.
- `NosAi.Core`. Unchanged here.

## Definition of done

`dotnet build` with 0 errors and 0 warnings; the whole
`NosAi.Runtime.Tests` suite green except the three failures that already
fail on `main` on a Windows host
(`ScreenVitalsCaptureTests.Capture_WithAProcessId_OnANonWindowsHost_FailsClosed_BeforeTouchingClientWindowLocator`,
`ScreenVitalsCaptureTests.TryEnsurePipeline_OnANonWindowsHost_FailsClosed_WithDxgisOwnReason`,
`MapReconstructionSourceTests.Resolve_NoStoreAvailable_StillReconstructsInMemoryForTheSession`);
`NosAi.Core.Tests` untouched and green. Report the exact numbers, not a
summary of them. Verification level is `Present` — `Verified` needs an
operator reading a real monster's `198/310` off a live client, which is not
yours to claim.
