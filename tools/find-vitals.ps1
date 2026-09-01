<#
.SYNOPSIS
    Finds where the NosTale client keeps the character's vitals.

.DESCRIPTION
    Must be run from an ELEVATED console. NostaleLauncher.exe self-elevates by
    manifest and the client inherits that, so a normal session is refused
    PROCESS_VM_READ on it however the game is started.

    An address is identified by elimination, not by one scan. Searching for a
    number like 7305 returns every unrelated integer that happens to equal it;
    what distinguishes the real one is that it tracks the value as it changes.
    So this asks you to take damage between passes, and keeps narrowing until a
    single address has survived several independent changes.

    Read-only throughout. Nothing is written to the game process.

.EXAMPLE
    .\tools\find-vitals.ps1 -Hp 7305
#>
[CmdletBinding()]
param(
    # Current HP, exactly as the client shows it.
    [Parameter(Mandatory = $true)][int] $Hp,

    # Process id of the client. Found automatically when omitted.
    [int] $ProcessId = 0,

    # Build configuration of the runtime to drive.
    [ValidateSet('Release', 'Debug')][string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repo "src/NosAi.Runtime/bin/$Configuration/net8.0-windows/NosAi.Runtime.dll"

if (-not (Test-Path $dll)) {
    Write-Error "Runtime not built at $dll. Run: dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c $Configuration"
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevated = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated) {
    Write-Warning "This console is NOT elevated. The client will refuse PROCESS_VM_READ."
    Write-Warning "Close this, open Terminal/PowerShell as administrator, and run it again."
}

if ($ProcessId -eq 0) {
    # The client with a window is the game; the other NostaleClientX processes
    # are its children and hold no character state.
    $candidates = Get-Process NostaleClientX -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle } | Sort-Object WorkingSet64 -Descending
    if (-not $candidates) { Write-Error "No NostaleClientX process with a window. Is the client in game?" }
    $ProcessId = $candidates[0].Id
    Write-Host "Client: PID $ProcessId ($([int]($candidates[0].WorkingSet64/1MB)) MB)" -ForegroundColor Cyan
}

function Invoke-Probe {
    param([string[]] $ProbeArgs)
    Push-Location $repo
    try { & dotnet $dll @ProbeArgs 2>&1 | ForEach-Object { $_ } }
    finally { Pop-Location }
}

Write-Host ""
Write-Host "Pass 1: scanning for $Hp" -ForegroundColor Cyan
$output = Invoke-Probe @('--memory-scan', "$ProcessId", "$Hp")
$output | ForEach-Object { Write-Host "  $_" }
if ($output -match 'Cannot read process') {
    Write-Host ""
    Write-Error "Refused. If this console is elevated and it still fails, the client is protected rather than merely elevated."
}

$pass = 1
while ($true) {
    $pass++
    Write-Host ""
    Write-Host "Now change the value in game -- take some damage, or use a potion." -ForegroundColor Yellow
    $next = Read-Host "New HP shown in the client (blank to stop)"
    if ([string]::IsNullOrWhiteSpace($next)) { break }
    if ($next -notmatch '^\d+$') { Write-Host "  Not a number; try again." -ForegroundColor Red; $pass--; continue }

    Write-Host "Pass ${pass}: narrowing against $next" -ForegroundColor Cyan
    $output = Invoke-Probe @('--memory-narrow', "$ProcessId", "$next")
    $output | ForEach-Object { Write-Host "  $_" }

    $addresses = @($output | Select-String -Pattern '^\s+0x([0-9A-Fa-f]+)$' |
        ForEach-Object { $_.Matches[0].Groups[1].Value })

    if ($addresses.Count -eq 1 -and $pass -ge 3) {
        $hpAddress = $addresses[0]
        Write-Host ""
        Write-Host "HP is at 0x$hpAddress, after $pass passes." -ForegroundColor Green
        Write-Host "The rest of the vitals struct:" -ForegroundColor Cyan
        Invoke-Probe @('--memory-dump', "$ProcessId", $hpAddress, '96') | ForEach-Object { Write-Host "  $_" }
        Write-Host ""
        Write-Host "Look for max HP and MP in that list, and note their offsets from +000." -ForegroundColor Yellow
        break
    }

    if ($addresses.Count -eq 0) {
        Write-Host ""
        Write-Host "Nothing survived. The HP entered probably did not match what was in memory" -ForegroundColor Red
        Write-Host "at that moment. Start again with the value the client shows right now." -ForegroundColor Red
        break
    }
}
