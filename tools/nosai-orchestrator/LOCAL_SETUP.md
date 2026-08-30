# Local Orchestrator Setup

## 1. Clone the repository

Clone `NosAiProject` locally and open the repository root in Cursor.

## 2. Keep credentials out of Git

Do not place API keys, OAuth tokens, Claude credentials, Cursor credentials, or `.env` files containing secrets inside the repository.

Use the official local authentication mechanism of each installed tool.

## 3. Verify executables

From PowerShell:

```powershell
Get-Command git
Get-Command dotnet
Get-Command claude -ErrorAction SilentlyContinue
Get-Command cursor -ErrorAction SilentlyContinue
```

Claude and Cursor discovery is informational at this stage. The repository does not assume a specific executable name or installation method.

## 4. Verify repository state

From the repository root:

```powershell
git status
.\tools\nosai-orchestrator\nosai.ps1 status
.\tools\nosai-orchestrator\nosai.ps1 next
```

Autonomous execution must occur on a dedicated branch with a clean working tree.

## 5. First verification run

```powershell
.\tools\nosai-orchestrator\nosai.ps1 verify
```

Fix environment/build problems before enabling any autonomous implementation adapter.

## 6. Adapter activation

The next implementation step is to add a reviewed adapter for the exact Claude/Cursor CLI available on the operator machine. Do not enable unattended command execution until the CLI invocation, workspace restrictions, timeout policy, output capture, and exit-code handling have been tested manually.
