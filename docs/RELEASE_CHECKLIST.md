# NosAiProject — Release Checklist

## Before release

- [ ] Roadmap scope explicitly frozen.
- [ ] All in-scope milestones are `DONE` or `VERIFIED` according to their gate.
- [ ] Working tree contains no accidental/generated files.
- [ ] No secrets, tokens, private keys or credentials are present.
- [ ] Python compilation passes.
- [ ] Python tests pass.
- [ ] .NET restore/build passes.
- [ ] Available .NET tests pass.
- [ ] Integration/contract tests pass.
- [ ] Security checks pass.
- [ ] Real-environment checks required by scope pass.
- [ ] Documentation and release notes are updated.
- [ ] Version metadata is consistent.

## Release evidence

Record:

- commit SHA;
- version/tag;
- exact validation commands;
- CI run;
- integration evidence;
- real-environment evidence;
- known limitations.

## Rollback

Use the last validated release tag or a revert commit. Do not rewrite shared history.
