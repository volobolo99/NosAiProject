$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root
$project = Join-Path $root 'src\NosAi.ControlPanel\NosAi.ControlPanel.csproj'
$exe = Join-Path $root 'src\NosAi.ControlPanel\bin\Release\net8.0-windows\NosAi.ControlPanel.exe'

if (-not (Test-Path $exe)) {
    Write-Host 'Compilo il Control Panel…'
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build fallita (uscita $LASTEXITCODE). L'eseguibile non è stato avviato."
        exit $LASTEXITCODE
    }
}

if (-not (Test-Path $exe)) {
    Write-Error "Build riuscita ma l'eseguibile non c'è: $exe"
    exit 1
}

Start-Process $exe
