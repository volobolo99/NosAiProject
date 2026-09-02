# ADR-0020 — One authorisation path to the act

## Status

Proposed, 2 Sep 2026.

**Refines** [ADR-0015](ADR-0015-adopt-roadmap-esecutiva-as-canonical-architecture.md),
which adopted `docs/ROADMAP_ESECUTIVA.md` as the canonical architecture: §§ 8.2 and 8.3
of that document — the Gate 7 execution contract — are superseded **for the actuation of
character control**. Everything else in it stands.

**Builds on:** [ADR-0003](ADR-0003-runtime-safety-authority.md) (the runtime is the
safety authority), [ADR-0019](ADR-0019-the-actuation-channel-for-character-control.md)
(operating-system input is the channel).

**Narrows** invariant `INV-07` (zero allocation on the critical path) to the scope its
own enforcement points describe. That is the one part of this record that weakens a
stated invariant, it is argued in the Decision, and it is why the status is *Proposed*
rather than *Accepted*.

## Context

Two authorisation paths to the same act exist in this repository. One is designed and
has never been built; the other is built, tested, and runs. Nothing says which one wins,
and the next piece of work — `P4`, the first real input emitted at the client — is the
one that would silently choose.

### What Gate 7 designs

`ROADMAP_ESECUTIVA.md` § 8.2 declares `IInputSink.TryDispatch(in PlanStep, in
ExecutionToken, out ExecutionReceipt, out FaultCode)`, with `ExecutionToken` carrying a
deadline, a granted scope, a trust tier, an `IntentDigest` and a `SafetySignature`; and
`IVerifier.Verify(receipt, before, after)` with `IPostCondition` and
`VerificationAction`. § 8.3 adds the rules: the signature is compared in constant time,
the deadline and the scope subset are checked, the digest is compared against the
current intent, the dispatch instant is compensated by `RTT_ewma / 2` and cancelled when
it misses by more than 30 ms, and nothing allocates.

It states one property in particular, and it is the right property:

> Non esiste alcun overload, flag di debug o percorso alternativo che consenta il
> dispatch senza token: proprietà verificata dal test di architettura.

**None of those types exists.** `ExecutionToken`, `IInputSink`, `ExecutionReceipt` and
`IVerifier` return no match anywhere under `src/` or `tests/`.

### What exists and runs

A different chain, assembled while the character-control work was done:

```
SafetyGate.TryAuthorize      -> SafetyToken, HMAC over the CandidateId, 1500 ms, single use
AuthorizedActionExecutor     -> validates signature, binding and consumption
IActionEffector.ApplyAsync   -> (candidate, cancellationToken)          <- the token stops here
InputActionEffector          -> resolves the keybind, projects the pixel
GatedInputBackend            -> the boundary ADR-0003 requires, taken concretely
CommitPointValidator         -> five conditions revalidated within 8 ms of emission
ActuationScope               -> one act, with the release of anything it held
Win32InputBackend            -> SendInput
```

It is tested, it refuses by name, and `ADR-0019`'s consequences make its commit point
load-bearing: `SendInput` does not address a window, so confinement is a property this
pipeline builds rather than one the API supplies.

### A second entry, which landed while this record was being written

`C-P4` was committed to `main` on 2 September 2026 as `b98e681`: `StepGuardChain`,
`MovementVerifier`, `OccupancyFreshness` and `SingleStepExecutor` under
`src/NosAi.Runtime/Navigation/`, with `StepGuardTests`. It is good work and it respects
the boundary — it takes
`GatedInputBackend` **concretely**, for the same reason `InputActionEffector` does, so it
cannot step around the gate or around the commit point.

It is nonetheless a **second caller of `GatedInputBackend.TryBeginActuation`**. There are
now two in production code:

| Caller | Reaches the gate through | Carries a `SafetyToken` |
|---|---|---|
| `SessionActuationAuthority` (the Gate 3 cycle) | `SafetyGate` → `AuthorizedActionExecutor` → `InputActionEffector` | yes, consumed one layer above the effector |
| `SingleStepExecutor` (the `--step` path `P4` describes) | `StepGuardChain` | **no** |

Both pass the commit point: `MayMove` refuses with `commit_scope_required` when a commit
point is configured and no scope is open, so nothing emits outside a scope. Confinement
holds on both. What does not hold on both is **attribution**: at the moment of emission
the gate cannot say under whose authority it is acting, and one of the two paths has no
authority object at all.

`SingleStepExecutor` is not yet reachable from any command — `--step` does not exist.
That is precisely why this is the moment to record the rule rather than the moment to
discover it, and why the consequence is a parameter rather than a rewrite.

### Where the two disagree

Four points, and none of them is cosmetic.

