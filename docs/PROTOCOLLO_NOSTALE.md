# NosTale world protocol — observed catalogue

**Source:** two real captures of a live session, 1 Sep 2026, server `79.110.84.175:4002`.
`data/nostale_01.noscap` (idle, 2490 packets) and `data/nostale_combat.noscap`
(combat, 8211 packets). Both decode to **100% printable ASCII** through
`NosTaleWorldDecoder`.

**Status of this document.** Everything here is *observed*, not specified. NosTale
publishes no protocol description, so each field below carries how strongly the
capture supports it:

| Mark | Meaning |
|---|---|
| **confirmed** | Cross-checked against something independent — the client's own HUD, or arithmetic that holds across the capture |
| **probable** | Consistent across every occurrence, and the reading is the only one that fits |
| **unknown** | Position is stable, meaning is not established. **Do not read these.** |

A field marked *unknown* that later turns out to be needed must be derived from a
new capture, not guessed from its neighbours. ADR-0014 is explicit that a decoded
value is `LIVE` only when the decoder verified its framing; a field nobody has
established is not a value, it is an offset with a number in it.

---

## Transport

- TCP, world channel. The port is per-session; `79.110.84.175:4002` in these captures.
- **Server → client** is what carries the world. It is decodable: see
  `NosTaleWorldDecoder`. Packets are terminated by `0xFF`.
- **Client → server** is *not decoded*. It uses a different, session-keyed
  encryption (observed: packets open `0xD5`/`0xD6` and end `0x4E`). Nothing in
  this document comes from it.

### What the wire cannot tell us

**The player's own position never arrives from the server.** Every `mv` in
117 KB of capture is entity type `3`; not one carries the player's id. Position
is client-authoritative — the client sends it, and that direction is encrypted.

This is the boundary where memory reads earn their place as the confirming
source: the network is authoritative for everything the *server* knows, and
silent about what only the client knows.

---

## Identities

