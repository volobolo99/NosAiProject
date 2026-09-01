# =============================================================================
# Verifica dell'avanzamento su docs/OBIETTIVO_CONTROLLO_GIOCO.md
# =============================================================================
#
# Risponde a una domanda sola: quali schede di docs/TASKS_CURSOR.md e
# docs/TASKS_CLAUDE.md hanno lasciato dietro di sé l'artefatto che avevano
# promesso.
#
# Quello che questo script PUO' dire:
#   - se un file, un simbolo o un test con il nome concordato esiste;
#   - se una stringa che doveva sparire è sparita;
#   - se la build passa e quanti test sono verdi.
#
# Quello che NON puo' dire, e non finge di poter dire:
#   - se il codice trovato fa la cosa giusta;
#   - se il personaggio si muove e combatte davvero.
#
# Per questo non stampa mai VERIFIED. Il modello di stato del progetto separa
# Present, Integrated, Done e Verified proprio qui, e ADR-0004 vuole la prova
# reale prima dell'ultimo gradino. La sezione finale elenca cio' che solo una
# sessione di gioco accesa puo' chiudere.
#
# Uso:
#   pwsh scripts/verifica-obiettivo.ps1            # solo controlli strutturali, veloce
#   pwsh scripts/verifica-obiettivo.ps1 -Test      # esegue anche build e test

