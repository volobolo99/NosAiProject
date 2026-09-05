# AP-00 / A4 — Cursor — Runtime Gate

Implement the runtime capability gate that consumes the existing profiler and exposes safe capability decisions to runtime consumers.

READ: `docs/ROADMAP_ESECUTIVA.md`, hardware profiler, runtime contracts, Gate1 bootstrap.
OWN: only runtime capability-gate files and direct tests.
REQUIRE: no hardcoded SKU; explicit UNKNOWN; no safety bypass; deterministic serialization; fail closed when required capability is unknown; complete files and tests.
HANDOFF: list files, commands, results, verification level, integration assumptions. Do not touch other agents' files.
