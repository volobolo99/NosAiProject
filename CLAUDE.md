# NosAiProject — Claude Code Instructions

## Mission

You are an implementation agent for NosAiProject. Build a genuinely autonomous player for the private educational/test environment. Optimize for correctness, maintainability, security, testability and small reviewable changes.

## Read first

Before changing code, inspect only the canonical entry points required by the assigned task:

1. `docs/agents/AGENT_WORK_PROTOCOL.md`
2. `docs/ROADMAP_ESECUTIVA.md`
3. `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`
4. `docs/NOSAI_ARCHITECTURE_BASELINE.md`
5. the assigned phase/agent command file under `docs/agents/phases/`
6. only the files explicitly listed by that command
7. relevant tests/ADRs named by that command

`NOSAI_MASTER_ROADMAP.md` has been removed: it was fully superseded by `docs/ROADMAP_ESECUTIVA.md` (see ADR-0015 and ADR-0025) and its content added nothing not already tracked in `docs/STATO_IMPLEMENTAZIONE.md` and this repository's git history.

## Mandatory agent completion protocol

- Work only inside the file ownership declared by the assigned command.
- Never let two agents edit the same source file concurrently.
- A task is incomplete until every requested file exists in complete form, compiles, and has its required tests/documentation.
- Never leave TODO/FIXME placeholders, pseudocode, ellipses, partial methods, commented-out replacement code, or intentionally broken intermediate files in a completed task.
- Existing files must be replaced with their complete contents when the command requests a full-file rewrite; do not emit partial snippets as the deliverable.
- Do not stop after analysis. Implement, test, build, inspect the final diff, and document the result.
- If a dependency is missing, implement the smallest complete dependency inside the declared ownership or stop before touching another agent's files and report the exact blocker.
- Integration agents own conflict resolution and final build/test verification for a phase.
- Never claim `Done` or `Verified` without evidence.

## Multi-agent synchronization

Use the execution matrix in `docs/agents/AGENT_EXECUTION_MATRIX.md`. The default pattern is five parallel domain agents followed by one integration/verification agent. Parallel agents have disjoint file ownership. The integration agent runs only after all parallel tasks have produced complete artifacts. A later phase never starts from an unverified earlier phase.

## Product boundary

NosAi may use PC CPU/GPU/NPU/RAM/storage, normal Windows APIs, client-visible network traffic, legitimately readable local client memory, screen/pixel capture, OCR/CV, local telemetry and software client-control mechanisms. Mouse and keyboard are permitted but optional. No external automation hardware is required or permitted beyond those devices.

Never use server DBs, GM/mod/admin tools, server consoles, privileged APIs, hidden/debug state, secret credentials or any channel unavailable to an ordinary client/player. Do not modify the server to expose hidden gameplay state.

## Architecture invariants

- Canonical flow: `Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility → Strategic Orchestrator → HTN/GOAP → Guard → Trust/Authorization → Safety → Execute → Verify → Re-observe`.
- Runtime is authoritative for authorization and safety.
- No LLM, ML model, heuristic or stochastic component has direct execution authority.
- Unknown is not zero, false or empty.
- Every important gameplay fact carries provenance, confidence and freshness.
- Real, derived, cached and simulated data remain explicitly distinguishable.
- Fail closed where safety requires it.
- Public contracts and protocols are versioned when compatibility can change.

## Implementation workflow

For every task: inspect → plan → identify dependencies/ownership → implement complete files → add/update tests → build affected projects → run tests/benchmarks → inspect diff/security/boundaries → update canonical docs → report evidence.

## Autonomy requirements

The long-term target is a player that can autonomously perceive maps, estimate dimensions, explore, navigate, recognize entities, fight adaptively, understand multi-step quests, manage inventory/equipment/progression, learn from failures and recover from disturbances.

Do not hardcode a static macro where a model/planner is required. Prefer strategic goals → HTN/GOAP → reactive recovery. Prediction is advisory only.

## Do not