[CmdletBinding()]
param(
    [switch] $Test,

    # Quale checkout esaminare. Per difetto quello che contiene questo script,
    # che e' quasi sempre quello giusto; si passa esplicitamente quando lo
    # script vive in un worktree e si vuole misurare il ramo di lavoro.
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'

$repo = if ($RepoRoot) { Resolve-Path $RepoRoot } else { Resolve-Path (Join-Path $PSScriptRoot '..') }
Push-Location $repo
try {

$script:Done = 0
$script:Open = 0

function Write-Head {
    param([string] $Text)
    Write-Host ''
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ('-' * $Text.Length) -ForegroundColor DarkGray
}

# Riporta l'esito di una scheda. $Ok true = artefatto presente.
function Report {
    param(
        [string] $Id,
        [string] $Title,
        [bool]   $Ok,
        [string] $Detail
    )

    if ($Ok) {
        $script:Done++
        Write-Host ('  [fatto]  {0,-6} {1}' -f $Id, $Title) -ForegroundColor Green
    }
    else {
        $script:Open++
        Write-Host ('  [aperto] {0,-6} {1}' -f $Id, $Title) -ForegroundColor Yellow
        if ($Detail) { Write-Host ('           manca: {0}' -f $Detail) -ForegroundColor DarkGray }
    }
}

# Vero quando il file esiste.
function Test-FilePresent {
    param([string] $Path)
    return (Test-Path -LiteralPath (Join-Path $repo $Path) -PathType Leaf)
}

# Vero quando il testo compare nel file. Un file assente e' un testo assente,
# non un errore: la scheda semplicemente non e' stata fatta.
function Test-TextIn {
    param([string] $Path, [string] $Pattern)
    $full = Join-Path $repo $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $false }
    return [bool] (Select-String -LiteralPath $full -Pattern $Pattern -SimpleMatch -Quiet)
}

# Vero quando il testo compare da qualche parte sotto una cartella.
function Test-TextUnder {
    param([string] $Root, [string] $Pattern, [string] $Include = '*.cs')
    $full = Join-Path $repo $Root
    if (-not (Test-Path -LiteralPath $full)) { return $false }
    $hits = Get-ChildItem -LiteralPath $full -Recurse -File -Include $Include -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern $Pattern -SimpleMatch -List
    return ($null -ne $hits -and $hits.Count -gt 0)
}

Write-Host ''
Write-Host '=== NosAiProject - avanzamento verso il controllo del personaggio ===' -ForegroundColor White
Write-Host ("    repository: {0}" -f $repo) -ForegroundColor DarkGray
Write-Host ("    ramo:       {0}" -f (git rev-parse --abbrev-ref HEAD)) -ForegroundColor DarkGray

# -----------------------------------------------------------------------------
Write-Head 'F0 - regole allineate'

$memoriaVietata = Test-TextIn '.cursorrules' 'must not hook into the target process memory'
$percezionePython = Test-TextIn '.cursorrules' 'tactical decision and simulation layers: Python'
Report 'F0-1' '.cursorrules allineato ad ADR-0014 e al C# reale' `
    (-not ($memoriaVietata -or $percezionePython)) `
    (@(
        if ($memoriaVietata)   { 'il divieto di lettura memoria e'' ancora scritto' }
        if ($percezionePython) { 'la percezione e'' ancora assegnata a Python' }
     ) -join '; ')

# -----------------------------------------------------------------------------
Write-Head 'F1 - percezione completa'

Report 'F1-1' 'EntitySighting ammette la salute ignota' `
    (Test-TextIn 'src/NosAi.Runtime/Perception/Network/GameTrafficObserver.cs' 'double? HpRatio') `
    'double? HpRatio in GameTrafficObserver.cs'

Report 'C2' 'mv pubblica la posizione senza la salute' `
    (Test-TextUnder 'tests' 'MoveReportsPositionWithoutHealth') `
    'test MoveReportsPositionWithoutHealth'

Report 'C7' 'stat porta gli MP massimi' `
    (Test-TextUnder 'tests' 'StatCarriesMaxMp') `
    'test StatCarriesMaxMp'

Report 'C6' 'cond legge la velocita'' del giocatore' `
    (Test-TextUnder 'tests' 'CondReadsSpeedForPlayerOnly') `
    'test CondReadsSpeedForPlayerOnly'

Report 'C5' 'sr legge lo slot skill pronto' `
    (Test-TextUnder 'tests' 'SkillReadyReportsSlot') `
    'test SkillReadyReportsSlot'

Report 'C8' 'lev legge livello ed esperienza' `
    (Test-TextUnder 'tests' 'LevRejectsXpAboveMax') `
    'test LevRejectsXpAboveMax'

Report 'C1' 'TargetFrameReader legge il riquadro bersaglio' `
    ((Test-FilePresent 'src/NosAi.Runtime/Perception/TargetFrameReader.cs') -and
     (Test-TextUnder 'tests' 'NoiseIsUnreadableNotAbsent')) `
    'TargetFrameReader.cs e il test NoiseIsUnreadableNotAbsent'

Report 'F1-8' 'HasTarget stabilito, con il suo ADR' `
    (Test-FilePresent 'docs/adr/ADR-0018-establishing-the-target.md') `
    'docs/adr/ADR-0018-establishing-the-target.md'

Report 'F1-10' 'posizione propria letta dalla memoria' `
    (Test-FilePresent 'src/NosAi.Runtime/LiveIntegration/MemoryGameplayProvider.cs') `
    'MemoryGameplayProvider.cs'

# -----------------------------------------------------------------------------
Write-Head 'F2 - azioni con bersagli veri'

# Controllo al contrario: la scheda e' chiusa quando i bersagli finti sono spariti.
$finti = @(@('TARGET_MOB_01', 'WAYPOINT_A', 'ITEM_POTION_HP') |
    Where-Object { Test-TextUnder 'src' $_ })
Report 'F2-1' 'i bersagli finti sono spariti dal planner' `
    ($finti.Count -eq 0) `
    ("ancora presenti in src/: {0}" -f ($finti -join ', '))

Report 'F2-3' 'coordinate di gioco -> pixel, calibrate' `
    (Test-FilePresent 'src/NosAi.Runtime/Perception/ScreenProjection.cs') `
    'ScreenProjection.cs'

Report 'C3' 'KeybindMap legge gli slot dell''operatore' `
    ((Test-FilePresent 'src/NosAi.Runtime/LowLevel/KeybindMap.cs') -and
     (Test-TextUnder 'tests' 'MissingFileIsRefusedNotEmpty')) `
    'KeybindMap.cs e il test MissingFileIsRefusedNotEmpty'

# -----------------------------------------------------------------------------
Write-Head 'F3 e F4 - esecuzione e verifica'

Report 'F3-1' 'InputActionEffector applica l''azione al client' `
    (Test-FilePresent 'src/NosAi.Runtime/Gate3/InputActionEffector.cs') `
    'InputActionEffector.cs'

Report 'C4' 'NetworkWorldStateObserver rilegge lo stato' `
    ((Test-FilePresent 'src/NosAi.Runtime/LiveIntegration/NetworkWorldStateObserver.cs') -and
     (Test-TextUnder 'tests' 'UnobservedProviderDoesNotBecomeZero')) `
    'NetworkWorldStateObserver.cs e il test UnobservedProviderDoesNotBecomeZero'

Report 'F4-1b' 'gli MP massimi sono pubblicati sullo snapshot' `
    (Test-TextIn 'src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs' 'maxMp') `
    'maxMp in GameplayObservation.ToWire()'

# -----------------------------------------------------------------------------
if ($Test) {
    Write-Head 'Build e test'

    # Mai NosAi.sln: fallisce sul packaging Android per un blocco file, e
    # quell'errore non riguarda nulla di questo elenco.
    dotnet build src/NosAi.Runtime/NosAi.Runtime.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host '  build del runtime: FALLITA' -ForegroundColor Red
    } else {
        Write-Host '  build del runtime: ok' -ForegroundColor Green
    }

    foreach ($proj in @('tests/NosAi.Runtime.Tests/NosAi.Runtime.Tests.csproj',
                        'tests/NosAi.ControlPanel.Tests/NosAi.ControlPanel.Tests.csproj')) {
        $out = dotnet test $proj -c Release --nologo 2>&1 | Out-String
        $line = ($out -split "`n" | Select-String -Pattern 'Superato!|Passed!|Non superati|Failed!' | Select-Object -Last 1)
        if ($LASTEXITCODE -eq 0) {
            Write-Host ("  {0}" -f $proj) -ForegroundColor Green
        } else {
            Write-Host ("  {0}  ROSSO" -f $proj) -ForegroundColor Red
        }
        if ($line) { Write-Host ("      {0}" -f $line.ToString().Trim()) -ForegroundColor DarkGray }
    }

    Write-Host ''
    Write-Host '  Baseline al 1 settembre 2026: 648 verdi nel runtime, 43 nel Control Panel.' -ForegroundColor DarkGray
    Write-Host '  Un numero piu'' alto significa che qualcosa e'' stato aggiunto; non che sia giusto.' -ForegroundColor DarkGray
}

