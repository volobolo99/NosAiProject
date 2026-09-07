# WinDivert — installazione del driver e cattura di prova
# ============================================================================
# ESEGUIRE COME AMMINISTRATORE.  Tasto destro > "Esegui con PowerShell" non basta:
# aprire PowerShell come amministratore e lanciare questo file, oppure il comando
# in fondo che si autoeleva.
#
# Cosa fa, e cosa NON fa:
#   - copia WinDivert.dll e WinDivert64.sys accanto all'exe del probe;
#   - avvia il probe, che alla prima apertura fa registrare il driver WinDivert
#     come servizio di sistema (questo richiede i privilegi di amministratore,
#     ed e' il motivo per cui questo passo lo esegui tu);
#   - cattura in SOLA LETTURA (modalita' SNIFF) il traffico verso il server di
#     gioco per qualche secondo, e stampa direzione e dimensioni.
#   - NON modifica, NON inietta, NON altera il flusso: il gioco continua normale.
#
# Rischio: catturare il traffico di un client di gioco e' esattamente cio' che
# anti-cheat e statistiche lato server cercano. Il rischio e' sul tuo account, e
# lo hai accettato (ADR-0014). Questo script non lo riduce e non lo nasconde.
# ============================================================================

$ErrorActionPreference = "Stop"

# --- autoelevazione: se non siamo amministratori, ci si rilancia con UAC -----
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Privilegi di amministratore necessari per registrare il driver."
    Write-Host "Comparira' il prompt UAC: approvare per continuare."
    # L'elenco si costruisce prima e si passa come un solo array: '@args' dentro
    # una lista separata da virgole non e' splatting, e' un errore di sintassi che
    # PowerShell segnala al *parse* del file. Cioe' lo script non partiva affatto,
    # nemmeno gia' elevato, perche' il parser rifiuta il file intero prima di
    # eseguirne una riga.
    $relaunch = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath
    ) + $args
    Start-Process powershell -Verb RunAs -ArgumentList $relaunch
    return
}

# --- parametri -------------------------------------------------------------
$root      = Split-Path -Parent $MyInvocation.MyCommand.Path
$driverSrc = Join-Path $root "WinDivert-2.2.2-A\x64"
$probeDir  = Join-Path $root "probe\bin\Release\net8.0-windows"
$probeExe  = Join-Path $probeDir "WinDivertProbe.exe"

$serverIp = if ($args.Count -ge 1) { $args[0] } else { "79.110.84.175" }
$port     = if ($args.Count -ge 2) { $args[1] } else { "4006" }
$seconds  = if ($args.Count -ge 3) { $args[2] } else { "15" }
# Passa un quarto argomento come nome file per REGISTRARE la sessione su .noscap,
# cosi' il traffico si puo' rigiocare e decodificare offline senza driver.
$recordArg = if ($args.Count -ge 4) { @("--record", $args[3]) } else { @() }

# --- verifiche -------------------------------------------------------------
foreach ($f in @((Join-Path $driverSrc "WinDivert.dll"),
                 (Join-Path $driverSrc "WinDivert64.sys"))) {
    if (-not (Test-Path $f)) { throw "File mancante: $f  (rieseguire il download)" }
}
if (-not (Test-Path $probeExe)) {
    throw "Probe non compilato. Eseguire: dotnet build tools/windivert/probe -c Release"
}

# La firma del driver deve essere valida: non installare un .sys manomesso.
$sig = Get-AuthenticodeSignature (Join-Path $driverSrc "WinDivert64.sys")
if ($sig.Status -ne "Valid") { throw "Firma driver non valida: $($sig.Status)" }
Write-Host "Firma driver: valida ($($sig.SignerCertificate.Subject.Split(',')[0]))"

# --- staging: dll + sys accanto all'exe (la dll cerca il .sys accanto a se') --
Copy-Item (Join-Path $driverSrc "WinDivert.dll")   $probeDir -Force
Copy-Item (Join-Path $driverSrc "WinDivert64.sys") $probeDir -Force
Write-Host "Binari copiati accanto al probe."
Write-Host ""

# --- cattura di prova (installa il driver alla prima apertura) --------------
Write-Host "Avvio cattura: server $serverIp porta $port per ${seconds}s"
Write-Host "----------------------------------------------------------------"
& $probeExe $serverIp $port $seconds @recordArg
$code = $LASTEXITCODE
Write-Host "----------------------------------------------------------------"

# --- stato del servizio driver dopo l'apertura ------------------------------
$svc = Get-Service -Name "WinDivert*" -ErrorAction SilentlyContinue
if ($svc) {
    Write-Host "Servizio driver: $($svc.Name) stato=$($svc.Status)"
    Write-Host "Il driver resta installato; le catture successive non richiedono questo passo."
} else {
    Write-Host "Servizio driver non registrato: rivedere l'output del probe qui sopra."
}

exit $code
