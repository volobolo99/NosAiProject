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

Remaining Python is the operator surface (`nosai.dashboard`, `nosai.phone`,
`nosai.bringup`) plus research packages without a complete C# counterpart.
See `docs/INVENTARIO_PYTHON.md`. `compileall` and `pytest` cover that tree, not
the deleted COPERTO packages.

```bash
python --version
python -m pip install --upgrade pip
python -m pip install pytest
python -m compileall -q nosai
python -m pytest -q tests
```

## 4. .NET validation

```bash
dotnet --version
dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release
dotnet test tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj --configuration Release
```

The gate in-process suites can also be invoked directly:

```bash
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate1-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate2-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate3-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate4-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate5-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate6-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --input-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --netobserve-test
```

Real-environment probes. These need an interactive desktop and validate what the
local suites cannot: that capture and injection actually reach the OS.

```bash
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --dxgi-probe
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --input-probe
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --host-test
```

Ogni sottosistema porta la propria suite di certificazione:

```bash
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --storage-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --navigation-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gateway-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --raids-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --miniland-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --localai-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --hardware-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --gate6-test
dotnet run --project src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release -- --host-test
```

`--list-suites` stampa l'elenco completo dei flag disponibili, così non serve
leggere questo documento per sapere cosa si può eseguire.

> **Perché conta.** Lo `StartupObject` fissato nel `.csproj` rende irraggiungibile
> ogni altro `Main` dell'assembly. Sette di queste suite erano state scritte e poi
> **non eseguite nemmeno una volta**, perché nessun flag le invocava e il loro
> punto d'ingresso era morto. Lo stesso difetto teneva nascosti due bug in Gate 3
> e lasciava la suite di Gate 4 rossa. Una suite che nessuno sa lanciare è una
> suite che nessuno lancia.

If a Windows Application Control policy blocks the generated apphost
(`0x800711C7`, "Un criterio di controllo dell'applicazione ha bloccato il
file"), run the managed assembly through the shared host instead. The policy
applies to the `.exe`, not to the DLL:

```bash
dotnet src/NosAi.Runtime/bin/Release/net8.0-windows/NosAi.Runtime.dll --gate1-test
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
