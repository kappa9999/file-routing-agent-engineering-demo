param(
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

$labRoot = Join-Path $env:TEMP "FileRoutingAgentLab"
if (Test-Path $labRoot) {
    Remove-Item $labRoot -Recurse -Force
}
New-Item -Path $labRoot -ItemType Directory | Out-Null

$projectRoot = Join-Path $labRoot "Project123"
$workingCad = Join-Path $projectRoot "60_CAD\_Working"
$workingDesign = Join-Path $projectRoot "70_Design\_Working"
$publishedCad = Join-Path $projectRoot "60_CAD\Published"
$progressPrints = Join-Path $projectRoot "70_Design\10_ProgressPrints"
New-Item -Path $workingCad -ItemType Directory -Force | Out-Null
New-Item -Path $workingDesign -ItemType Directory -Force | Out-Null
New-Item -Path $publishedCad -ItemType Directory -Force | Out-Null
New-Item -Path $progressPrints -ItemType Directory -Force | Out-Null

$policyTemplate = Join-Path $WorkspaceRoot "FileRoutingAgent.App\Config\firm-policy.json"
$policyLocal = Join-Path $labRoot "firm-policy.local.json"
$signatureLocal = Join-Path $labRoot "firm-policy.local.json.sig"

$jsonRoot = $projectRoot.Replace("\", "\\")
$policyText = Get-Content -Path $policyTemplate -Raw
$policyText = $policyText.Replace("P:\\Project123", $jsonRoot)
$policyText = $policyText.Replace("firm-policy.json.sig", "firm-policy.local.json.sig")
Set-Content -Path $policyLocal -Value $policyText -NoNewline

$hash = (Get-FileHash -Path $policyLocal -Algorithm SHA256).Hash
Set-Content -Path $signatureLocal -Value $hash -NoNewline

$dbPath = Join-Path $labRoot "state.db"
$prefsPath = Join-Path $labRoot "user-preferences.json"

$env:FILEROUTINGAGENT_AGENTRUNTIME__POLICYPATH = $policyLocal
$env:FILEROUTINGAGENT_AGENTRUNTIME__DATABASEPATH = $dbPath
$env:FILEROUTINGAGENT_AGENTRUNTIME__USERPREFERENCESPATH = $prefsPath
$env:FILEROUTINGAGENT_AUTOMATIONPROMPT__ENABLED = "true"
$env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTACTION = "move"
$env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTPDFCATEGORY = "progress_print"

$appProject = Join-Path $WorkspaceRoot "FileRoutingAgent.App"
$process = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --project `"$appProject`" --no-build" `
    -PassThru `
    -WindowStyle Minimized `
    -WorkingDirectory $WorkspaceRoot

try {
    Start-Sleep -Seconds 8

    $sourceFile = Join-Path $workingDesign "smoke-progress.pdf"
    Set-Content -Path $sourceFile -Value "smoke $(Get-Date -Format o)"
    Start-Sleep -Seconds 2
    Add-Content -Path $sourceFile -Value "touch $(Get-Date -Format o)"

    Start-Sleep -Seconds 35

    $destinationFile = Join-Path $progressPrints "smoke-progress.pdf"
    $moved = (Test-Path $destinationFile) -and -not (Test-Path $sourceFile)

    Write-Host "Smoke Test Result: $moved"
    Write-Host "Source: $sourceFile"
    Write-Host "Destination: $destinationFile"
    Write-Host "Policy: $policyLocal"
    Write-Host "DB: $dbPath"

    if (-not $moved) {
        throw "Smoke test failed: file did not route to destination."
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }

    if (-not $KeepArtifacts) {
        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__POLICYPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__DATABASEPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__USERPREFERENCESPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__ENABLED -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTACTION -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTPDFCATEGORY -ErrorAction SilentlyContinue
    }
}