# -----------------------------------------------------------------------------
Write-Head 'Riepilogo'

$totale = $script:Done + $script:Open
Write-Host ("  schede con l'artefatto presente: {0} su {1}" -f $script:Done, $totale)
if ($script:Open -gt 0) {
    Write-Host ("  schede ancora aperte:            {0}" -f $script:Open) -ForegroundColor Yellow
}

Write-Head 'Cio'' che questo script non puo'' dirti'

Write-Host @'
  I criteri A1-A8 di docs/OBIETTIVO_CONTROLLO_GIOCO.md non sono verificabili da
  qui. Nessuna ricerca di stringhe puo' dire se il personaggio si e' mosso.
  Vanno provati con il client acceso e registrati in docs/GATE1_CHECKLIST.md:

    A1  HP/MP propri LIVE su una sessione in corso, non su un replay  (T-05)
    A2  la posizione segue il personaggio e si ferma quando si ferma
    A3  i mob compaiono, si muovono e spariscono come sullo schermo
    A4  bersaglio selezionato -> true, deselezionato -> false, illeggibile -> UNKNOWN
    A5  MoveToPosition che termina Confirmed
    A6  UseBasicAttack che termina Confirmed, con il su a confermare il danno
    A7  UseConsumable che termina Confirmed, con l'HP che risale
    A8  ogni rifiuto nominato e distinto nel log

  Un ciclo Unverified non e' un successo, e non va segnato come tale: e' il
  difetto che Gate 3 ha gia' corretto una volta (docs/GATE3_PIPELINE.md).
'@ -ForegroundColor DarkGray

Write-Host ''

}
finally {
    Pop-Location
}
