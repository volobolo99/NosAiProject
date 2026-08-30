# NosAiProject — AI Agent Execution Checklist

Use this checklist for every Cursor/Claude Code task.

## Before implementation

- [ ] Read `CLAUDE.md`.
- [ ] Read `NOSAI_MASTER_ROADMAP.md`.
- [ ] Read `docs/NOSAI_ARCHITECTURE_BASELINE.md`.
- [ ] Read relevant ADRs.
- [ ] Inspect existing implementation and tests.
- [ ] Identify the exact milestone and acceptance criteria.
- [ ] Check for unexpected working-tree/repository changes.

## During implementation

- [ ] Keep scope limited to the requested milestone.
- [ ] Preserve architecture boundaries.
- [ ] Preserve real/demo data separation.
- [ ] Validate external input.
- [ ] Preserve security and authorization boundaries.
- [ ] Add tests for changed behavior.
- [ ] Avoid unrelated refactoring/dependencies.

## Before completion

- [ ] Build affected components.
- [ ] Run relevant tests.
- [ ] Run integration/contract tests when boundaries changed.
- [ ] Inspect the complete diff.
- [ ] Check for secrets/credentials.
- [ ] Update documentation/contracts.
- [ ] Record exact commands and results.
- [ ] Assign the correct verification level.

## Verification levels

`Present` = code exists.

`Integrated` = connected to the intended component boundary.

`Done` = build and required local tests pass.

`Verified` = acceptance evidence, including real-environment evidence where required, is recorded.
