# ADR-0018 — The screen establishes the target, the wire checks it

## Status

Accepted, 1 Sep 2026. Builds on
[ADR-0017](ADR-0017-training-the-screen-reader-from-the-wire.md), which made the
screen an independent reader and closed by naming this as the next separate piece
of work. Changes no classification rule: a screen reading stays `DERIVED`.

## Context

`HasTarget` is the biggest single gap in the observation.
[ADR-0016](ADR-0016-planning-and-acting-on-partial-observation.md) makes the
planner skip every rule that reads an unknown fact, and every attack rule reads
this one. While it is `UNKNOWN`, combat does not exist: the runtime can react to
its own health and nothing else.

Neither source can answer it alone.

### The wire has no "no"

`ct` carries targeting between two entities — 108 occurrences — and `su` carries
every hit. Neither has an **observed counterpart that clears a target**. There is
no packet in either capture that says "target dropped", and
[docs/PROTOCOLLO_NOSTALE.md](../PROTOCOLLO_NOSTALE.md) records this as the reason
the field stayed `UNKNOWN`.

A flag derived from `ct` would therefore be sticky: it would go true on the first
targeting message and stay true for the rest of the session, with nothing on the
wire ever correcting it. That is worse than unknown, because it is confident.

### The screen has the "no" and has never been aimed

The target frame disappears when the target is dropped, so the screen can say
*no* — which is exactly what the wire cannot do.

But `RoiSegmenter.Segment` places `TargetHpBar` at fractions `0.40, 0.06, 0.20,
0.02` that **have never been calibrated on a real client**. They were written as a
plausible guess. Only `PlayerHpBar` has been confirmed against a live client, by
T-03, with the crop as evidence.

A bar reader pointed at the wrong region is not a reader that fails. It is a real
measurement of the wrong pixels, and `TargetFrameReader` cannot tell that from a
correct one. Pointed at empty HUD background it would report `Absent` — a
confident, wrong *no target* — every frame.

## Decision

**The screen establishes `HasTarget`. The wire confirms it and never creates it.**

| Screen reading | `HasTarget` | Source |
|---|---|---|
| `TargetFrameState.Present` | `true` | `DERIVED` |
| `TargetFrameState.Absent` | `false` | `DERIVED` |
| `TargetFrameState.Unreadable` | **`UNKNOWN`**, carrying the reader's own reason | — |

`Unreadable` must never become `false`. A false there sends ADR-0016's planner
toward an exploration waypoint in the middle of a fight, which is the precise case
that ADR exists to prevent. The three outcomes of the reader are three outcomes
here; they are not collapsed into two.

### The wire enters only as a contradiction

A `su` in which the player is the attacker, **more recent than the screen
reading**, while the screen says `Absent`, means the two sources do not agree. The
result is `UNKNOWN` with the reason `target_sources_disagree` — not a choice
between them.

The asymmetry is deliberate. A `su` while the screen says `Present` is agreement
and changes nothing. A `su` older than the screen reading is history: the target
was dropped after that hit, which is ordinary. Only a hit that happened *after*
the screen looked, while the screen saw nothing, is a disagreement.

This is ADR-0017 run backwards. There the wire taught the screen, because the wire
was the stronger source. Here the screen establishes, because it is the only one
that can say *no*, and the wire is worth having because it is independent.

### The calibration is a precondition, enforced in code

Until the `TargetHpBar` ROI has been calibrated against a real client with a
target selected, `HasTarget` stays `UNKNOWN` with the reason
`target_roi_not_calibrated`. This is not advice in a document: `TargetStateComposer`
refuses before it reads anything, and the uncalibrated fractions in `RoiSegmenter`
reach no published fact.

An uncalibrated read produces a self-assured `false`, which is the worst of the
three possible outcomes — worse than `Unreadable`, which at least reports itself.
Refusing costs nothing while the fact is unusable anyway.

The calibration lives in `data/perception/target-roi.calibration`, beside the
glyph atlas and the T-03 crops, and **is not committed**. ADR-0017 argued this for
the atlas and the argument is identical: the fractions are of one client at one
resolution on one display. A calibration built anywhere else is a calibration of
somebody else's screen, and it would fail by reading the wrong pixels confidently.
A fresh clone reads `target_roi_not_calibrated`, which is a different state from
broken and reports as one.

The operator produces it with the path T-03 already used — `--hud-probe` and
`HudCropWriter` — with a target selected, so the crop is the evidence that the
region is the target frame.

### Identifying the player as the attacker

`su` carries attacker type and id, both confirmed. The protocol document
distinguishes its two shapes by attacker type: type `1` is the player-attacks
shape, type `3` the monster-attacks one. The composer uses the type.

**The named limitation:** another player attacking a monster nearby is also type
`1`, and this cannot be separated from the controlled character without the
character's own entity id, which nothing on the read side of the wire establishes
today. The error it causes is a *false disagreement*, whose result is `UNKNOWN`.
That direction is safe: it costs a fact the planner then skips, and it never
produces a confident wrong answer. It is written here so that the day the own
entity id becomes available, this is a known place to tighten rather than a
surprise.

### A mapped wire flag is not overridden

`ProtocolMap` can name a `HasTarget` field. If a protocol map has one, that is a
direct wire reading and it stands; the screen composes only where the observation
is `UNKNOWN`. `NosTaleWorldProtocolDecoder` has no such field and never will —
fields 5 and 6 of `stat` are unknown — so on the real client the screen always
decides. The rule exists so a future map that genuinely reads the flag is not
silently replaced by a derived one.

## Consequences

- **`HasTarget` has a path to `DERIVED` for the first time.** It stays `UNKNOWN`
  until the operator calibrates the ROI, and the reason says which of the two
  states it is in.
- **The screen becomes a source of a fact the wire cannot carry**, which is what
  ADR-0012 asked of it and what ADR-0017 made possible.
- **`NetworkObservationReport` gains `PlayerAttackedAtUtc`**, additively. It is
  the wire's whole contribution here, and it is a timestamp rather than a flag
  because the decision is about which source is more recent.
- **The composer is pure and total.** It takes the calibration, the screen
  observation and the wire's timestamp, and returns a classified value for every
  combination, so each refusal is testable without a client.
- **Nothing is verified against a real client yet.** The reader, the composer and
  the refusals are unit-tested; `Present` and `Absent` on real pixels are not
  observed, and cannot be until the calibration exists. This ADR is `Integrated`,
  not `Verified`, and F1-8's calibration step is what closes it.
