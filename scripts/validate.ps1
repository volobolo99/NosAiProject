$ErrorActionPreference = 'Stop'

Write-Host '=== NosAiProject validation ==='

python --version
python -m compileall -q nosai
python -m pytest -q

dotnet --version
dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore

$testProjects = Get-ChildItem -Path . -Filter '*Tests.csproj' -Recurse
if ($testProjects.Count -eq 0) {
    Write-Host 'No .NET test projects found; skipping .NET tests.'
} else {
    foreach ($project in $testProjects) {
        dotnet test $project.FullName --configuration Release --no-restore
    }
}

Write-Host '=== Validation completed successfully ==='
