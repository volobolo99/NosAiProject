# ADR-0022 — Reading the world from what the client draws

## Status

**Proposed, and gated on one experiment**, 2 Sep 2026.

The operator asked for this channel three times and, after the objections were put,
asked for it to be developed. This record develops it: what it would uniquely give, what
it would cost, and the one measurement that decides whether it can exist at all on this
installation. **Nothing is built before that measurement.**

**Builds on** [ADR-0014](ADR-0014-operator-chooses-the-data-path.md), which permits
injection and leaves the choice to the operator, and
[ADR-0019](ADR-0019-the-actuation-channel-for-character-control.md), which declined
injection *for actuation* and invited a later record to argue for another channel. This
is that record, and it argues for **observation only**.

## Context

### What is being proposed

Not recognising the game from pixels. **Intercepting the drawing itself** — the calls the
client makes to put the world on screen — so that identity and position are *read* rather
than inferred. Where a detector would say « something that resembles a monster, at pixel
640,380, confidence 0.87 », the draw call says « this texture, this quad, here ».

### What it would uniquely give

Three things, and only the third is unavailable elsewhere.

- **Screen coordinates for everything drawn.** This would close `T-10`, the map→screen
  projection that has failed five times, without calibrating anything: the client states
  where it puts each thing while it puts it there.
- **Identity without recognition** — no model, no threshold, no palette, no skin.
- **The state of the interface.** Is the inventory window open, is a dialog up, did the
  key press land. Neither the wire nor the client's memory answers this today, and it is
  the one gap `CATALOGO_AZIONI_E_POSTCONDIZIONI.md` leaves as `Unverified` by name.

The first two overlap with sources the project already has and prefers: entities come
off the wire with their real id, vnum, position and health, and the projection has a
self-calibrating path that reads back from memory which cell the client resolved. The
third does not overlap with anything.

### What the client actually is, measured

Read off this installation on 2 September 2026, not assumed:

| | |
|---|---|
| Renderer | **Direct3D 9** — `NostaleClientX.exe` imports `Direct3DCreate9` |
| Sprite helper | **none** — no `D3DXCreateSprite`, no `d3dx9_NN`; only `D3DXMatrix` appears |
| Executable | 4 095 344 bytes, Delphi |
| Graphics DLL in the game folder | **absent** — no `d3d9.dll` beside the executable |
| Also present | **`NPGLIB.dll`, 139 264 bytes, containing both `nProtect` and `GameGuard`** |

Two of these decide the shape of the work and one decides whether there is work at all.

**No `ID3DXSprite` means the hook points are the device's own.** The interception would
sit on `IDirect3DDevice9::SetTexture` and `DrawPrimitive` / `DrawIndexedPrimitive`,
correlating the bound texture with the screen-space quad. That is ordinary and
well-trodden; it is also more work than hooking a sprite helper would have been, because
the texture has to be mapped back to a game concept.

**`NPGLIB.dll` is nProtect GameGuard**, and its entire purpose is to stop code from
running inside this process and to detect API hooks. Both candidate techniques — an
injected DLL and a proxy `d3d9.dll` — are precisely what it exists to prevent.

## The distinction that has to be made honestly

This project already reads the client's memory, repeatedly, across restarts, and it
works: the map id oracle survived four maps and a restart, and the target oracle survived
six selections and a restart on 2 September. So GameGuard is either inactive here or does
not interfere with **external** reads.

**That is evidence about `ReadProcessMemory` from outside, and none whatsoever about code
running inside.** They are different things to an anti-cheat, and the first working says
nothing about the second. Treating the first as encouragement for the second would be
exactly the kind of unearned inference this project refuses everywhere else.

So the feasibility question is genuinely open, and it is cheap to close.

## Options considered

### Injected DLL with vtable hooks

Get the device's vtable, swap the entries, forward. Full control, and the standard
approach for D3D9.

It requires writing into a process this runtime has never written into. Today a wrong
offset produces a wrong number that gets declared `UNKNOWN`; a wrong hook does not
produce a wrong value, it **crashes the client or makes it behave unpredictably** — the
objection `ADR-0019` raised against injection for actuation, and it applies unchanged to
observation.

### Proxy `d3d9.dll` beside the executable

Windows loads a DLL from the application directory before the system one, so a wrapper
that forwards every call and observes on the way needs **no injection at all** — no
`CreateRemoteThread`, no writing into the process. It is what `dxwrapper` and
`DDrawCompat` do, and the slot is free: there is no `d3d9.dll` in the game folder.

Strictly less invasive than injection, and the same to an anti-cheat looking for an
unexpected module in the process.

### Do not adopt the channel

What the project is stuck on today is not what is around — that comes off the wire — nor
which entity is selected, which the memory oracle established on 2 September. The channel
would uniquely deliver interface state, which is not on the critical path.

## Decision

**Adopt the experiment, not the channel.**

Before any hook is written, one measurement answers whether this record can proceed:

> **Does anything of ours run inside `NostaleClientX.exe` at all?**

The smallest honest form: place a **proxy `d3d9.dll` that does nothing but forward every
call and write one line to a file when it is loaded**, beside the executable, and start
the client normally.

- **The client starts and the line appears** → the channel is available on this
  installation, and this record can be completed with a design.
- **The client refuses to start, or the line never appears** → the channel does not exist
  here, and no amount of hook engineering changes that. The record is closed as *not
  adopted for a measured reason* rather than an argued one.

The proxy forwards and observes nothing about the game in this experiment. It is not a
first version of the feature; it is the question asked in the cheapest way that can
answer it.

**If the experiment succeeds, the design must still answer three things** before anything
is built on it:

1. **Texture → game concept.** A bound texture is an identity only if it can be mapped to
   a vnum or an entity. The client's archives are already read by `NosArchive`, so this is
   the same kind of work the map grids were — but it is work, and it is where the value
   actually is.
2. **What is `LIVE` here.** A draw call is the client's own statement, which makes it a
   strong source; but a texture matched to a vnum through an archive is `DERIVED`, and the
   two must not be published under one label.
3. **The failure mode.** A hook that stops receiving calls looks identical to a game with
   nothing on screen. It must report `CaptureUnavailable`, never an empty world — the same
   rule the capture backend already carries.

## Consequences

- **Nothing changes today.** The runtime keeps reading the wire and the memory, and the
  interface state stays `Unverified` and declared, as `C3-3` records.
- **The experiment is the operator's**, not an agent's: it puts a file next to the game
  client and starts it. It is reversible by deleting that file.
- **If it fails, the answer is recorded rather than re-litigated.** The idea has come up
  three times; a measured no is worth more than three arguments.
- **`ADR-0019` is untouched.** This record proposes an observation channel. Actuation
  stays operating-system input, for the reason that decided it: an act that passes through
  the client's own code inherits every refusal the client already implements.
