$ErrorActionPreference = 'Stop'

Write-Host '=== NosAiProject tests ==='

python -m compileall -q nosai
python -m pytest -q

$projects = Get-ChildItem -Path . -Filter *Tests.csproj -Recurse -File | Where-Object { $_.FullName -notmatch '\\.git\\' }
if ($projects.Count -eq 0) {
    Write-Host 'No .NET test projects found; skipping .NET tests.'
} else {
    foreach ($project in $projects) {
        dotnet test $project.FullName --configuration Release
    }
}

Write-Host '=== Tests completed successfully ==='
