# File Routing Agent For Engineering Teams

Windows tray-hosted file routing automation for structural/civil engineering outputs.  
Built for Windows 11 teams using shared SMB project folders and Bentley CAD workflows.

## Non-Technical User Friendly
- No command line usage is required for normal setup or daily use.
- The tray app now includes an **Easy Setup Wizard** with simple form fields:
  - Project ID
  - Project Name
  - Project Root Folder
  - Review/Edit all key folders before applying (watch root, working folders, official destinations)
  - Optional checkboxes for recommended defaults
- The tray app also includes **Export Support Bundle**:
  - Creates one zip on Desktop with policy, preferences, audit DB, scan history, and app logs.
  - Share that zip for troubleshooting instead of manually describing issues.
- The wizard automatically configures:
  - Working roots
  - Official CAD/PDF destinations
  - Candidate and watch roots
  - Policy signature refresh
  - Live policy reload (no restart required)

## Why This Exists
- Engineers save CAD and PDF outputs into working folders, Desktop, Downloads, or temporary locations.
- Teams lose time confirming which print set or drawing is the latest.
- This agent detects those mis-saves, prompts users once at the right time, and routes files to official project destinations.

## What The Agent Does
- Watches configured roots for risky saves (`FileSystemWatcher` hint source).
- Reconciles with periodic scans (scanner is truth source for SMB reliability).
- Waits for file stability before prompting (min-age + quiet checks + lock-safe open).
- Prompts with simple actions: `Move`, `Copy`, `Publish Copy`, `Leave`, `Snooze`.
- Never silently overwrites destination files.
- Writes full audit trail to SQLite (`state.db`).

## MVP Feature Highlights
- PDF routing by configured output categories (`progress_print`, `exhibit`, `check_print`, `clean_set`).
- CAD publish workflow with `Publish Copy` default.
- Conflict handling with versioned keep-both flow.
- Agent-origin suppression to avoid self-trigger loops.
- Pending queue that survives restart.
- Policy integrity check (`firm-policy.json` + signature hash).
- Connector adapter boundary for future ProjectWise integration.

## Architecture At A Glance
- `FileRoutingAgent.App`: WPF tray UX, prompts, diagnostics, config editor.
- `FileRoutingAgent.Core`: domain contracts, config models, interfaces.
- `FileRoutingAgent.Infrastructure`: pipeline, watcher/scanner, routing, conflict, transfer, persistence.
- `FileRoutingAgent.Tests`: unit and integration tests for pipeline invariants.

## Repository Layout
- `FileRoutingAgent.App/Config/firm-policy.json`: admin policy file.
- `FileRoutingAgent.App/Config/firm-policy.json.sig`: integrity signature (SHA256 hash).
- `scripts/LocalSmokeTest.ps1`: local end-to-end routing smoke test.
- `scripts/ProjectWisePublish.ps1`: sample connector script for `projectwise_script` profile.
- `scripts/Install-ProjectWiseConnectorSample.ps1`: installs sample connector into `%ProgramData%`.

## Quick Start (Demo Machine)
### Recommended (No Terminal)
1. Download the demo bundle zip from GitHub releases.
2. Extract the zip.
3. Double-click `Install-FileRoutingAgentDemo.cmd`.
4. Launch the app from desktop or Start Menu shortcut.
5. Run `Easy Setup Wizard (Recommended)` from tray menu.
6. In the wizard, verify/edit each folder path before clicking `Apply Setup`.

### Developer / Build Path
1. Build and test:
```powershell
dotnet build FileRoutingAgent.slnx
dotnet test FileRoutingAgent.slnx --no-build
```
2. Run local smoke test (no production `P:\` required):
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\LocalSmokeTest.ps1
```
3. Start the tray app:
```powershell
dotnet run --project FileRoutingAgent.App
```
4. Use tray menu:
- `Review Pending Detections`
- `Run Reconciliation Scan Now`
- `Open Configuration`
- `Diagnostics`
- `Export Support Bundle`

### Build One-Click Demo Bundle
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-DemoBundle.ps1
```
Output zip:
- `artifacts/FileRoutingAgentDemoBundle-win-x64.zip`

## ProjectWise Command Connector Profile
The app supports a script/CLI connector profile so you can demonstrate publish metadata flow before deep ProjectWise API integration.

### Default Policy Profile (Included)
Each project can set:
- `connector.enabled`: `true` or `false`
- `connector.provider`: `projectwise_script` (handled by command-process adapter)
- `connector.settings.command`: executable (`powershell.exe`)
- `connector.settings.arguments`: tokenized template:
  - `{projectId}`
  - `{sourcePath}`
  - `{destinationPath}`
  - `{action}`
  - `{category}`
- `connector.settings.timeoutSeconds`
- `connector.settings.parseStdoutJson`

### Sample Script Installation
Install the sample script to the default policy path:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Install-ProjectWiseConnectorSample.ps1 -Force
```

### Script Quick Validation
```powershell
powershell -ExecutionPolicy Bypass -File "%ProgramData%\FileRoutingAgent\Connectors\ProjectWisePublish.ps1" `
  -ProjectId Project123 `
  -SourcePath C:\Temp\input.pdf `
  -DestinationPath C:\Temp\official\input.pdf `
  -Action Copy `
  -Category progress_print `
  -DryRun
```

Expected behavior:
- Outputs JSON on stdout.
- Writes connector log under `%ProgramData%\FileRoutingAgent\ConnectorLogs`.
- Writes queue/submission JSON under `%ProgramData%\FileRoutingAgent\ConnectorQueue`.

## Admin UX For Demonstration Meeting
- `Open Configuration` provides:
  - **Easy Setup Wizard (Recommended)** for non-programmers
  - folder-by-folder verification/editing before setup is applied
  - guided setup window for all key settings and required project paths
  - live JSON editor
  - validation panel
  - project template wizard
  - `Save + Sign + Reload` (no app restart required)
  - `Apply PW Cmd Profile (Unconfigured)` bulk action
- `Diagnostics` shows:
  - root availability
  - scan history
  - connector publish activity (`connector`, `status`, `success`, `externalTransactionId`, `error`)
- `Export Support Bundle` creates a support zip you can send after a setup/test run.

## Runtime Data Locations
- User preferences: `%LOCALAPPDATA%\FileRoutingAgent\user-preferences.json`
- SQLite state/audit: `%LOCALAPPDATA%\FileRoutingAgent\state.db`
- App logs: `%LOCALAPPDATA%\FileRoutingAgent\Logs\agent-*.log`
- Support bundle export: `%USERPROFILE%\Desktop\FileRoutingAgent_Support_*.zip`

## Policy Signature Refresh
When policy JSON changes:
```powershell
$hash=(Get-FileHash -Path "FileRoutingAgent.App/Config/firm-policy.json" -Algorithm SHA256).Hash
Set-Content -Path "FileRoutingAgent.App/Config/firm-policy.json.sig" -Value $hash -NoNewline
```

## Demonstration Checklist (Engineering Firm)
1. Install sample connector script.
2. Run local smoke test.
3. Start tray app.
4. Run `Easy Setup Wizard` from tray menu and apply project settings.
5. Save a PDF to a configured working folder.
6. Show prompt decision and routed destination.
7. Open Diagnostics and show connector publish activity row.
8. Show pending queue retry/dismiss flow.
9. Show audit DB location for traceability.
