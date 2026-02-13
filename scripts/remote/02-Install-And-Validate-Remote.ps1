param(
    [Parameter(Mandatory = $true)]
    [string]$ComputerName,
    [string]$UserName = "",
    [string]$LocalBundleZip = "",
    [string]$RemoteWorkingFolder = "C:\Temp\FileRoutingAgentDemo",
    [switch]$AddTrustedHost,
    [switch]$StartAppAfterInstall
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RemoteInstall] $Message"
}

function Resolve-BundleZip {
    param([string]$InputPath)

    if (-not [string]::IsNullOrWhiteSpace($InputPath)) {
        return (Resolve-Path -LiteralPath $InputPath).Path
    }

    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
    $defaultZip = Join-Path $repoRoot "artifacts\FileRoutingAgentDemoBundle-win-x64.zip"
    if (-not (Test-Path -LiteralPath $defaultZip)) {
        throw "Bundle zip not found at '$defaultZip'. Run scripts\Build-DemoBundle.ps1 first or pass -LocalBundleZip."
    }

    return (Resolve-Path -LiteralPath $defaultZip).Path
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
            Write-Step "TrustedHosts already contains '$HostName'."
            return
        }

        $updated = if ($hosts.Count -eq 0) { $HostName } else { ($hosts + $HostName) -join "," }
        Set-Item -Path $clientPath -Value $updated -Force
        Write-Step "Added '$HostName' to TrustedHosts."
    }
    catch {
        Write-Warning "Could not update TrustedHosts automatically. Continuing."
        Write-Warning "If remote session fails, run this script from an elevated PowerShell or use domain credentials."
    }
}

$bundleZip = Resolve-BundleZip -InputPath $LocalBundleZip
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
    $remoteZip = Join-Path $RemoteWorkingFolder "FileRoutingAgentDemoBundle-win-x64.zip"
    $remoteExtracted = Join-Path $RemoteWorkingFolder "bundle"

    Write-Step "Preparing remote workspace..."
    Invoke-Command -Session $session -ScriptBlock {
        param($folder, $extract)
        New-Item -Path $folder -ItemType Directory -Force | Out-Null
        if (Test-Path -LiteralPath $extract) {
            Remove-Item -Path $extract -Recurse -Force
        }
    } -ArgumentList $RemoteWorkingFolder, $remoteExtracted

    Write-Step "Copying bundle zip to remote machine..."
    Copy-Item -Path $bundleZip -Destination $remoteZip -ToSession $session -Force

    Write-Step "Installing app remotely..."
    $result = Invoke-Command -Session $session -ScriptBlock {
        param($zipPath, $extractPath, $launch)

        Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

        $installer = Join-Path $extractPath "Install-FileRoutingAgentDemo.ps1"
        if (-not (Test-Path -LiteralPath $installer)) {
            throw "Installer script not found at $installer"
        }

        & powershell -ExecutionPolicy Bypass -File $installer -LaunchAfterInstall:$launch

        $installRoot = Join-Path $env:LOCALAPPDATA "FileRoutingAgentDemo"
        $appExe = Join-Path $installRoot "FileRoutingAgent.App.exe"
        $policy = Join-Path $installRoot "Config\firm-policy.json"
        $signature = Join-Path $installRoot "Config\firm-policy.json.sig"

        [PSCustomObject]@{
            MachineName = $env:COMPUTERNAME
            UserName = $env:USERNAME
            InstallRoot = $installRoot
            AppExeExists = Test-Path -LiteralPath $appExe
            PolicyExists = Test-Path -LiteralPath $policy
            SignatureExists = Test-Path -LiteralPath $signature
            LogFolder = Join-Path $env:LOCALAPPDATA "FileRoutingAgent\Logs"
            TimestampUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
    } -ArgumentList $remoteZip, $remoteExtracted, $StartAppAfterInstall.IsPresent

    Write-Host ""
    Write-Host "Remote install completed:"
    $result | Format-List | Out-String | Write-Host
    Write-Host "Next on the test machine:"
    Write-Host "  1) Open File Routing Agent from desktop shortcut."
    Write-Host "  2) Run Easy Setup Wizard."
    Write-Host "  3) Perform your test saves."
    Write-Host "  4) Click tray -> Export Support Bundle."
    Write-Host ""
    Write-Host "Then run this from your main/dev machine:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\remote\03-Collect-RemoteSupport.ps1 -ComputerName $ComputerName -AddTrustedHost"
    Write-Host ""
}
finally {
    if ($null -ne $session) {
        Remove-PSSession -Session $session
    }
}
