param(
    [string]$InstallRoot = "$env:ProgramData\FileRoutingAgent\Connectors",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "ProjectWisePublish.ps1"
if (-not (Test-Path -LiteralPath $sourceScript)) {
    throw "Sample connector script not found at $sourceScript"
}

New-Item -Path $InstallRoot -ItemType Directory -Force | Out-Null
$destinationScript = Join-Path $InstallRoot "ProjectWisePublish.ps1"

if ((Test-Path -LiteralPath $destinationScript) -and -not $Force.IsPresent) {
    throw "Destination script already exists: $destinationScript. Re-run with -Force to overwrite."
}

Copy-Item -Path $sourceScript -Destination $destinationScript -Force

Write-Host "Installed sample ProjectWise connector script:"
Write-Host "  $destinationScript"
Write-Host ""
Write-Host "Optional quick validation:"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$destinationScript`" -ProjectId Demo -SourcePath C:\temp\a.pdf -DestinationPath C:\temp\b.pdf -DryRun"
