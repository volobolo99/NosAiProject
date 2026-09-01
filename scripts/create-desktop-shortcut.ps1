# Puts the bench one double-click away, and stays reproducible.
#
# The shortcut itself is two minutes of clicking, which is exactly why it belongs
# in a script: a step that lives only in someone's memory is a step that is wrong
# on the next machine, and this one carries a working directory that has to be
# right or the candidate file lands somewhere nobody looks.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'NosAi.cmd'
if (-not (Test-Path $target)) {
    throw "Non trovo ${target}: lo script va eseguito dentro il repository."
}

$desktop = [Environment]::GetFolderPath('Desktop')
$path = Join-Path $desktop 'NosAi - banco di prova.lnk'

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($path)
$link.TargetPath = $target

# The launcher resolves its own directory, but the shortcut states it too: a
# shortcut that runs from System32 writes data/mapid_candidates.txt there.
$link.WorkingDirectory = $root
$link.Description = 'NosAi - banco di prova (si aggiorna da solo a ogni avvio)'

# The icon is the built runtime when there is one. A missing icon is a blank
# square, not a broken shortcut, so its absence is not worth failing over.
$exe = Join-Path $root 'src\NosAi.Runtime\bin\Debug\net8.0-windows\NosAi.Runtime.exe'
if (Test-Path $exe) {
    $link.IconLocation = "$exe,0"
}

$link.Save()

Write-Host "Collegamento creato: $path"
Write-Host "Punta a: $target"