| | Gate 7 (designed) | Built |
|---|---|---|
| What the signature covers | the intent digest, plus scope and tier | **the `CandidateId` alone** |
| Where the token stops | at the boundary that emits | one layer above it: `IActionEffector` cannot receive it |
| What revalidates before the act | latency compensation, `RTT_ewma / 2`, ±30 ms | the commit point: geometry, foreground, point ownership, human precedence, scale |
| The vocabulary of an outcome | `ExecutionReceipt` + `FaultCode` enum | `ExecutionResult` + `ExecutionState` + named string reasons |

The first is already recorded as debt in `docs/GATE3_PIPELINE.md`: `candidate with {
Target = ... }` produces a different candidate with the same `CandidateId`, and the token
goes on validating it. The token authorises *an identifier*, not *an action*.

The second means the property Gate 7 states is not enforced where it matters. Between
the executor that consumes the token and the input that leaves the process there is the
effector, which resolves a keybind and projects a coordinate — work done **after**
authorisation and covered by nothing.

The third is the one that decides the shape of the answer, and the reason is a date.
Gate 7 § 8 was written before `ADR-0019`. Its revalidation is latency compensation
against a **round trip**, which is the right worry for an act sent on a channel that
addresses the client. `ADR-0019` chose a channel that addresses *whatever holds focus*.
There is no round trip to compensate, and nothing in Gate 7 § 8 closes the question that
channel actually raises: did the intended window receive it.

## Options considered

### Build Gate 7 as written, retire the commit point

Honest to the canonical roadmap, and it would deliver the two properties the built chain
lacks.

It also deletes tested code that answers a question Gate 7 does not ask, and replaces a
measured 8 ms budget between the last check and the emission with a 30 ms tolerance
around an `RTT_ewma` that has no round trip to estimate. The confinement problem would
be reopened, with the first real input as the occasion to discover it.

**Rejected.** Not because the design is worse in the abstract, but because it is a design
for the channel `ADR-0019` did not choose.

### Keep the built chain, mark Gate 7 § 8 obsolete

Cheapest, and it loses two real properties. The signature would go on covering an
identifier, so a target substituted between authorisation and execution would still pass;
and "no emission without a token" would remain a fact about the order of calls rather
than about the types, which is exactly the kind of guarantee that survives until someone
adds an overload.

**Rejected.** Both gaps are already written down as debt. Declaring the debt canonical is
not a decision.

### Adopt the built chain and import the two properties Gate 7 has

**Adopted.** The structure follows the channel that was chosen; the two guarantees follow
the design that stated them.

## Decision

### 1. The commit point is the boundary, and there is only one boundary

`IInputSink`, `ExecutionToken` and `ExecutionReceipt` are **not built**. Every act
reaches the input stream through `GatedInputBackend`, and through the commit point inside
it. No second boundary is introduced, and no caller takes `IInputBackend` where the gate
is expected.

**Two entries to that boundary are legitimate; a third state is not.** An act is either
planned — the Gate 3 cycle, authorised by a `SafetyToken` — or commanded, by an operator
who typed something. Both are authorities. What is not permitted is the state where the
gate cannot say which: an emission attributable to nobody.

### 2. A scope names its authority

`GatedInputBackend.TryBeginActuation` takes, beside the `CommitRequest`, the authority
under which the scope is opened: a `SafetyToken`, or a named operator command. There is
no overload without it, and the audit event for the act records which of the two it was.

This is Gate 7's *"no dispatch without a token"* kept in the only form that survives the
existence of an operator command — which the project needs, since `--input-guards`,
`--step` and the physical proofs of `P2` and `P3` are all human-driven acts against the
real client. It is enforced at the one place both entries already pass through, which is
why it is one change rather than a rule repeated in every caller.

### 3. The token signs the act, not the identifier

The HMAC covers an **intent digest** over every field that changes what the act does:
`CandidateId`, `Type`, the target's discriminator and its fields, `SkillOrItemId`,
`RequiredTrust`. `candidate with { Target = ... }` must stop validating against a token
issued for the original, and a test must assert exactly that.

This closes the debt in `GATE3_PIPELINE.md` and is the part of Gate 7 § 8.3 kept
verbatim in intent.

### 4. The token reaches the boundary that emits

`IActionEffector.ApplyAsync` takes the token. An effector that cannot receive one cannot
be composed into the pipeline, so "nothing emits without an authorisation bound to this
act" becomes a property of the signature rather than of the call order — Gate 7's
architecture test, kept, at the boundary `ADR-0019` chose.

**What that boundary can actually verify, added while implementing this** (2 Sep 2026).
The sentence above did not say, and the difference matters. The effector **cannot check
the signature**: the key lives in the issuer and must not leave it. What it can check is
that the token is bound to *this* candidate and that it has not expired **in the instant
of emission** — and that is precisely the interval this record says is covered by
nothing, because between the executor consuming the token and the click there is the
keybind lookup and the projection.

