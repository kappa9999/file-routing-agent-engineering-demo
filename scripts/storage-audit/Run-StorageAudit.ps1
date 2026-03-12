param(
    [string]$RootPath = "P:\",
    [string]$OutputFolder = ""
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Run-StorageAudit] $Message"
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$candidates = @(
    (Join-Path (Resolve-Path (Join-Path $scriptDir "..")) "app\StorageAudit.Tool.exe"),
    (Join-Path (Resolve-Path (Join-Path $scriptDir "..\..")) "StorageAudit.Tool\bin\Release\net8.0\StorageAudit.Tool.exe"),
    (Join-Path (Resolve-Path (Join-Path $scriptDir "..\..")) "StorageAudit.Tool\bin\Debug\net8.0\StorageAudit.Tool.exe")
)

$exePath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($exePath)) {
    throw "StorageAudit.Tool.exe was not found. Build the tool first or run from the published bundle."
}

$arguments = @("--root", $RootPath)
if (-not [string]::IsNullOrWhiteSpace($OutputFolder)) {
    $arguments += @("--output-folder", $OutputFolder)
}

Write-Step "Starting storage audit..."
& $exePath @arguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "Storage audit failed with exit code $exitCode."
    exit $exitCode
}

if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $latestOutput = Get-ChildItem -Path (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "FileStorageAudit") -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $latestOutput) {
        Start-Process explorer.exe $latestOutput.FullName
    }
}
