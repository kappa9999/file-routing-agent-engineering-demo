param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[Build-DemoBundle] $Message"
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "artifacts"
}

$publishDir = Join-Path $OutputRoot "publish-$Runtime"
$bundleDir = Join-Path $OutputRoot "FileRoutingAgentDemoBundle-$Runtime"
$zipPath = Join-Path $OutputRoot "FileRoutingAgentDemoBundle-$Runtime.zip"

Write-Step "Repo root: $repoRoot"
Write-Step "Output root: $OutputRoot"

New-Item -Path $OutputRoot -ItemType Directory -Force | Out-Null

if (-not $SkipTests) {
    Write-Step "Running tests..."
    dotnet test (Join-Path $repoRoot "FileRoutingAgent.slnx") --configuration $Configuration
}

Write-Step "Publishing WPF app..."
dotnet publish (Join-Path $repoRoot "FileRoutingAgent.App\FileRoutingAgent.App.csproj") `
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
Copy-Item -Path (Join-Path $repoRoot "docs\ENGINEER_USER_GUIDE.md") -Destination (Join-Path $docsDir "ENGINEER_USER_GUIDE.md") -Force
Copy-Item -Path (Join-Path $repoRoot "docs\DEMO_SETUP_GUIDE.md") -Destination (Join-Path $docsDir "DEMO_SETUP_GUIDE.md") -Force
Copy-Item -Path (Join-Path $repoRoot "docs\DEMO_PRESENTATION_CHECKLIST.md") -Destination (Join-Path $docsDir "DEMO_PRESENTATION_CHECKLIST.md") -Force
Copy-Item -Path (Join-Path $repoRoot "docs\REMOTE_SETUP_QUICK_START.md") -Destination (Join-Path $docsDir "REMOTE_SETUP_QUICK_START.md") -Force

$connectorsDir = Join-Path $bundleDir "connectors"
New-Item -Path $connectorsDir -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $repoRoot "scripts\ProjectWisePublish.ps1") -Destination (Join-Path $connectorsDir "ProjectWisePublish.ps1") -Force

if (Test-Path -LiteralPath (Join-Path $repoRoot "scripts\remote")) {
    Copy-Item -Path (Join-Path $repoRoot "scripts\remote") -Destination (Join-Path $bundleDir "remote-scripts") -Recurse -Force
}

Copy-Item -Path (Join-Path $repoRoot "installer\Install-FileRoutingAgentDemo.ps1") -Destination (Join-Path $bundleDir "Install-FileRoutingAgentDemo.ps1") -Force
Copy-Item -Path (Join-Path $repoRoot "installer\Install-FileRoutingAgentDemo.cmd") -Destination (Join-Path $bundleDir "Install-FileRoutingAgentDemo.cmd") -Force
Copy-Item -Path (Join-Path $repoRoot "installer\BUNDLE_README.txt") -Destination (Join-Path $bundleDir "README.txt") -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -Path $zipPath -Force
}

Write-Step "Creating zip package..."
Compress-Archive -Path (Join-Path $bundleDir "*") -DestinationPath $zipPath -Force

Write-Step "Done."
Write-Host "Bundle folder: $bundleDir"
Write-Host "Bundle zip:    $zipPath"
