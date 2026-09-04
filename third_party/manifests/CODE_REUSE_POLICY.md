# NosAiProject — Code Reuse Policy for Cursor and Claude

Status: ACTIVE

## Purpose

This file explicitly permits Cursor and Claude to inspect, copy, adapt and integrate source code from the local `third_party/` vault when the source is accompanied by a recorded license and provenance entry.

The project is an academic/private test project. This does **not** by itself remove copyright or license obligations, so every reused component must retain the applicable notices and license metadata.

## Rules for agents

1. **Local-first:** search `third_party/` before searching the internet.
2. **Code reuse is allowed:** do not reject a source merely because it is GPL/LGPL/MIT.
3. **Never delete licensed source:** files under `third_party/` are preserved unless the human explicitly requests deletion.
4. **GPL/GPL-derived code:** may be copied/adapted when the source's license permits it. Preserve copyright/license notices, record the exact upstream path and revision, and mark modified files as modified where required.
5. **LGPL:** preserve the LGPL notice and follow the applicable LGPL requirements when modifying or integrating the component.
6. **MIT:** preserve the copyright and permission notice in the local license/provenance record and in redistributed copies where required.
7. **Unknown license:** do not copy code until the license is identified.
8. **No blind bulk-copy:** copy only files/classes required for the current NosAi task; avoid importing entire repositories unnecessarily.
9. **Provenance required:** every copied file must be listed in `third_party/manifests/SOURCES.md` or a more specific provenance manifest with repository, upstream path, revision/commit, license and integration status.
10. **Architecture boundary:** third-party code is reference/source material unless explicitly adapted into `src/`. It must not bypass NosAi's architecture, Safety Gate, Trust Gate or deterministic execution path.
11. **University observability boundary:** reused code must not introduce server-admin, GM/moderator, database-admin, hidden-state or privileged gameplay capabilities. Follow `docs/ADR-0021-unprivileged-observability-boundary.md` and `docs/UNPRIVILEGED_DEMO_SPEC.md`.
12. **No secrets:** never copy credentials, private keys, tokens, cookies or other secrets into the vault.
13. **Tests:** adapted code must receive NosAi tests appropriate to its role before entering the production path.

## Preferred workflow

`AGENT_LOOKUP.md` -> identify source -> inspect local copy -> read license/provenance -> copy/adapt minimal code -> add provenance -> test -> integrate.

## Important

The fact that the software is for study and not intended for profit is useful project context, but it is not treated by agents as a blanket legal exemption. License compliance remains part of the engineering record.
