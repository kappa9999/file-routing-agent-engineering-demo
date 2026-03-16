# Folder Compare Tool Guide

Use this desktop tool when you need to compare any two folders and generate a clean review package.

Common example:
- Primary Folder: `P:\1000_Software`
- Reference Folder: `C:\Users\akiswani\Documents\SoftwareFolderCompare`

## What it produces
- `folder-compare-report.xlsx`
- `missing-from-reference.csv`
- `changed-after-cutoff.csv`
- `cleanup-review.csv`
- `ambiguous-matches.csv`
- `scan-issues.json`
- `run-manifest.json`
- `reconcile.log`

## What it does not do
- It does not delete files.
- It does not move files.
- It does not archive files.
- It does not modify either folder.

## Simple steps
1. Launch the tool.
2. Select the Primary Folder.
3. Select the Reference Folder.
4. Pick the cutoff date you want to use.
5. Leave Output Folder blank to use the default location, or choose a local output folder.
6. Click `Run Compare`.
7. Open the workbook and review:
   - `Missing From Reference`
   - `Changed After Cutoff`
   - `Cleanup Review`
   - `Ambiguous Matches`

## What the report means
- `Missing From Reference`: files in the Primary Folder that do not appear to exist in the Reference Folder.
- `Changed After Cutoff`: files in the Primary Folder that changed after the chosen cutoff date.
- `Cleanup Review`: files where an equivalent file appears to exist in the Reference Folder and the Primary copy was not changed after the cutoff date.
- `Ambiguous Matches`: files that need manual review because there are multiple possible matches or a weak-confidence match.
- `Scan Issues`: files or folders that could not be read during the compare.

## Match rule used
A file is treated as a match when:
- file name matches,
- file size matches exactly,
- last modified time is within plus/minus 2 days.

## Default output folder
`%USERPROFILE%\Documents\FileStorageAudit\PwReconcile_<timestamp>`

## Settings memory
The tool remembers the last folder paths, cutoff date, and output preference for the next run.
