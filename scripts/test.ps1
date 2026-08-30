$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Write-Host '=== NosAiProject tests ==='

python -m compileall -q nosai
Assert-LastExitCode 'python -m compileall'

python -m pytest -q
Assert-LastExitCode 'python -m pytest'

$projects = Get-ChildItem -Path . -Filter *Tests.csproj -Recurse -File | Where-Object { $_.FullName -notmatch '\\.git\\' }
if ($projects.Count -eq 0) {
    Write-Host 'No .NET test projects found; skipping .NET tests.'
} else {
    foreach ($project in $projects) {
        dotnet test $project.FullName --configuration Release
        Assert-LastExitCode "dotnet test $($project.Name)"
    }
}

Write-Host '=== Tests completed successfully ==='
