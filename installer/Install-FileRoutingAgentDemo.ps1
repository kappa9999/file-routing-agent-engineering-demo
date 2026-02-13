param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\FileRoutingAgentDemo",
    [string]$LaunchAfterInstall = "true"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[FileRoutingAgent Installer] $Message"
}

function Copy-Directory {
    param(
        [string]$Source,
        [string]$Destination
    )

    New-Item -Path $Destination -ItemType Directory -Force | Out-Null
    & robocopy $Source $Destination /MIR /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    if ($code -gt 7) {
        throw "Copy failed ($code): $Source -> $Destination"
    }
}

function New-Shortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $TargetPath
    $shortcut.Save()
}

function Ensure-ConfigWritableForUsers {
    param([string]$ConfigRoot)

    if (-not (Test-Path -LiteralPath $ConfigRoot)) {
        return
    }

    try {
        & icacls $ConfigRoot /grant "BUILTIN\Users:(OI)(CI)M" /T | Out-Null
        Write-Step "Granted BUILTIN\\Users modify rights on config folder."
    }
    catch {
        Write-Step "Warning: could not update config ACLs. Easy Setup may require admin rights."
    }
}

function Update-PolicyConnectorPath {
    param(
        [string]$PolicyPath,
        [string]$ConnectorScriptPath
    )

    if (-not (Test-Path -LiteralPath $PolicyPath)) {
        return
    }

    $policy = Get-Content -Path $PolicyPath -Raw | ConvertFrom-Json
    if ($null -eq $policy.projects) {
        return
    }

    $escaped = $ConnectorScriptPath.Replace("\", "\\")
    foreach ($project in $policy.projects) {
        if ($null -eq $project.connector) {
            continue
        }

        if ($null -eq $project.connector.settings) {
            continue
        }

        $project.connector.provider = "projectwise_script"
        if ($project.connector.settings.arguments) {
            $project.connector.settings.arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$escaped`" -ProjectId `"{projectId}`" -SourcePath `"{sourcePath}`" -DestinationPath `"{destinationPath}`" -Action `"{action}`" -Category `"{category}`""
        }
        else {
            $project.connector.settings | Add-Member -NotePropertyName arguments -NotePropertyValue "-NoProfile -ExecutionPolicy Bypass -File `"$escaped`" -ProjectId `"{projectId}`" -SourcePath `"{sourcePath}`" -DestinationPath `"{destinationPath}`" -Action `"{action}`" -Category `"{category}`"" -Force
        }

        if (-not $project.connector.settings.command) {
            $project.connector.settings | Add-Member -NotePropertyName command -NotePropertyValue "powershell.exe" -Force
        }
        if (-not $project.connector.settings.timeoutSeconds) {
            $project.connector.settings | Add-Member -NotePropertyName timeoutSeconds -NotePropertyValue "120" -Force
        }
        if (-not $project.connector.settings.parseStdoutJson) {
            $project.connector.settings | Add-Member -NotePropertyName parseStdoutJson -NotePropertyValue "true" -Force
        }
    }

    $json = $policy | ConvertTo-Json -Depth 30
    Set-Content -Path $PolicyPath -Value $json -Encoding UTF8

    $sigPath = "$PolicyPath.sig"
    $hash = (Get-FileHash -Path $PolicyPath -Algorithm SHA256).Hash
    Set-Content -Path $sigPath -Value $hash -NoNewline -Encoding ASCII
}

try {
    $launch = -not ($LaunchAfterInstall.Trim().Equals("false", [StringComparison]::OrdinalIgnoreCase) -or
                    $LaunchAfterInstall.Trim().Equals("0", [StringComparison]::OrdinalIgnoreCase) -or
                    $LaunchAfterInstall.Trim().Equals("no", [StringComparison]::OrdinalIgnoreCase))

    $bundleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $sourceAppRoot = Join-Path $bundleRoot "app"
    $sourceDocsRoot = Join-Path $bundleRoot "docs"
    $sourceConnector = Join-Path $bundleRoot "connectors\ProjectWisePublish.ps1"

    if (-not (Test-Path -LiteralPath $sourceAppRoot)) {
        throw "Installer bundle is missing app files. Expected: $sourceAppRoot"
    }

    New-Item -Path $InstallRoot -ItemType Directory -Force | Out-Null
    $installAppRoot = Join-Path $InstallRoot "app"
    $installDocsRoot = Join-Path $InstallRoot "docs"

    Write-Step "Copying application files..."
    Copy-Directory -Source $sourceAppRoot -Destination $installAppRoot
    Ensure-ConfigWritableForUsers -ConfigRoot (Join-Path $installAppRoot "Config")

    if (Test-Path -LiteralPath $sourceDocsRoot) {
        Write-Step "Copying documentation..."
        Copy-Directory -Source $sourceDocsRoot -Destination $installDocsRoot
    }

    $connectorTarget = ""
    if (Test-Path -LiteralPath $sourceConnector) {
        try {
            $connectorRoot = "$env:ProgramData\FileRoutingAgent\Connectors"
            New-Item -Path $connectorRoot -ItemType Directory -Force | Out-Null
            $connectorTarget = Join-Path $connectorRoot "ProjectWisePublish.ps1"
            Copy-Item -Path $sourceConnector -Destination $connectorTarget -Force
            Write-Step "Installed connector script to ProgramData."
        }
        catch {
            $fallbackRoot = Join-Path $env:LOCALAPPDATA "FileRoutingAgent\Connectors"
            New-Item -Path $fallbackRoot -ItemType Directory -Force | Out-Null
            $connectorTarget = Join-Path $fallbackRoot "ProjectWisePublish.ps1"
            Copy-Item -Path $sourceConnector -Destination $connectorTarget -Force
            Write-Step "Installed connector script to LocalAppData fallback."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($connectorTarget)) {
        $policyPath = Join-Path $installAppRoot "Config\firm-policy.json"
        Update-PolicyConnectorPath -PolicyPath $policyPath -ConnectorScriptPath $connectorTarget
    }

    $appExe = Join-Path $installAppRoot "FileRoutingAgent.App.exe"
    if (-not (Test-Path -LiteralPath $appExe)) {
        throw "Installed app executable not found: $appExe"
    }

    Write-Step "Creating shortcuts..."
    $desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "File Routing Agent.lnk"
    New-Shortcut -ShortcutPath $desktopShortcut -TargetPath $appExe -WorkingDirectory $installAppRoot

    $startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\File Routing Agent"
    New-Item -Path $startMenuDir -ItemType Directory -Force | Out-Null
    New-Shortcut -ShortcutPath (Join-Path $startMenuDir "File Routing Agent.lnk") -TargetPath $appExe -WorkingDirectory $installAppRoot
    if (Test-Path -LiteralPath $installDocsRoot) {
        New-Shortcut -ShortcutPath (Join-Path $startMenuDir "Engineer User Guide.lnk") -TargetPath (Join-Path $installDocsRoot "ENGINEER_USER_GUIDE.md") -WorkingDirectory $installDocsRoot
    }

    Write-Step "Writing uninstall script..."
    $uninstallScript = @"
param()
`$ErrorActionPreference = "Stop"
`$root = "$InstallRoot"
`$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "File Routing Agent.lnk"
`$startMenuDir = Join-Path `$env:APPDATA "Microsoft\Windows\Start Menu\Programs\File Routing Agent"
if (Test-Path -LiteralPath `$desktopShortcut) { Remove-Item -Path `$desktopShortcut -Force }
if (Test-Path -LiteralPath `$startMenuDir) { Remove-Item -Path `$startMenuDir -Recurse -Force }
if (Test-Path -LiteralPath `$root) { Remove-Item -Path `$root -Recurse -Force }
Write-Host "File Routing Agent removed."
"@
    Set-Content -Path (Join-Path $InstallRoot "Uninstall-FileRoutingAgentDemo.ps1") -Value $uninstallScript -Encoding UTF8

    if ($launch) {
        Write-Step "Launching File Routing Agent..."
        Start-Process -FilePath $appExe -WorkingDirectory $installAppRoot
    }

    Write-Step "Install complete."
    Write-Host ""
    Write-Host "Installed to: $InstallRoot"
    Write-Host "Start Menu: File Routing Agent"
    Write-Host "Next step: open tray menu and run 'Easy Setup Wizard (Recommended)'."
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Installer failed: $($_.Exception.Message)"
    exit 1
}
