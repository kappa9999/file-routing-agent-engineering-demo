param(
    [switch]$EnableLocalAdminRemoteToken = $true,
    [string]$AllowWinRmFrom = "Any"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[RemoteSetup] $Message"
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script as Administrator on the test machine."
    }
}

Assert-Administrator

Write-Step "Enabling PowerShell remoting (WinRM)..."
Enable-PSRemoting -Force -SkipNetworkProfileCheck | Out-Null
Set-Service -Name WinRM -StartupType Automatic
Start-Service -Name WinRM

if ($EnableLocalAdminRemoteToken) {
    Write-Step "Setting LocalAccountTokenFilterPolicy for local admin remoting..."
    try {
        New-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" -Force | Out-Null
        New-ItemProperty `
            -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" `
            -Name "LocalAccountTokenFilterPolicy" `
            -PropertyType DWord `
            -Value 1 `
            -Force | Out-Null
    }
    catch {
        Write-Warning "Could not set LocalAccountTokenFilterPolicy. Continuing."
        Write-Warning "Use domain credentials in Steps 2/3, or run this script in an elevated admin shell with policy permissions."
    }
}

Write-Step "Checking WinRM listener and localhost connectivity..."
$null = Test-WSMan -ComputerName "localhost"

Write-Step "Configuring WinRM firewall scope..."
try {
    $rules = Get-NetFirewallRule -DisplayGroup "Windows Remote Management" -ErrorAction Stop
    if ($null -eq $rules -or $rules.Count -eq 0) {
        Write-Warning "No WinRM firewall rules found by display group."
    }
    else {
        Set-NetFirewallRule -DisplayGroup "Windows Remote Management" -Enabled True -Profile Any -Direction Inbound -Action Allow -ErrorAction Stop | Out-Null
        Set-NetFirewallRule -DisplayGroup "Windows Remote Management" -RemoteAddress $AllowWinRmFrom -ErrorAction Stop | Out-Null
        Write-Step "WinRM firewall updated. RemoteAddress scope: $AllowWinRmFrom"
    }
}
catch {
    Write-Warning "Could not update WinRM firewall scope automatically: $($_.Exception.Message)"
    Write-Warning "If remote access fails, allow inbound TCP 5985 from Machine A."
}

$ipv4Addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "127.0.0.1" } |
    Select-Object -ExpandProperty IPAddress -Unique

Write-Host ""
Write-Host "Remote setup complete."
Write-Host "Machine Name: $env:COMPUTERNAME"
Write-Host "User Name:    $env:USERNAME"
Write-Host "IPv4:         $($ipv4Addresses -join ', ')"
Write-Host "WinRM Scope:  $AllowWinRmFrom"
Write-Host ""
Write-Host "Next step (from your main/dev machine):"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\remote\02-Install-And-Validate-Remote.ps1 -ComputerName $env:COMPUTERNAME -AddTrustedHost"
Write-Host ""
