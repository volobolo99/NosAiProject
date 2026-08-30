#!/usr/bin/env bash
set -euo pipefail

echo '=== NosAiProject validation ==='

python3 --version
python3 -m compileall -q nosai
python3 -m pytest -q

dotnet --version
dotnet restore src/NosAi.Runtime/NosAi.Runtime.csproj
dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj --configuration Release --no-restore

mapfile -t tests < <(find . -name '*Tests.csproj' -type f -not -path './.git/*')
if [[ ${#tests[@]} -eq 0 ]]; then
  echo 'No .NET test projects found; skipping .NET tests.'
else
  for project in "${tests[@]}"; do
    dotnet test "$project" --configuration Release --no-restore
  done
fi

echo '=== Validation completed successfully ==='
