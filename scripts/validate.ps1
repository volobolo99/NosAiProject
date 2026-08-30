$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Write-Host '=== NosAiProject validation ==='

python --version
Assert-LastExitCode 'python --version'

python -m compileall -q nosai
Assert-LastExitCode 'python -m compileall'

python -m pytest -q
Assert-LastExitCode 'python -m pytest'

dotnet --version
Assert-LastExitCode 'dotnet --version'

dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
Assert-LastExitCode 'dotnet restore'

dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore
Assert-LastExitCode 'dotnet build'

# No --no-restore here: only NosAi.Runtime.csproj was restored above, so a test
# project would fail on its first run.
$testProjects = Get-ChildItem -Path . -Filter '*Tests.csproj' -Recurse -File | Where-Object { $_.FullName -notmatch '\\.git\\' }
if ($testProjects.Count -eq 0) {
    Write-Host 'No .NET test projects found; skipping .NET tests.'
} else {
    foreach ($project in $testProjects) {
        dotnet test $project.FullName --configuration Release
        Assert-LastExitCode "dotnet test $($project.Name)"
    }
}

Write-Host '=== Validation completed successfully ==='
