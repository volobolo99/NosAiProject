[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('status', 'next', 'plan', 'run', 'verify', 'resume', 'stop')]
    [string]$Command = 'status'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$StateFile = Join-Path $RepoRoot '.nosai/PROJECT_STATE.md'
$PolicyFile = Join-Path $RepoRoot '.nosai/ORCHESTRATOR_EXECUTION.md'
$QueueDir = Join-Path $RepoRoot '.nosai/tasks'

function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required file not found: $Path"
    }
}

function Assert-SafeGitState {
    Push-Location $RepoRoot
    try {
        $branch = (git branch --show-current).Trim()
        if ([string]::IsNullOrWhiteSpace($branch)) { throw 'Detached HEAD is not allowed.' }
        if ($branch -eq 'main' -or $branch -eq 'master') {
            throw "Autonomous execution is blocked on protected branch '$branch'. Create a dedicated task branch first."
        }
        $status = git status --porcelain
        if ($status) {
            throw 'Working tree is not clean. Commit or explicitly isolate existing changes before autonomous execution.'
        }
        Write-Host "SAFE: branch=$branch, working-tree=clean"
    }
    finally { Pop-Location }
}

Require-File $PolicyFile
Require-File $StateFile
Require-File (Join-Path $RepoRoot 'NOSAI_MASTER_ROADMAP.md')

switch ($Command) {
    'status' {
        Write-Host 'NosAi Orchestrator status'
        Write-Host "Repository: $RepoRoot"
        Write-Host "State: $StateFile"
        Get-Content -LiteralPath $StateFile
    }
    'next' {
        Write-Host 'Authorized task discovery is repository-driven.'
        Get-ChildItem -LiteralPath $QueueDir -Filter 'TASK-*.md' | Sort-Object Name | ForEach-Object { $_.FullName }
    }
    'plan' {
        Write-Host 'PLAN mode: no files will be modified.'
        Write-Host 'Read .nosai/ORCHESTRATOR_EXECUTION.md and select the first authorized GREEN task.'
    }
    'run' {
        Assert-SafeGitState
        Write-Host 'RUN mode is intentionally a safety boundary.'
        Write-Host 'Configure a reviewed local agent adapter before enabling unattended implementation.'
        exit 2
    }
    'verify' {
        Push-Location $RepoRoot
        try {
            if (Get-Command dotnet -ErrorAction SilentlyContinue) {
                dotnet build
                dotnet test
            } else {
                throw 'dotnet CLI not found. Install the required .NET SDK before verification.'
            }
        }
        finally { Pop-Location }
    }
    'resume' {
        Write-Host 'RESUME requires an explicit human-approved unblock condition.'
        Write-Host 'Review .nosai/PROJECT_STATE.md before continuing.'
    }
    'stop' {
        Write-Host 'STOP: no autonomous process is running from this script.'
    }
}
