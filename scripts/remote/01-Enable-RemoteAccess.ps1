param(
    [switch]$EnableLocalAdminRemoteToken = $true
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

$ipv4Addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "127.0.0.1" } |
    Select-Object -ExpandProperty IPAddress -Unique

Write-Host ""
Write-Host "Remote setup complete."
Write-Host "Machine Name: $env:COMPUTERNAME"
Write-Host "User Name:    $env:USERNAME"
Write-Host "IPv4:         $($ipv4Addresses -join ', ')"
Write-Host ""
Write-Host "Next step (from your main/dev machine):"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\remote\02-Install-And-Validate-Remote.ps1 -ComputerName $env:COMPUTERNAME -AddTrustedHost"
Write-Host ""