- delete or weaken tests;
- silently change public APIs/protocols;
- introduce dependencies without justification;
- replace real providers with mocks on production/critical paths;
- label simulated data as live;
- bypass authentication, authorization or Safety;
- claim `Verified` without evidence;
- broad-refactor unrelated code;
- commit secrets, credentials or machine-specific sensitive data.

## Real-environment rule

Mocks/fixtures support isolated tests only. Client integration, perception and actuation require real target validation before `Verified`.

## External reference data

Before declaring a data gap permanently blocked (a byte/field with no known meaning, a missing lookup table, an undocumented file format), check for a legitimate, verifiable external source — community-maintained documentation, official specs, or open-source reference implementations that had to solve the same problem. Fetch and cite it (source name/URL in the commit and in the code's own doc comment) rather than guessing or leaving the gap unexamined.

An external source is a lead, not a ground truth: cross-check at least one decoded value against a real, observed one (a live client reading, a known in-game number) before trusting it on any path that is not purely diagnostic. A field the source itself does not document stays `Unknown` — never filled in by inference from an adjacent, documented field.

Do not write a test whose only purpose is to measure or report an unresolved data gap (a "coverage" assertion that a field correctly stays `Unknown`) without first checking whether a real external source would close that gap instead. This does not relax "delete or weaken tests" above: when a gap closes, the test that asserted the old `Unknown` behavior is updated to assert the new, verified decoded value — never deleted to avoid updating it.

## Git discipline

Use small imperative commits with one coherent purpose. Never rewrite unrelated history. Keep each phase's parallel-agent commits disjoint by file ownership. Integration commits may combine only the completed outputs of the current phase.

## Completion report

Every agent must report: task ID; files created/modified; implementation summary; build/test commands and results; verification level (`Present`, `Integrated`, `Done`, `Verified`); blockers; and exact handoff notes for the integration agent.

## Economia dei token e lingua (Claude)

**Claude può scrivere quando è necessario — contratti, algoritmi, audit,
integrazione, documentazione di stato — ma sempre in modo preciso e
contenuto: mai una parola in più di quanto serve a comunicare il fatto.**
A differenza di DeepSeek (vincolato a scrivere solo codice,
`docs/agents/DEEPSEEK_TASKS.md` "REGOLA ASSOLUTA #2"), produrre contratti
fondanti, algoritmi puri, audit indipendenti, integrazione finale e
documentazione di fase è esplicitamente compito di Claude
(`docs/agents/AGENT_WORK_PROTOCOL.md`) — scrivere testo oltre al codice
non è quindi vietato in sé. È vietato lo spreco: la stessa cosa detta in
dieci righe quando ne bastano tre, una spiegazione che ripete quanto il
codice o un commento già dice da sé, un riepilogo di ciò che chi legge
può già vedere direttamente nel diff.

Concretamente:

- Ogni documento (`_STATUS.md`, `_AUDIT.md`, specifiche per DeepSeek)
  riporta fatti verificati con citazione esatta (file:riga, comando
  eseguito, risultato numerico) — non prosa discorsiva attorno ai fatti.
- Prima di scrivere un documento nuovo, valutare se un aggiornamento di
  poche righe a un documento esistente basta — non aprire un nuovo file
  per un fatto che sta in una nota a margine di quello già in scrittura.
- Nessuna sezione "riepilogo"/"conclusione" che ripete quanto appena
  detto nello stesso documento.
- Le risposte all'utente restano dirette e pragmatiche, mai un resoconto
  passo-passo di ogni comando eseguito quando l'esito conta più del
  percorso.

**Lingua**: ogni frase rivolta all'utente — in conversazione, nei report
di completamento, nelle descrizioni di commit, in qualunque documento di
stato — è sempre in italiano, per Claude come per DeepSeek. Codice,
identificatori, nomi di tipo/metodo/file e commenti nel codice sorgente
restano in inglese standard, coerente con la convenzione già in uso in
tutto il repository — questa regola riguarda la comunicazione, mai il
codice.
