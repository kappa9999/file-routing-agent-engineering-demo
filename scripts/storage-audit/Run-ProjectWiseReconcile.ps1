param(
    [string]$PDriveRoot = "P:\1000_Software",
    [string]$CompareRoot = "C:\Users\akiswani\Documents\SoftwareFolderCompare",
    [string]$CutoffDate = "2025-04-01",
    [string]$OutputFolder = ""
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Run-ProjectWiseReconcile] $Message"
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

if ([string]::IsNullOrWhiteSpace($CompareRoot) -or -not (Test-Path -LiteralPath $CompareRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($CompareRoot)) {
        Write-Host ""
        Write-Host "Default compare folder not found: $CompareRoot"
    }

    $CompareRoot = Read-Host "Enter the local ProjectWise folder copy path"
}

if ([string]::IsNullOrWhiteSpace($CompareRoot) -or -not (Test-Path -LiteralPath $CompareRoot)) {
    throw "The local ProjectWise folder copy was not found."
}

$arguments = @(
    "reconcile-pw",
    "--p-root", $PDriveRoot,
    "--compare-root", $CompareRoot,
    "--cutoff-date", $CutoffDate
)
if (-not [string]::IsNullOrWhiteSpace($OutputFolder)) {
    $arguments += @("--output-folder", $OutputFolder)
}

Write-Step "P drive root: $PDriveRoot"
Write-Step "Compare root: $CompareRoot"
Write-Step "Starting ProjectWise reconcile..."
& $exePath @arguments
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "ProjectWise reconcile failed with exit code $exitCode."
    exit $exitCode
}

$folderToOpen = $OutputFolder
if ([string]::IsNullOrWhiteSpace($folderToOpen)) {
    $folderToOpen = Get-ChildItem -Path (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "FileStorageAudit") -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "PwReconcile_*" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

if (-not [string]::IsNullOrWhiteSpace($folderToOpen) -and (Test-Path -LiteralPath $folderToOpen)) {
    Start-Process explorer.exe $folderToOpen
}
