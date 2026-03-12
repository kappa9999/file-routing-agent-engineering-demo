Storage Audit Tool Bundle

What this does:
- Scans a project share (default P:\) without modifying any files.
- Produces an Excel workbook, CSV files, JSON metadata, and a scan log.
- Writes all output to the local workstation, not the share.

How to run on the office machine:
1) Extract this zip.
2) Double-click Run-StorageAudit.cmd.
3) Wait for the scan to finish.
4) Review the output folder that opens automatically.

Default output location:
- %USERPROFILE%\Documents\FileStorageAudit\Audit_<timestamp>

If P:\ is not available:
- Run inside the signed-in engineer session where P:\ is mapped, or
- use the exe with: --root \\server\share

Important:
- This tool does not delete, move, archive, or modify anything on the scanned share.
