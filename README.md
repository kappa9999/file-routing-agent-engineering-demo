# File Routing Agent For Engineering Teams

Windows tray-hosted file routing automation for structural/civil engineering outputs.  
Built for Windows 11 teams using shared SMB project folders and Bentley CAD workflows.

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

## Runtime Data Locations
- User preferences: `%LOCALAPPDATA%\FileRoutingAgent\user-preferences.json`
- SQLite state/audit: `%LOCALAPPDATA%\FileRoutingAgent\state.db`
- App logs: `%LOCALAPPDATA%\FileRoutingAgent\Logs\agent-*.log`

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
4. Save a PDF to a configured working folder.
5. Show prompt decision and routed destination.
6. Open Diagnostics and show connector publish activity row.
7. Show pending queue retry/dismiss flow.
8. Show audit DB location for traceability.
