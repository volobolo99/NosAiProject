# ADR-0021 — The client's memory establishes the target, and the wire proves the offset

## Status

Proposed, 2 Sep 2026.

**Supersedes** [ADR-0018](ADR-0018-establishing-the-target-from-the-screen.md) in one
respect only: which source establishes `HasTarget`. Everything else in that record
stands — the three reader states, the refusal to collapse them into two, and the wire's
role as a contradiction rather than a creator.

**Builds on** [ADR-0014](ADR-0014-operator-chooses-the-data-path.md) (reading the
client's memory is a permitted observation source, and is already in use) and
[ADR-0012](ADR-0012-gameplay-observation-source.md).

## Context

### ADR-0018's reasoning was right, and it is confirmed

That record chose the screen because **the wire has no "no"**: `ct` carries targeting
between two entities and nothing carries "target dropped", so a flag derived from the
wire alone would go true once and stay true.

Replaying `data/nostale_combat.noscap` through the decoder that now reads `ct` confirms
it exactly. Ninety seconds of combat produce **16 selections, every one naming an
entity, and not one clearing**. The wire says *which*. It never says *none*.

### But the screen's price has come due

`HasTarget` from the screen requires the target frame's rectangle, and that rectangle is
**a human measurement, taken again for every resolution**. Worse, its failure mode is
the one this project refuses everywhere else:

> A bar reader pointed at the wrong pixels does not fail. It performs a real measurement
> of the wrong region and reports `Absent` — a confident *no target*, every frame.

On 2 September the operator's first calibration recorded a 230×230 square that turned
out to be a picture of **another application's window**, because desktop duplication
copies whatever is drawn at the client's rectangle and the client was behind it. The
mechanism worked perfectly and produced a confident wrong answer, which is precisely the
shape of failure ADR-0018 warned about — arriving from a direction it did not consider.

The measurement was recoverable. The requirement is not: every resolution change, every
window mode, every client skin costs another one.

### The client already knows

The client holds the selected entity. It has to: it draws the frame from it. And unlike
the wire, **it holds a value for "nothing selected" too** — an object that is drawn only
sometimes is an object whose absence is representable.

This project has met this exact problem once already and solved it without any human
measurement. Map walkability is not inferred from pixels: it is a file of the client.
The map id is not a remembered address: it is discovered by an oracle, narrowed across
maps and a restart, and anchored to the module base.

## Options considered

### Keep the screen, and calibrate it

Honest and already half-built. It costs one human measurement per resolution, and the
error it admits is a silent one. It also makes combat wait on an operator, which is what
has happened: the reactive rule, the goal stack and the post-condition catalogue are all
written, tested, and inert.

**Not adopted.**

### Recognise the HUD by image matching

Match a template of the target frame's border, at several scales, to find it whatever the
resolution.

It replaces a measurement with a dependency and a new fragility: an imaging library on an
observation path, a template that is specific to a client skin and a language, a score
threshold that is itself a calibration nobody can check, and an answer that is still a
*picture* rather than a fact. When it is wrong it is wrong confidently, exactly as the
fixed rectangle is.

**Not adopted.** It buys resolution independence at the price of a second thing to
believe on faith.

### Read the selected entity from the client's memory, and prove the offset with the wire

**Adopted.** No pixels, so resolution independence is not a feature to achieve but a
consequence of not looking at the screen. And the proof that the offset is the right one
is already flowing: the wire names the selected entity 16 times in 90 seconds.

## Decision

### 1. `HasTarget` and the target's identity come from memory

The client's own selected-entity field is the source. The screen stops being the
establishing source for this fact.

### 2. The offset is discovered, never remembered

**Amended 2 September 2026, after the first version failed on the live client.** What
this section originally described — a word is a candidate while it equals an id the wire
has just named, narrowed by each subsequent `ct` — was never built, because `ct` is
catalogued and not yet decoded (`C1-9`). What *was* built constrained a candidate by the
client's own **scene list**, and on the live build that constraint could not be evaluated
at all:

```
process=27192 module=0x400000 manager=0xEF65C60
entita' nella scena: 0
[REFUSED] scene_unreadable:scene_manager_not_confirmed:1_candidates:
          0xFFFFFFFF:player_list_pointer_unreadable_at+0xC
```

`NosTaleClientLayout.SceneManagerSignature` is not a code signature. It is a data pattern
— twenty-five bytes of mostly `FF`, `00` and wildcards — and a pattern like that matches
padding as readily as it matches a structure. It found one candidate, and the pointer
behind it read `0xFFFFFFFF`: the wildcard bytes were all `FF`. **On this build that
signature is unconfirmed**, and it is left in place, unused by this oracle, because the
Control Panel's *Attorno* view reads the same lists and knowing *why* it is empty is
worth more than deleting it.

#### The constraint is now about behaviour, not content

> A word is a candidate only if it **changes exactly when the selection changes**, and
> **returns to the same "nobody" value every time the target is cleared.**

This is stronger than the rule it replaces, and it asks nothing of anybody. The old rule
said *this word holds an id that exists*; millions of words hold plausible integers, and
every entry of the client's own entity list holds a real id — which is why a cleared pass
was needed merely to separate the selection from the list. The new rule describes very
nearly the only field in the process that behaves this way: a timer always changes, a
counter only grows, a coordinate drifts while the character walks, a remembered id never
moves. Only the selection comes back to *one particular value* on every deselection and
takes a new, different one on every target.

The second clearing is where the proof lives. The first only **records** what each word
becomes — any word that changed passes it. The second requires that same value **back**,
and that is what a counter cannot do.

#### Two rounds of narrowing, one execution

Without a content filter the first round has nothing to narrow with, so it keeps a
snapshot of the client's private memory and compares against it. A snapshot exists only
while the process holding it does, so the rounds happen **inside one execution**, with the
operator alternating at the keyboard while it waits — select, clear, select a different
one, clear again. Only the survivors reach the file. The previous design asked for five
separate launches and, lacking a snapshot, capped its candidate list at twenty thousand
words in address order, which is not a sample of anything.

**The restart still needs a second execution, and necessarily so.** An offset that
survives the client being closed and reopened is exactly what a bare address is not, and
there is no way to observe that without a second process. The second run resumes from the
survivors rather than re-scanning.

#### The one bound, and why it decides nothing

The first round would otherwise keep every word of private memory, so values seen *while
a target is selected* must be plausible entity ids. The bound is anchored on a
measurement and not on a guess: **the character's own entity id**, read from the player
object on that same run. A value more than 256× that id is not another id from the same
allocation scheme — it is a pointer, a tick count or a bit pattern.

It is deliberately generous, and its only job is to keep the survivor list workable. If
it were wrong, the hunt would end with **zero** survivors and say so — a loud failure, not
a wrong answer. The bound is applied only to selected values: the "nobody" sentinel is
whatever the client chose, possibly `0` or `-1`, and filtering it would discard the
candidate for holding exactly the value the proof is about to require it to repeat.

#### What would remove the operator from the loop

Decoding `ct` (`C1-9`) would supply the selection changes from the wire, and `die` would
supply clearings for free — a target that dies is cleared by the client itself. That is
the design this section originally described, and it remains the right one; it is future
work rather than the record of what exists, and the distinction between those two is why
this amendment exists at all.

The survivor is **anchored to the module base**, so it survives ASLR and a client restart
— the distinction `MapIdModuleOffset` was proved against on 2 September, and the reason an
address that worked once is not an offset.

### 3. The wire cross-checks every read; it does not become the source

On each read the held id is compared against the last `ct`. Agreement is silence.
Disagreement is `target_sources_disagree` and the result is `UNKNOWN`, which is
ADR-0018's rule kept verbatim, with the two sources exchanged.

### 4. The three states survive, and the new one is UNKNOWN

| Memory | `HasTarget` |
|---|---|
| holds an entity id | `true`, `DERIVED` |
| holds the established "none" value | `false`, `DERIVED` |
| **offset not yet established** | **`UNKNOWN`**, with `target_offset_not_established` |

An unestablished offset must never read as `false`. That is ADR-0018's `Unreadable`
rule, and it exists for the same reason: a false sends the planner to an exploration
waypoint in the middle of a fight.

### 5. The screen reader is kept, as the second source

It is not deleted and its calibration is not forbidden — it becomes the **independent
check** that ADR-0017 and ADR-0018 built the project's habits around. Where it is
calibrated it must agree; where it is not, memory answers alone. What changes is that
combat no longer waits for it.

## Consequences

- **`T-09` stops blocking `C4`.** The target ROI calibration becomes optional
  hardening instead of a precondition, and the work already written — the reactive
  rule, the goal stack, the post-condition catalogue — can run.
- **Resolution independence is free, not engineered.** Memory holds an entity id, and
  an entity id has no pixels. Changing resolution, window mode or skin changes nothing.
- **What still needs the screen is one thing only:** turning a map cell into a point to
  click. And that is already automatic — `ScreenProjectionAutoCalibrator` clicks a ring
  of pixels and reads back from memory which cell the client resolved, so nobody aims and
  nobody types a coordinate. Its five failures were environmental, not a design fault.
- **A wrong offset would be a confident wrong id**, which is the risk this decision
  inherits rather than removes. It is mitigated the way the map id was: the candidate must
  survive several selections and a restart before it is written, and it is checked against
  `ct` on every read afterwards. An offset that stops agreeing is dropped, not trusted.
- **`NosSmooth.Local` binds the player manager and is worth reading** for where to look
  first. It is a starting hypothesis and not an authority, exactly as its player-manager
  offsets already are in `NosTaleClientLayout`.
- **`ADR-0018` is not withdrawn.** Its analysis of the wire remains the reason the wire
  cannot be the source, and this record depends on it.