So the guarantee here is of two different strengths and they must not be conflated. The
strong one is **by type**: `ApplyAsync` requires a token, so an effector that cannot
receive one does not exist as far as the pipeline is concerned, and a reflection test
holds that over every implementation rather than over the ones that happen to exist
today. The weaker one is **by check**: binding and freshness, at the last moment before
the act.

Whoever reads this must not come away believing the effector re-verifies the signature.
A defence that appears to be there is worse than one that is absent, which is the failure
this whole record exists to remove.

**What the digest does not cover, deliberately.** The pixel. The coordinate is computed
in the effector, after authorisation, from the map point the digest *does* cover; what
guards the pixel is the commit point, whose third and fifth conditions ask whether that
point belongs to the session window and whether the scale it was computed under is still
live. Two guards, two subjects, and the seam between them is the projection. Nothing
binds a pixel to a digest, and that limit is recorded here rather than left to be
discovered.

### 5. One vocabulary for an outcome

`ExecutionResult`, `ExecutionState` and named string reasons. `FaultCode` and
`ExecutionReceipt` are not introduced as a parallel set.

The named reason is not a stylistic preference: `keybind_not_configured:consumable.101`
tells the operator what to configure and a seven-value enum does not, and that property
is asserted across the character-control documents.

### 6. Emission latency replaces latency compensation

`CommitDecision.ElapsedSinceValidation`, against the 8 ms budget already implemented, is
the measure. `t_target = t_dispatch + RTT_ewma / 2` and the ±30 ms window are **not**
adopted: the client is a local process reached through the system input stream, and the
risk being managed is not delay in transit but the window changing under the act.

### 7. `INV-07` keeps the scope its enforcement points describe

`INV-07` names `ArrayPool<T>`, `Span<T>`, `struct Pack = 1` and a BenchmarkDotNet gate —
all transport-codec constructs, and it was written for the Gate 1 framing path, where
frames arrive continuously and an allocation per frame is an allocation per packet.

On the actuation path it is scoped to the emission itself. The cost of an act is
dominated by `SendInput` and by the commit point's own Win32 reads; a refusal allocates a
string on a path that then **emits nothing**, and the diagnostic value of that string is
established. Zero-allocation stays mandatory where its enforcement points live.

This narrows a stated invariant, which is why this record is Proposed: it needs the
operator's acceptance, not an author's.

### 8. What Gate 7 keeps unchanged

The journal appended before the next cycle begins; three failed verifications within 60 s
demoting to `Quarantined`; no promotion from any verification outcome (`INV-06`); and the
divergence thresholds of § 8.3, which
[CATALOGO_AZIONI_E_POSTCONDIZIONI.md](../CATALOGO_AZIONI_E_POSTCONDIZIONI.md) makes
computable for the first time.

## Consequences

- **`SingleStepExecutor` acquires an authority before `--step` is wired.** It is the
  cheapest moment: the class exists, nothing calls it yet, and an operator command is a
  legitimate authority — it simply has to be one. The change is a parameter, not a
  redesign, and none of `StepGuardChain`, `MovementVerifier` or `OccupancyFreshness` is
  affected.
- **`C-P4` is written, and it composed against the right chain.** The guard ladder takes
  the gate concretely and does not carry a second copy of the commit point, which are the
  two properties this record would otherwise have had to impose after the fact.
- **`PostConditionTable` lives in Gate 3**, beside `ActionExecutionVerifier`, not in
  `NosAi.Core` beside types that do not exist. This answers § 8 of the action catalogue,
  which named this decision as the thing it could not take alone.
- **Changing what enters the HMAC is a security-behaviour change** and carries its own
  tests: a forged token, a token whose candidate had its target rebound, an expired one,
  and one already consumed. Tokens in flight are not a migration concern — the lifetime
  is 1500 ms and the issuer and verifier are the same process — but the change must not
  ride along inside another commit.
- **The duplicated-types debt becomes load-bearing.** `SafetyGate` exists in `Gate3`,
  `Gate6` and `Safety`, and `TrustTier` in four namespaces. One authorisation path means
  one `SafetyGate`; two copies of the gate are two answers to "was this act authorised".
  Not resolved here, and now blocking rather than untidy.
- **`ROADMAP_ESECUTIVA.md` §§ 8.2–8.3 no longer describe the built system** for
  actuation. `docs/SOURCE_OF_TRUTH.md` records this ADR as the resolution, which is the
  mechanism that file exists to provide.
- **The door `ADR-0019` left open stays open, and Gate 7 comes back through it.** If a
  later ADR ever adopts the protocol channel, the act would no longer pass through a
  window, the commit point would no longer be the relevant guard, and Gate 7 § 8 — token
  at the sink, receipt, latency compensation, zero-allocation dispatch — becomes the
  right design again. This record supersedes it for the channel in use, not for every
  channel.
