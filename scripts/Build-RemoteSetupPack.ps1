param(
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Build-RemoteSetupPack] $Message"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$packDir = Join-Path $OutputRoot "RemoteSetupPack"
$zipPath = Join-Path $OutputRoot "RemoteSetupPack.zip"
$remoteScriptsDir = Join-Path $repoRoot "scripts\remote"

Write-Step "Repo root: $repoRoot"
Write-Step "Output root: $OutputRoot"

if (-not (Test-Path -LiteralPath $remoteScriptsDir)) {
    throw "Remote scripts folder not found at $remoteScriptsDir"
}

New-Item -Path $OutputRoot -ItemType Directory -Force | Out-Null
if (Test-Path -LiteralPath $packDir) {
    Remove-Item -Path $packDir -Recurse -Force
}

New-Item -Path $packDir -ItemType Directory -Force | Out-Null
Copy-Item -Path $remoteScriptsDir -Destination (Join-Path $packDir "remote-scripts") -Recurse -Force
Copy-Item -Path (Join-Path $repoRoot "docs\REMOTE_SETUP_QUICK_START.md") -Destination (Join-Path $packDir "REMOTE_SETUP_QUICK_START.md") -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Step "Creating zip..."
Compress-Archive -Path (Join-Path $packDir "*") -DestinationPath $zipPath -Force

Write-Step "Done."
Write-Host "Pack folder: $packDir"
Write-Host "Pack zip:    $zipPath"
