$ErrorActionPreference = 'Stop'

Write-Host '=== NosAiProject build ==='

dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore

Write-Host '=== Build completed successfully ==='
