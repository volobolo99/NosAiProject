# NosAiProject Third-Party Source Vault

This directory contains selected source files copied from external open-source projects for academic study and possible code reuse.

## Reuse policy

Cursor and Claude Code are explicitly allowed to:

1. Read files under `third_party/sources/`.
2. Copy useful implementation code into NosAiProject when technically appropriate.
3. Adapt/refactor copied code to the NosAi architecture.
4. Preserve the original copyright/license notices and provenance metadata.
5. Prefer the local vault over repeatedly searching external repositories.

GPL/LGPL material is intentionally retained and may be reused subject to its license terms. No third-party file in this vault should be deleted automatically.

## Safety boundary

Third-party code is reference material, not authority. Before integrating anything into the gameplay path, agents MUST check:

- `docs/adr/ADR-0021-unprivileged-observability-boundary.md`
- `docs/UNPRIVILEGED_DEMO_SPEC.md`
- the current roadmap and ADRs

No code from this vault may introduce GM/mod/admin controls, server-database access, hidden gameplay state, privileged APIs, secret credentials, or server-side authority into the autonomous client path.

## Integration rule

`third_party/sources/**` is not compiled automatically. Production code belongs under `src/` and must be covered by tests and provenance records.

For every integrated third-party implementation, update the corresponding file under `third_party/provenance/` with:

- upstream repository
- source path
- upstream commit/blob SHA
- license
- original vs modified status
- destination in NosAiProject
- tests validating the adaptation