| Concept | Observed |
|---|---|
| Entity type `1` | player — **confirmed** (the session's own character id `3443217` appears as type 1 in `su`, `cond`, `sayi`) |
| Entity type `2` | not observed in these captures |
| Entity type `3` | monster / NPC — **confirmed** (all `mv`, `in`, `die`) |
| Entity id | stable per entity for its lifetime — **confirmed** (traced `313816` across `in`, `st`, `su`, `mv`, `die`) |

---

## `stat` — the player's own vitals

```
stat 7288 7305 1420 1420 0 1184
     hp   maxHp mp  maxMp  ?   ?
```

| # | Field | Confidence |
|---|---|---|
| 1 | current HP | **confirmed** — matched the client's HUD reading 7305/7305 while idle, then moved across 33 distinct values during combat (7218…7305) |
| 2 | max HP | **confirmed** — constant 7305, matched the HUD |
| 3 | current MP | **confirmed** — matched the HUD reading 1420 |
| 4 | max MP | **confirmed** — matched the HUD |
| 5 | — | **unknown** — constant `0` throughout both captures |
| 6 | — | **unknown** — constant `1184`. Candidate: SP, given the HUD's third bar. Not established |

**This is the vitals source.** 62 packets during 90 s of combat, tracking damage
as it happened.

---

## `st` — another entity's vitals

```
st 3 313816 8 0 66 100 198 52 310 52 0
   ty id    lv ?  ?  ?   hp mp mxH mxM ?
```

| # | Field | Confidence |
|---|---|---|
| 1 | entity type | **confirmed** |
| 2 | entity id | **confirmed** |
| 3 | level | **probable** — `8` for the monsters fought, plausible and stable |
| 5 | HP percent | **probable, and inconsistent with fields 7/9** — see below |
| 6 | MP percent | **probable** — `100` throughout |
| 7 | current HP | **probable** |
| 8 | current MP | **probable** |
| 9 | max HP | **probable** |
| 10 | max MP | **probable** |

**Do not use field 5.** Checked arithmetically across the capture, `round(hp/maxHp*100)`
matches it in only 28 of 49 packets; where it disagrees it reads about two points
high (`198/310` = 64%, field says 66). The most likely explanation is that the
percentage is computed before the update the same packet reports, but that is not
established. **Use the absolute values (7 and 9).**

---

## `su` — a skill or attack resolving

Two shapes, by who attacks.

**Monster → player:**
```
su 3 313816 1 3443217 0 12 11 200 0 0 1 99 0 1 0 7289 7305
   ty id     ty id     sk ?  ?  ?   ? ? ?  % ?  ? ? hp  maxHp
```

**Player → monster:**
```
su 1 3443217 3 313816 226 250 12 522 0 0 0 0 698 5 0 0 310
   ty id     ty id    skill ?   ?  ?   ? ? ? ?  dmg ? ? ? maxHp
```

| Field | Confidence |
|---|---|
| attacker type, attacker id | **confirmed** |
| target type, target id | **confirmed** |
| skill vnum (field 5) | **probable** — `0` for a monster's basic attack, `226` for a player skill |
| damage | **probable** — `698` against a monster whose max HP is 310, i.e. an overkill; matches the `die` that follows |
| target HP percent | **probable** — `99` while the player sat at 7289/7305 |
| **last two fields** | **confirmed for the player as target** — `7289 7305` is exactly the player's HP/maxHP, and it tracks `stat` |

`su` is the per-hit event stream: who hit whom, with what, for how much, and the
target's resulting HP. It is the highest-value packet for combat reasoning after
`stat`.

---

## `in` — an entity enters view

```
in 3 36 313826 109 63 2 100 100 0 0 0 -1 1 0 -1 - 0 -1 0 …
   ty vnum id  x   y  d hp% mp% …
```

| Field | Confidence |
|---|---|
| type, vnum, id | **confirmed** — vnum groups identical monsters (`36`, `45`, `9`, `96` seen) |
| x, y | **probable** — consistent with the `mv` that follows for the same id |
| direction (field 6) | **probable** |
| HP percent, MP percent | **probable** — `100 100` on spawn |
| remainder | **unknown** — long tail, mostly `0` and `-1`, one `-` (empty string field) |

Spawn is where an entity's **vnum** is learned; `mv` afterwards carries only the id.
Anything that needs to know *what* a monster is has to keep the `in` mapping.

---

## `mv` — an entity moved

```
mv 3 3194 121 110 5
   ty id   x   y   speed
```

All fields **confirmed** by consistency across 7685 packets and continuity with `in`.
**Never carries the player** — see *What the wire cannot tell us*.

---

## `lev` — the player's progression

```
lev 56 9688533 39 43226 18247900 185500 35106 7 0 0 1 0
    lv xp      jl jXp   xpMax    jXpMax rep   ?
```

| Field | Confidence |
|---|---|
| level, XP, job level, job XP | **probable** — XP rises monotonically across the capture while the others hold |
| XP max, job XP max | **probable** — constant, and larger than the running values |
| field 7 (`35106`) | **unknown** — candidate reputation |
| remainder | **unknown** |

---

## `cond` — movement and action state

```
cond 1 3443217 0 0 11
     ty id     ? ? speed
```

| Field | Confidence |
|---|---|
| type, id | **confirmed** |
| fields 3, 4 | **probable** — candidates: cannot-attack, cannot-move. Both `0` throughout, so never observed asserted |
| speed (field 5) | **probable** — `11`, plausible for a level 56 character |

---

## Events

| Opcode | Seen | Shape | Reading |
|---|---:|---|---|
| `die` | 4 | `die 3 313820 3 313820` | An entity died — **confirmed**, each followed the `su` that overkilled it |
| `drop` | 3 | `drop 2006 1092257 110 63 1 0 3443217` | vnum, drop id, x, y, amount, ?, owner id — **probable** |
| `get` | 2 | `get 1 3443217 1092257 0` | Picked up: taker type/id, drop id — **probable**, ids match a preceding `drop` |
| `ivn` | 3 | `ivn 2 34.2006.1.0` | Inventory slot: `slot.vnum.amount.rarity` — **probable**, vnum `2006` matches the `drop` |
| `eff` | 6 | `eff 3 313909 5000` | Visual effect on an entity — **probable** |
| `sr` | 17 | `sr 0`, `sr 2`, `sr 6` | Skill ready / cooldown ended, by skill slot — **probable** |
| `ct` | 108 | `ct 3 313816 1 3443217 -1 -1 0` | Targeting between two entities — **probable** |
| `sayi`, `msgi` | 18 | `sayi 1 3443217 12 975 2 2006 1 0 0` | Localised message ids, not text — **probable** |
| `guri` | 6 | `guri 2 1 3443217 0` | **unknown** |
| `icon` | 2 | `icon 1 3443217 1 2006` | **unknown** |
| `delay` | 6 | `delay 4000 4 #guri^400^3324` | Timed action, ms + a callback string — **probable**; the only packet with non-numeric payload |
| `cancel` | 1 | `cancel 1 1092257 -1` | **unknown** |
| `ms_c` | 2 | `ms_c 0` | **unknown** |

---

## What this gives the runtime today

Directly available, per ADR-0014's `LIVE` bar, through `NosTaleWorldFramer` +
`NosTaleWorldProtocolDecoder` + `NetworkGameplayProvider`:

- **Own vitals** — HP, max HP, MP from `stat`, updating per hit.
  Published `LIVE` when the capture itself is live. Max MP is confirmed on the
  packet and used to reject a malformed `stat`, but `GameplayObservation` does
  not yet carry it. HasTarget and InCombat are **not** read from `stat` (fields 5
  and 6 are unknown); `HasTarget` is established from the screen instead
  (ADR-0018, below), and `InCombat` stays `UNKNOWN`.
- **Target vitals** — absolute HP and max HP of any entity in view, from `st`
  (fields 7 and 9; field 5 is ignored).
- **Combat events** — every hit with attacker, target, skill and damage, from `su`.
- **Entities in view** — spawn with vnum and position from `in`, tracked by `mv`
  only after an `in`/`st` has supplied HP, removed by `die`.
- **Progression** — level and XP from `lev` (catalogued, not yet published).
- **Drops and inventory** — `drop`, `get`, `ivn` (catalogued, not yet published).

Not available from the server, and needing the confirming source:

- **The player's own position.**
- **Anything the client decides locally** before telling the server.
- **Whether the player has a target, and whether the player is in combat.** No
  packet in either capture establishes either. `ct` carries targeting between two
  entities and `su` carries every hit, but neither has an observed "target
  cleared" counterpart, so a flag derived from them would be sticky and wrong in
  a way nothing on the wire would correct.
  [ADR-0016](adr/ADR-0016-planning-and-acting-on-partial-observation.md) makes
  the planner skip the rules that read them instead of blocking every rule that
  does not.

  `HasTarget` now has a source, and it is not this one:
  [ADR-0018](adr/ADR-0018-establishing-the-target-from-the-screen.md) has the
  screen establish it, because the target frame disappears and the screen is
  therefore the only source that can say *no*. The wire's contribution is a
  `su` in which the player is the attacker — attacker type `1`, the
  player-attacks shape above — which **contradicts** a screen that saw no frame
  and never establishes the fact by itself. Until the operator calibrates the
  target ROI against a real client, `HasTarget` stays UNKNOWN with the reason
  `target_roi_not_calibrated`. `InCombat` is still unsourced.

## What the runtime does not read

Replaying the combat capture through the shipping decoder
(`WinDivertProbe.exe --world data/nostale_combat.noscap`) reports 7942 of 8211
packets carrying an opcode it reads, and 7741 sightings across 164 distinct
entities, with 287 packets producing no observation at all. The same replay
reported 629 packets producing an observation while a sighting had to carry
health; letting a sighting state a position without one is what closed the gap.
What is left out is left out on purpose, and it is worth stating so nobody
chases it:

- **`mv` dominates the wire and carries no health.** 7685 of 8211 packets are
  movements. `EntitySighting.HpRatio` is nullable, so a movement now produces a
  sighting that says where the entity is and says nothing about its health;
  filling that in with full health, or with a zero, would have been an invented
  observation, and dropping the packet threw away the position along with it.
  Health still comes only from `in` or `st`, and a capture that starts
  mid-session has 25 `in` and 49 `st` against those 7685 `mv` — so most entities
  are located long before their health is ever known.
  `EntitySighting.ToDetection()` returns null for such a sighting rather than a
  `Detection` at zero HP, because zero HP is a dead mob to the world model.
- **Entity types other than 3 are refused** in `in`, `mv` and `st`. The shapes
  above are type 3's; type 1 is confirmed only in `su`, `cond` and `sayi`, and
  type 2 was never observed. Reading a type-1 `in` at these positions would take
  x and y out of fields that are not x and y.
- **Opcodes marked unknown are not read at all**, which is 269 packets here:
  `ct`, `cond`, `lev`, `sr`, `sayi`, `eff`, `delay`, `guri`, `msgi`, `drop`,
  `ivn`, `get`, `icon`, `ms_c`, `cancel`.
