param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Build-FolderCompareBundle] $Message"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$publishDir = Join-Path $OutputRoot "folder-compare-publish-$Runtime"
$bundleDir = Join-Path $OutputRoot "FolderCompareTool-$Runtime"
$zipPath = Join-Path $OutputRoot "FolderCompareTool-$Runtime.zip"

Write-Step "Repo root: $repoRoot"
Write-Step "Output root: $OutputRoot"

New-Item -Path $OutputRoot -ItemType Directory -Force | Out-Null

if (-not $SkipTests) {
    Write-Step "Running tests..."
    dotnet test (Join-Path $repoRoot "FileRoutingAgent.slnx") --configuration $Configuration
}

Write-Step "Publishing Folder Compare tool..."
dotnet publish (Join-Path $repoRoot "FolderCompare.App\FolderCompare.App.csproj") `
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
Copy-Item -Path (Join-Path $repoRoot "docs\FOLDER_COMPARE_TOOL_GUIDE.md") -Destination (Join-Path $docsDir "FOLDER_COMPARE_TOOL_GUIDE.md") -Force

Copy-Item -Path (Join-Path $repoRoot "installer\FOLDER_COMPARE_BUNDLE_README.txt") -Destination (Join-Path $bundleDir "README.txt") -Force

$launcherPath = Join-Path $bundleDir "Launch-FolderCompareTool.cmd"
@'
@echo off
setlocal
start "" "%~dp0app\FolderCompare.App.exe"
'@ | Set-Content -Path $launcherPath -Encoding ASCII

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Step "Creating zip package..."
Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force

Write-Step "Done."
Write-Host "Bundle folder: $bundleDir"
Write-Host "Bundle zip:    $zipPath"
