param(
    [Parameter(Mandatory = $true)]
    [string]$ComputerName,
    [string]$UserName = "",
    [string]$LocalOutputFolder = "",
    [switch]$AddTrustedHost
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RemoteCollect] $Message"
}

function Add-TrustedHostIfNeeded {
    param([string]$HostName)

    $clientPath = "WSMan:\localhost\Client\TrustedHosts"
    $current = (Get-Item -Path $clientPath -ErrorAction Stop).Value
    $hosts = @()
    if (-not [string]::IsNullOrWhiteSpace($current)) {
        $hosts = $current.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    }

    if ($hosts -contains "*" -or $hosts -contains $HostName) {
        Write-Step "TrustedHosts already contains '$HostName'."
        return
    }

    $updated = if ($hosts.Count -eq 0) { $HostName } else { ($hosts + $HostName) -join "," }
    Set-Item -Path $clientPath -Value $updated -Force
    Write-Step "Added '$HostName' to TrustedHosts."
}

if ([string]::IsNullOrWhiteSpace($LocalOutputFolder)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $LocalOutputFolder = Join-Path $repoRoot "artifacts\remote-support"
}

if (-not (Test-Path -LiteralPath $LocalOutputFolder)) {
    New-Item -Path $LocalOutputFolder -ItemType Directory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($UserName)) {
    $UserName = "$ComputerName\Administrator"
}

if ($AddTrustedHost) {
    Write-Step "Adding remote host to TrustedHosts (client-side)..."
    Add-TrustedHostIfNeeded -HostName $ComputerName
}

Write-Step "Collecting credentials for $UserName ..."
$credential = Get-Credential -UserName $UserName -Message "Enter password for remote machine $ComputerName"

Write-Step "Opening remote session..."
$session = New-PSSession -ComputerName $ComputerName -Credential $credential

try {
    Write-Step "Collecting support artifacts on remote machine..."
    $remote = Invoke-Command -Session $session -ScriptBlock {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $captureRoot = Join-Path $env:TEMP "FileRoutingAgentRemoteCapture_$timestamp"
        $captureData = Join-Path $captureRoot "data"
        $summaryPath = Join-Path $captureRoot "summary.json"
        $zipPath = Join-Path $env:TEMP "FileRoutingAgentRemoteCapture_$timestamp.zip"

        if (Test-Path -LiteralPath $captureRoot) {
            Remove-Item -Path $captureRoot -Recurse -Force
        }
        New-Item -Path $captureData -ItemType Directory -Force | Out-Null

        $desktop = [Environment]::GetFolderPath("Desktop")
        $latestSupportBundle = Get-ChildItem -Path $desktop -Filter "FileRoutingAgent_Support_*.zip" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -ne $latestSupportBundle) {
            $bundleDir = Join-Path $captureData "support-bundle"
            New-Item -Path $bundleDir -ItemType Directory -Force | Out-Null
            Copy-Item -Path $latestSupportBundle.FullName -Destination (Join-Path $bundleDir $latestSupportBundle.Name) -Force
        }

        $appData = Join-Path $env:LOCALAPPDATA "FileRoutingAgent"
        $logsPath = Join-Path $appData "Logs"
        $dbPath = Join-Path $appData "state.db"
        $prefsPath = Join-Path $appData "user-preferences.json"

        if (Test-Path -LiteralPath $logsPath) {
            Copy-Item -Path $logsPath -Destination (Join-Path $captureData "Logs") -Recurse -Force
        }

        if (Test-Path -LiteralPath $dbPath) {
            $stateDir = Join-Path $captureData "state"
            New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
            Copy-Item -Path $dbPath -Destination (Join-Path $stateDir "state.db") -Force
            if (Test-Path -LiteralPath "$dbPath-wal") {
                Copy-Item -Path "$dbPath-wal" -Destination (Join-Path $stateDir "state.db-wal") -Force
            }
            if (Test-Path -LiteralPath "$dbPath-shm") {
                Copy-Item -Path "$dbPath-shm" -Destination (Join-Path $stateDir "state.db-shm") -Force
            }
        }

        if (Test-Path -LiteralPath $prefsPath) {
            $prefDir = Join-Path $captureData "preferences"
            New-Item -Path $prefDir -ItemType Directory -Force | Out-Null
            Copy-Item -Path $prefsPath -Destination (Join-Path $prefDir "user-preferences.json") -Force
        }

        $summary = [PSCustomObject]@{
            machineName = $env:COMPUTERNAME
            userName = $env:USERNAME
            capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
            latestSupportBundle = if ($null -ne $latestSupportBundle) { $latestSupportBundle.FullName } else { $null }
            localAppData = $env:LOCALAPPDATA
            includedLogs = Test-Path -LiteralPath $logsPath
            includedState = Test-Path -LiteralPath $dbPath
            includedPreferences = Test-Path -LiteralPath $prefsPath
        }
        $summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -NoNewline

        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -Path $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $captureData "*"), $summaryPath -DestinationPath $zipPath -Force

        [PSCustomObject]@{
            ZipPath = $zipPath
            SummaryPath = $summaryPath
        }
    }

    $timestampLocal = Get-Date -Format "yyyyMMdd_HHmmss"
    $localZip = Join-Path $LocalOutputFolder "RemoteCapture_$ComputerName`_$timestampLocal.zip"

    Write-Step "Copying capture zip to local machine..."
    Copy-Item -Path $remote.ZipPath -Destination $localZip -FromSession $session -Force

    Write-Step "Cleaning temporary remote capture files..."
    Invoke-Command -Session $session -ScriptBlock {
        param($remoteZipPath, $remoteSummaryPath)
        $remoteRoot = Split-Path -Path $remoteSummaryPath -Parent
        if (Test-Path -LiteralPath $remoteZipPath) {
            Remove-Item -Path $remoteZipPath -Force
        }
        if (Test-Path -LiteralPath $remoteRoot) {
            Remove-Item -Path $remoteRoot -Recurse -Force
        }
    } -ArgumentList $remote.ZipPath, $remote.SummaryPath

    Write-Host ""
    Write-Host "Remote support capture complete."
    Write-Host "Saved to: $localZip"
    Write-Host "Send this zip for troubleshooting."
    Write-Host ""
}
finally {
    if ($null -ne $session) {
        Remove-PSSession -Session $session
    }
}
