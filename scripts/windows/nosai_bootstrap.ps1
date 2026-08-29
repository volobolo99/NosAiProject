[CmdletBinding()]
param(
    [string]$VolumeLabel = "NOSAI-SSD",
    [int]$MinimumFreeGB = 20,
    [switch]$CreateLayout
)

$ErrorActionPreference = "Stop"

function Get-VolumeByLabel([string]$Label) {
    Get-Volume | Where-Object { $_.FileSystemLabel -eq $Label } | Select-Object -First 1
}

$volume = Get-VolumeByLabel -Label $VolumeLabel
if (-not $volume) {
    throw "Volume '$VolumeLabel' not found. Connect the dedicated NosAi SSD and try again. No formatting is performed by this script."
}

if ($volume.FileSystem -ne "NTFS") {
    throw "Volume '$VolumeLabel' uses '$($volume.FileSystem)'. NosAi Windows deployment requires NTFS. Reformatting is intentionally disabled."
}

if ($volume.DriveLetter -eq $null) {
    throw "Volume '$VolumeLabel' has no drive letter. Assign one in Windows Disk Management, then retry."
}

$root = "$($volume.DriveLetter):\"
$freeGB = [math]::Round($volume.SizeRemaining / 1GB, 1)
if ($freeGB -lt $MinimumFreeGB) {
    throw "Insufficient free space on '$VolumeLabel': $freeGB GiB available; $MinimumFreeGB GiB required."
}

$nosaiRoot = Join-Path $root "NosAi"
$required = @(
    "app", "runtime", "models", "data\db", "data\state", "data\evidence",
    "data\exports", "cache", "logs", "temp", "backups", "config", "tools"
)

if ($CreateLayout) {
    New-Item -ItemType Directory -Force -Path $nosaiRoot | Out-Null
    foreach ($relative in $required) {
        New-Item -ItemType Directory -Force -Path (Join-Path $nosaiRoot $relative) | Out-Null
    }
}

Write-Host "NosAi external storage validated."
Write-Host "Volume : $VolumeLabel"
Write-Host "Root   : $nosaiRoot"
Write-Host "FS     : $($volume.FileSystem)"
Write-Host "Free   : $freeGB GiB"
Write-Host ""
Write-Host "This bootstrap is non-destructive: it never formats, partitions, deletes, or moves user data."
Write-Host "Use -CreateLayout once to create the canonical NosAi directory structure."
