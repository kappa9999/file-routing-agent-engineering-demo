param(
    [Parameter(Mandatory = $true)]
    [string]$ComputerName,
    [Parameter(Mandatory = $true)]
    [string]$RootPath,
    [string]$UserName = "",
    [string]$LocalBundleZip = "",
    [string]$RemoteWorkingFolder = "C:\Temp\StorageAudit",
    [string]$OutputFolder = "",
    [switch]$AddTrustedHost
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RemoteStorageAuditRun] $Message"
}

function Resolve-BundleZip {
    param([string]$InputPath)

    if (-not [string]::IsNullOrWhiteSpace($InputPath)) {
        return (Resolve-Path -LiteralPath $InputPath).Path
    }

    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $candidatePaths = @(
        (Join-Path $repoRoot "artifacts\StorageAuditTool-win-x64.zip"),
        (Join-Path $repoRoot "artifacts\StorageAuditBundle-win-x64.zip")
    )

    foreach ($candidate in $candidatePaths) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Bundle zip not found in artifacts. Run scripts\Build-StorageAuditBundle.ps1 first or pass -LocalBundleZip."
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

$bundleZip = Resolve-BundleZip -InputPath $LocalBundleZip

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
    $remoteZip = Join-Path $RemoteWorkingFolder "StorageAuditTool-win-x64.zip"
    $remoteExtracted = Join-Path $RemoteWorkingFolder "bundle"

    Invoke-Command -Session $session -ScriptBlock {
        param($folder, $extract)
        New-Item -Path $folder -ItemType Directory -Force | Out-Null
        if (Test-Path -LiteralPath $extract) {
            Remove-Item -Path $extract -Recurse -Force
        }
    } -ArgumentList $RemoteWorkingFolder, $remoteExtracted

    Copy-Item -Path $bundleZip -Destination $remoteZip -ToSession $session -Force

    $result = Invoke-Command -Session $session -ScriptBlock {
        param($zipPath, $extractPath, $rootPath, $outputFolder)

        Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force
        $exe = Join-Path $extractPath "app\StorageAudit.Tool.exe"
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "StorageAudit.Tool.exe not found in extracted bundle."
        }

        $arguments = @("--root", $rootPath)
        if (-not [string]::IsNullOrWhiteSpace($outputFolder)) {
            $arguments += @("--output-folder", $outputFolder)
        }

        & $exe @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Remote storage audit failed with exit code $LASTEXITCODE"
        }

        $resolvedOutputFolder = if (-not [string]::IsNullOrWhiteSpace($outputFolder)) {
            $outputFolder
        } else {
            (Get-ChildItem -Path (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "FileStorageAudit") -Directory |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 1).FullName
        }

        [PSCustomObject]@{
            MachineName = $env:COMPUTERNAME
            OutputFolder = $resolvedOutputFolder
        }
    } -ArgumentList $remoteZip, $remoteExtracted, $RootPath, $OutputFolder

    Write-Host ""
    Write-Host "Remote storage audit completed."
    Write-Host "Machine:      $($result.MachineName)"
    Write-Host "OutputFolder: $($result.OutputFolder)"
    Write-Host ""
}
finally {
    if ($null -ne $session) {
        Remove-PSSession -Session $session
    }
}
