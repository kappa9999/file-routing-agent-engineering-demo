param(
    [Parameter(Mandatory = $true)]
    [string]$ComputerName,
    [Parameter(Mandatory = $true)]
    [string]$UserName,
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [string]$AppExePath = "C:\ProgramData\FileRoutingAgentDemo\app\FileRoutingAgent.App.exe"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RemoteSmoke] $Message"
}

Write-Step "Building credential for $UserName ..."
$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$credential = New-Object System.Management.Automation.PSCredential($UserName, $securePassword)

Write-Step "Running remote smoke test on $ComputerName ..."
$result = Invoke-Command -ComputerName $ComputerName -Credential $credential -ScriptBlock {
    param($appExePath)
    $ErrorActionPreference = "Stop"

    if (-not (Test-Path -LiteralPath $appExePath)) {
        throw "App executable not found: $appExePath"
    }

    $labRoot = Join-Path $env:TEMP "FileRoutingAgentLabRemote"
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

    $policyTemplate = "C:\ProgramData\FileRoutingAgentDemo\app\Config\firm-policy.json"
    if (-not (Test-Path -LiteralPath $policyTemplate)) {
        throw "Policy template missing at $policyTemplate"
    }

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

    $process = Start-Process -FilePath $appExePath -PassThru -WindowStyle Minimized
    try {
        Start-Sleep -Seconds 8

        $sourceFile = Join-Path $workingDesign "remote-smoke-progress.pdf"
        Set-Content -Path $sourceFile -Value ("smoke " + (Get-Date -Format o))
        Start-Sleep -Seconds 2
        Add-Content -Path $sourceFile -Value ("touch " + (Get-Date -Format o))

        Start-Sleep -Seconds 35

        $destinationFile = Join-Path $progressPrints "remote-smoke-progress.pdf"
        $moved = (Test-Path $destinationFile) -and -not (Test-Path $sourceFile)

        [PSCustomObject]@{
            SmokeResult = $moved
            Source = $sourceFile
            Destination = $destinationFile
            Policy = $policyLocal
            Database = $dbPath
            Preferences = $prefsPath
            Error = if ($moved) { "" } else { "File did not route to destination." }
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
        }

        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__POLICYPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__DATABASEPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AGENTRUNTIME__USERPREFERENCESPATH -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__ENABLED -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTACTION -ErrorAction SilentlyContinue
        Remove-Item Env:FILEROUTINGAGENT_AUTOMATIONPROMPT__DEFAULTPDFCATEGORY -ErrorAction SilentlyContinue
    }
} -ArgumentList $AppExePath

$result | Format-List *

if (-not $result.SmokeResult) {
    throw "Remote smoke test failed: $($result.Error)"
}

Write-Step "Remote smoke test passed."
