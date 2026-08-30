# NosAiProject — Build, Test & Release

**Version:** 1.0  
**Date:** 2026-08-30  
**Status:** ACTIVE

## 1. Scope

This document defines the reproducible validation path used by developers and AI agents. Commands must be run from repository root.

## 2. Environment

- Python: `3.12`
- .NET: `8.0.x`
- OS: CI uses Ubuntu for cross-platform validation; real-client validation requires the supported Windows target environment.

## 3. Python validation

```bash
python --version
python -m pip install --upgrade pip
python -m pip install pytest
python -m compileall -q nosai
python -m pytest -q
```

## 4. .NET validation

```bash
dotnet --version
dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --configuration Release
```

The Gate 1 in-process suite can also be invoked directly:

```bash
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate1-test
```

## 5. Full validation sequence

1. Clean working tree or explicitly document local changes.
2. Restore dependencies.
3. Compile Python sources.
4. Run Python tests.
5. Restore/build .NET.
6. Run available .NET tests.
7. Inspect generated artifacts and Git diff.
8. Run integration/contract tests required by the changed milestone.
9. For real-client/device features, execute the documented real-environment validation.

## 6. CI

The canonical CI workflow is `.github/workflows/ci.yml`. It currently performs Python compilation/tests, a Release build of `src/NosAi.Runtime/NosAi.Runtime.csproj`, and `dotnet test` for every `*Tests.csproj`.

CI success is necessary but does not imply real-environment verification.

## 7. Release procedure

1. Confirm roadmap milestones are `DONE` or `VERIFIED` as required by release scope.
2. Run the full validation sequence.
3. Review security/dependency state.
4. Update release notes and version metadata.
5. Create a version tag such as `v1.0.0`.
6. Publish only validated artifacts.
7. Record verification evidence.

## 8. Rollback

Use a revert commit or a previously validated release tag. Do not rewrite shared history as a rollback mechanism.

## 9. AI-agent requirement

Agents must report the exact commands executed and whether each step passed, failed or was skipped. A skipped real-environment check must never be reported as passed.
