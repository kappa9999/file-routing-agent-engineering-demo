param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Build-StorageAuditBundle] $Message"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$publishDir = Join-Path $OutputRoot "storage-audit-publish-$Runtime"
$bundleDir = Join-Path $OutputRoot "StorageAuditTool-$Runtime"
$zipPath = Join-Path $OutputRoot "StorageAuditTool-$Runtime.zip"

Write-Step "Repo root: $repoRoot"
Write-Step "Output root: $OutputRoot"

New-Item -Path $OutputRoot -ItemType Directory -Force | Out-Null

if (-not $SkipTests) {
    Write-Step "Running tests..."
    dotnet test (Join-Path $repoRoot "FileRoutingAgent.slnx") --configuration $Configuration
}

Write-Step "Publishing Storage Audit tool..."
dotnet publish (Join-Path $repoRoot "StorageAudit.Tool\StorageAudit.Tool.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    -o $publishDir

if (Test-Path -LiteralPath $bundleDir) {
    Remove-Item -Path $bundleDir -Recurse -Force
}

Write-Step "Assembling bundle folder..."
New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
Copy-Item -Path $publishDir -Destination (Join-Path $bundleDir "app") -Recurse -Force

$docsDir = Join-Path $bundleDir "docs"
New-Item -Path $docsDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot "docs\STORAGE_AUDIT_GUIDE.md") -Destination (Join-Path $docsDir "STORAGE_AUDIT_GUIDE.md") -Force
Copy-Item -Path (Join-Path $repoRoot "docs\REMOTE_SETUP_QUICK_START.md") -Destination (Join-Path $docsDir "REMOTE_SETUP_QUICK_START.md") -Force

$localScriptsDir = Join-Path $bundleDir "local-scripts"
New-Item -Path $localScriptsDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot "scripts\storage-audit\Run-StorageAudit.ps1") -Destination (Join-Path $localScriptsDir "Run-StorageAudit.ps1") -Force
Copy-Item -Path (Join-Path $repoRoot "scripts\storage-audit\Run-StorageAudit.cmd") -Destination (Join-Path $bundleDir "Run-StorageAudit.cmd") -Force

if (Test-Path -LiteralPath (Join-Path $repoRoot "scripts\remote")) {
    $remoteScriptsDir = Join-Path $bundleDir "remote-scripts"
    New-Item -Path $remoteScriptsDir -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $repoRoot "scripts\remote\05-Collect-RemoteStorageAudit.ps1") -Destination (Join-Path $remoteScriptsDir "05-Collect-RemoteStorageAudit.ps1") -Force
    Copy-Item -Path (Join-Path $repoRoot "scripts\remote\06-Run-RemoteStorageAudit.ps1") -Destination (Join-Path $remoteScriptsDir "06-Run-RemoteStorageAudit.ps1") -Force
}

Copy-Item -Path (Join-Path $repoRoot "installer\STORAGE_AUDIT_BUNDLE_README.txt") -Destination (Join-Path $bundleDir "README.txt") -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Step "Creating zip package..."
Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force

Write-Step "Done."
Write-Host "Bundle folder: $bundleDir"
Write-Host "Bundle zip:    $zipPath"
