# Storage Audit Tool v0.1.0

## Summary
Initial standalone release of the read-only storage audit utility for reviewing large and aging files on `P:\` or a UNC project share.

## Highlights
- Streams file inventory into local SQLite instead of holding the full scan in memory.
- Exports a review workbook plus CSV and JSON outputs.
- Produces ranked largest-file, candidate-review, project-rollup, extension-summary, and scan-issues reports.
- Includes local run wrapper and remote collection/run helper scripts.

## Included Asset
- `StorageAuditTool-win-x64.zip`

## Notes
- This tool never deletes, archives, moves, or modifies files on the scanned share.
- For mapped-drive scans such as `P:\`, run it inside the signed-in office user session.
