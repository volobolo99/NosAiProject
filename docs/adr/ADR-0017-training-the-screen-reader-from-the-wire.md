# ADR-0017 — The wire teaches the screen to read

## Status

Accepted, 1 Sep 2026. Implements the confirming source
[ADR-0012](ADR-0012-gameplay-observation-source.md) requires and
[docs/PROTOCOLLO_NOSTALE.md](../PROTOCOLLO_NOSTALE.md) names as the missing half.
Does not change any classification rule: a screen reading stays `DERIVED`.

## Context

The world channel now publishes the player's HP, max HP and MP as `LIVE`, checked
against the client's own HUD. It is silent about two things it can never carry:

- **The player's own position.** Every `mv` in 117 KB of capture is entity type 3.
  Position is client-authoritative, and that direction of the wire is encrypted.
- **Whether the player has a target.** No packet in either capture establishes it,
  and [ADR-0016](ADR-0016-planning-and-acting-on-partial-observation.md) makes
  Gate 3 skip every rule that reads it. `HasTarget` is what separates reacting to
  one's own health from fighting.

Both need a source that watches the client rather than the server. ADR-0012 chose
screen perception for that. And screen perception has never produced a number.

### Why it never produced one

The reader was complete except for one link. `RoiSegmenter` finds the HUD,
`HudGlyphExtractor` cuts the numerals into normalised bitmaps, `GlyphHashOcrCache`
recognises a glyph it has been taught, and `ScreenDerivedVitalGate` range-checks
what comes out. **Nothing ever taught it a glyph.** Every reading in the project's
history ended `ocr_glyphs_not_trained`, so screen HP was a bar ratio and never an
integer — and a ratio cannot be turned into HP and max HP without inventing one of
the two, which is the distinction ADR-0012 turns on.

Two ways to supply the atlas were available.

### Ship a font atlas

Render the client's font offline and hash the glyphs. It is the obvious answer and
it is wrong here: the bitmaps depend on the client's renderer, the UI scale, and
the display, so an atlas built anywhere but on the operator's machine is an atlas
of a font that resembles theirs. **Rejected** — it would fail silently, producing
`'?'` for glyphs that look right to a person.

### Have the operator type what they see

Works, and it makes the screen reader's accuracy depend on a person transcribing
their own HUD correctly at the moment they press a button. A typo is written to
disk and believed thereafter. **Rejected** as a worse label than one already
available.

## Decision

**The network reading is the label.** `stat 7305 7305 1420 1420` says what the
numerals over the HP bar spell, and it was checked against this same HUD. Pairing
the glyph bitmaps from the crop with the characters of `7305/7305` teaches the
atlas, once, on the operator's own machine. After that the screen reads by itself.

`HudGlyphAtlas` persists the mapping to `data/perception/glyphs.atlas`, keyed by
the FNV-1a hash of the normalised bitmap and versioned by the normalisation it was
built under, so a change to `HudGlyphExtractor` invalidates the file rather than
half-matching it. `HudGlyphTraining` performs the pass.

### The label must be LIVE

Each of the other classifications is refused for its own reason:

| Label | Refused because |
|---|---|
| `SIMULATED`, `UNKNOWN` | Not a reading of this HUD at all. |
| `CACHED` | A real reading of an earlier moment. `stat` is push-on-change, so a retained value is usually still current — but "usually" is the wrong standard for something written to disk and believed thereafter, and one dropped packet during combat pairs the frame with the previous HP. |
| `DERIVED` | What the screen reader itself publishes. Training the screen on a label the screen produced would teach it to agree with itself, and independence afterwards is the entire point. |

### A disagreement refuses the whole pass

The extractor splits on runs of ink, so numerals printed without a gap arrive as
one bitmap. Measured on the real client: `7305/7305` separates cleanly into nine
groups, `1420/1420` merges its `0` and `/` into one seven-pixel group and yields
eight. Pairing nine characters onto eight bitmaps by position would teach the atlas
that a merged `0/` is the character `0`, and every later reading of a real `0`
would be wrong in a way nothing downstream could detect.

So a count mismatch, a size mismatch, or one bitmap that has to be two characters
refuses the pass and leaves the atlas untouched. Refusing costs one frame. The
atlas is cumulative, and the next frame whose values print with a gap teaches the
same characters correctly.

## Consequences

- **Screen HP becomes an integer, `DERIVED`.** Verified on the T-03 crop: trained
  from `7305/7305` and read back as 7305/7305 through the shipping reader
  (`HudGlyphAtlasTests`).
- **The two sources become independent.** After training, a screen reading that
  disagrees with the wire is a real disagreement worth surfacing. Before training
  there was nothing to disagree. This is what makes the screen usable as the
  confirming source for facts the wire does not carry.
- **The atlas is not committed.** It is specific to one client, one scale and one
  display, and it lives in gitignored `data/` beside the crops. A fresh clone reads
  `atlas_not_trained_yet` and is told to train, which is a different state from
  broken and reports as one.
- **`HasTarget` is not solved by this.** Training makes the screen able to read
  numbers; reading the target frame is a separate ROI and a separate piece of work.
  This ADR removes the reason that work could not begin.
