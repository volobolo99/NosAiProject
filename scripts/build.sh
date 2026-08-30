#!/usr/bin/env bash
set -euo pipefail

echo '=== NosAiProject build ==='

dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore

echo '=== Build completed successfully ==='
