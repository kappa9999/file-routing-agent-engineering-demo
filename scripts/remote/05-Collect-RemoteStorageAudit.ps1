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
    Write-Host "[RemoteStorageAuditCollect] $Message"
}

function Add-TrustedHostIfNeeded {
    param([string]$HostName)

    try {
        $clientPath = "WSMan:\localhost\Client\TrustedHosts"
        $current = (Get-Item -Path $clientPath -ErrorAction Stop).Value
        $hosts = @()
        if (-not [string]::IsNullOrWhiteSpace($current)) {
            $hosts = $current.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        }

        if ($hosts -contains "*" -or $hosts -contains $HostName) {
            return
        }

        $updated = if ($hosts.Count -eq 0) { $HostName } else { ($hosts + $HostName) -join "," }
        Set-Item -Path $clientPath -Value $updated -Force
    }
    catch {
        Write-Warning "Could not update TrustedHosts automatically."
    }
}

if ([string]::IsNullOrWhiteSpace($LocalOutputFolder)) {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $LocalOutputFolder = Join-Path $repoRoot "artifacts\remote-storage-audit"
}

if (-not (Test-Path -LiteralPath $LocalOutputFolder)) {
    New-Item -Path $LocalOutputFolder -ItemType Directory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($UserName)) {
    $UserName = "$ComputerName\Administrator"
}

if ($AddTrustedHost) {
    Add-TrustedHostIfNeeded -HostName $ComputerName
}

Write-Step "Collecting credentials for $UserName ..."
$credential = Get-Credential -UserName $UserName -Message "Enter password for remote machine $ComputerName"

Write-Step "Opening remote session..."
$session = New-PSSession -ComputerName $ComputerName -Credential $credential

try {
    $remote = Invoke-Command -Session $session -ScriptBlock {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $captureRoot = Join-Path $env:TEMP "StorageAuditRemoteCapture_$timestamp"
        $zipPath = Join-Path $env:TEMP "StorageAuditRemoteCapture_$timestamp.zip"

        if (Test-Path -LiteralPath $captureRoot) {
            Remove-Item -Path $captureRoot -Recurse -Force
        }
        New-Item -Path $captureRoot -ItemType Directory -Force | Out-Null

        $latestAudit = Get-ChildItem -Path "C:\Users" -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                $auditRoot = Join-Path $_.FullName "Documents\FileStorageAudit"
                if (Test-Path -LiteralPath $auditRoot) {
                    Get-ChildItem -Path $auditRoot -Directory -Filter "Audit_*" -ErrorAction SilentlyContinue
                }
            } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1

        if ($null -eq $latestAudit) {
            throw "No Storage Audit output folder was found under C:\Users\*\Documents\FileStorageAudit"
        }

        Copy-Item -Path $latestAudit.FullName -Destination (Join-Path $captureRoot $latestAudit.Name) -Recurse -Force
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -Path $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $captureRoot "*") -DestinationPath $zipPath -Force

        [PSCustomObject]@{
            ZipPath = $zipPath
            CaptureRoot = $captureRoot
            LatestAuditFolder = $latestAudit.FullName
        }
    }

    $timestampLocal = Get-Date -Format "yyyyMMdd_HHmmss"
    $localZip = Join-Path $LocalOutputFolder "StorageAudit_$ComputerName`_$timestampLocal.zip"
    Copy-Item -Path $remote.ZipPath -Destination $localZip -FromSession $session -Force

    Invoke-Command -Session $session -ScriptBlock {
        param($zipPath, $captureRoot)
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -Path $zipPath -Force
        }
        if (Test-Path -LiteralPath $captureRoot) {
            Remove-Item -Path $captureRoot -Recurse -Force
        }
    } -ArgumentList $remote.ZipPath, $remote.CaptureRoot

    Write-Host ""
    Write-Host "Remote storage audit capture complete."
    Write-Host "Audit folder: $($remote.LatestAuditFolder)"
    Write-Host "Saved to:     $localZip"
    Write-Host ""
}
finally {
    if ($null -ne $session) {
        Remove-PSSession -Session $session
    }
}
