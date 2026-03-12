# Engineering File Workflow Tools

Windows tools for structural and civil engineering teams working on shared SMB project folders, Bentley CAD workflows, and live project-review environments.

This repository currently ships two separate utilities:

## Included Tools
### 1. File Routing Agent
A Windows tray application that helps engineers route CAD and PDF outputs into the correct project locations.

Key capabilities:
- detects risky saves in working folders or other non-official locations,
- prompts with simple actions such as `Move`, `Copy`, `Publish Copy`, `Leave`, and `Snooze`,
- prevents silent overwrites,
- keeps an audit trail in SQLite,
- supports a safe `_FRA_Demo` mirror mode for presentations on live project shares.

### 2. Storage Audit Tool
A separate, read-only utility for scanning `P:\` or a UNC share and generating Excel/CSV/JSON reports for storage cleanup review.

Key capabilities:
- ranks the largest files by size,
- highlights older large-file review candidates,
- summarizes storage by project bucket and file extension,
- writes all output locally on the workstation,
- never deletes, archives, or modifies the scanned share.

## Quick Start
### File Routing Agent
1. Build the demo bundle:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-DemoBundle.ps1
```
2. Extract `artifacts\FileRoutingAgentDemoBundle-win-x64.zip`.
3. Double-click `Install-FileRoutingAgentDemo.cmd`.
4. Launch the app and run `Easy Setup Wizard (Recommended)`.

Main documentation:
- `docs/ENGINEER_USER_GUIDE.md`
- `docs/DEMO_SETUP_GUIDE.md`
- `docs/DEMO_PRESENTATION_CHECKLIST.md`

### Storage Audit Tool
1. Build the audit bundle:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-StorageAuditBundle.ps1
```
2. Extract `artifacts\StorageAuditBundle-win-x64.zip`.
3. On the office machine, while signed into the normal user session where `P:\` is mapped, double-click `Run-StorageAudit.cmd`.
4. Open the generated `storage-audit-report.xlsx` under:
   `%USERPROFILE%\Documents\FileStorageAudit\Audit_<timestamp>`

Main documentation:
- `docs/STORAGE_AUDIT_GUIDE.md`

## Remote Workflow
The repo includes PowerShell remoting helpers for two-machine workflows.

File Routing Agent remote scripts:
- `scripts\remote\01-Enable-RemoteAccess.ps1`
- `scripts\remote\02-Install-And-Validate-Remote.ps1`
- `scripts\remote\03-Collect-RemoteSupport.ps1`
- `scripts\remote\04-Run-RemoteSmokeTest.ps1`

Storage Audit remote scripts:
- `scripts\remote\05-Collect-RemoteStorageAudit.ps1`
- `scripts\remote\06-Run-RemoteStorageAudit.ps1`

Important constraint:
- mapped drives such as `P:\` are usually only visible in the signed-in user session,
- remote admin/WinRM sessions generally do not inherit that mapping,
- for `P:\` scans, run the Storage Audit Tool locally on the office machine,
- for fully remote execution, pass a UNC path with `--root`.

## Developer Build
Build everything:
```powershell
dotnet build FileRoutingAgent.slnx
```

Run all tests:
```powershell
dotnet test FileRoutingAgent.slnx --no-build
```

Run the tray app from source:
```powershell
dotnet run --project FileRoutingAgent.App
```

Run the storage audit tool from source:
```powershell
dotnet run --project StorageAudit.Tool -- --root P:\
```

Run the local routing smoke test:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\LocalSmokeTest.ps1
```

## Repository Layout
- `FileRoutingAgent.App/`: WPF tray UI, prompts, diagnostics, config editor
- `FileRoutingAgent.Core/`: shared contracts, config models, interfaces
- `FileRoutingAgent.Infrastructure/`: watcher/scanner pipeline, routing, transfer, persistence
- `StorageAudit.Tool/`: standalone storage-audit/report generator
- `FileRoutingAgent.Tests/`: unit and integration tests
- `scripts/`: bundle builders, smoke tests, remote helpers
- `docs/`: user-facing and operator-facing guides

## ProjectWise Connector Support
The File Routing Agent includes a script/CLI connector boundary so you can demonstrate publish metadata flow before deeper ProjectWise integration.

Relevant files:
- `scripts/ProjectWisePublish.ps1`
- `scripts/Install-ProjectWiseConnectorSample.ps1`

## Runtime Data Locations
File Routing Agent:
- user preferences: `%LOCALAPPDATA%\FileRoutingAgent\user-preferences.json`
- state/audit DB: `%LOCALAPPDATA%\FileRoutingAgent\state.db`
- logs: `%LOCALAPPDATA%\FileRoutingAgent\Logs\agent-*.log`
- support bundle export: `%USERPROFILE%\Desktop\FileRoutingAgent_Support_*.zip`

Storage Audit Tool:
- output folder: `%USERPROFILE%\Documents\FileStorageAudit\Audit_<timestamp>`
- output contents: workbook, CSV files, JSON metadata, scratch SQLite DB, and `scan.log`
