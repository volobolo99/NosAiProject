Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Find-AgentExecutable {
    param([Parameter(Mandatory)][string[]]$Candidates)
    foreach ($candidate in $Candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($null -ne $command) { return $command.Source }
    }
    return $null
}

function Get-AgentAdapters {
    [pscustomobject]@{
        Claude = Find-AgentExecutable @('claude', 'claude.exe')
        Cursor = Find-AgentExecutable @('cursor', 'cursor.exe')
        Git = Find-AgentExecutable @('git', 'git.exe')
        DotNet = Find-AgentExecutable @('dotnet', 'dotnet.exe')
    }
}

function Assert-AgentEnvironment {
    $adapters = Get-AgentAdapters
    if ($null -eq $adapters.Git) { throw 'Git executable not found.' }
    if ($null -eq $adapters.DotNet) { throw '.NET SDK executable not found.' }
    $adapters
}
