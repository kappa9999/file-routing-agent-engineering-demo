# Storage Audit Guide

This tool scans a project share such as `P:\` and creates a clean storage review package.

## What it produces
- `storage-audit-report.xlsx`
- `largest-files.csv`
- `project-rollups.csv`
- `candidate-review.csv`
- `extension-summary.csv`
- `scan-issues.json`
- `run-manifest.json`
- `scan.log`

## What it does not do
- It does not delete files.
- It does not archive files.
- It does not modify the scanned share.

## Recommended way to run
Run it on the office machine while signed into the normal engineer session, because that is where `P:\` is usually mapped.

## Simple steps
1. Extract the Storage Audit bundle.
2. Double-click `Run-StorageAudit.cmd`.
3. Wait for the scan to finish.
4. Open the generated Excel workbook and review:
- `Largest Files`
- `Candidate Review`
- `Project Rollups`

## Default output folder
`%USERPROFILE%\Documents\FileStorageAudit\Audit_<timestamp>`

## If `P:\` is not available
Use a UNC share path instead:

```powershell
StorageAudit.Tool.exe --root \\server\share
```

## Remote helpers
If the tool runs on the office machine and you want the results back on your main machine:

- Collect the latest audit package:
  - `scripts\remote\05-Collect-RemoteStorageAudit.ps1`
- Run the audit remotely against a UNC share:
  - `scripts\remote\06-Run-RemoteStorageAudit.ps1`

## Candidate review meaning
- `Review for Archive`
- `Review for Retention`
- `Needs Engineering Check`

These are review labels only. They are not delete instructions.
