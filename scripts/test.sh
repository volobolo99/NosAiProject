#!/usr/bin/env bash
set -euo pipefail

echo '=== NosAiProject tests ==='

python3 -m compileall -q nosai
python3 -m pytest -q

mapfile -t tests < <(find . -name '*Tests.csproj' -type f -not -path './.git/*')
if [[ ${#tests[@]} -eq 0 ]]; then
  echo 'No .NET test projects found; skipping .NET tests.'
else
  for project in "${tests[@]}"; do
    dotnet test "$project" --configuration Release
  done
fi

echo '=== Tests completed successfully ==='
