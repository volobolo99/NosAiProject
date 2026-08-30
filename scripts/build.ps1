$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"

Write-Host '=== NosAiProject build ==='

dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
Assert-LastExitCode 'dotnet restore'

dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore
Assert-LastExitCode 'dotnet build'

Write-Host '=== Build completed successfully ==='
