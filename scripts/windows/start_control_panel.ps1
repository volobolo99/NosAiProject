$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Set-Location $root
$project = Join-Path $root 'src\NosAi.ControlPanel\NosAi.ControlPanel.csproj'
$exe = Join-Path $root 'src\NosAi.ControlPanel\bin\Release\net8.0-windows\NosAi.ControlPanel.exe'
if (-not (Test-Path $exe)) {
    Write-Host 'Compilo il Control Panel…'
    dotnet build $project -c Release
}
Start-Process $exe
