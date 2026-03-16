# ProjectWise Reconcile Guide

Use this mode when you need to compare `P:\1000_Software` against a local copy of the ProjectWise Software folder and answer:

- what still exists on `P:` but is missing from the local ProjectWise copy,
- what looks like a cleanup candidate on `P:` because an equivalent file already exists in the local ProjectWise copy,
- what changed after the March 2025 migration cutoff and should be reviewed first.

## What this mode produces
- `folder-compare-report.xlsx`
- `missing-from-reference.csv`
- `changed-after-cutoff.csv`
- `cleanup-review.csv`
- `ambiguous-matches.csv`
- `scan-issues.json`
- `run-manifest.json`
- `reconcile.log`

## What it does not do
- It does not query `pw:\` directly.
- It does not copy files.
- It does not delete files.
- It does not archive files.
- It does not modify either source folder.

## Inputs required
1. The live `P:\1000_Software` folder.
2. A local copy of the ProjectWise Software folder.

Default compare folder on the office machine:

`C:\Users\akiswani\Documents\SoftwareFolderCompare`

## Simple workflow
1. Copy the ProjectWise Software folder locally on the office machine.
2. Double-click `Run-ProjectWiseReconcile.cmd`.
3. If `C:\Users\akiswani\Documents\SoftwareFolderCompare` exists, the script uses it automatically. If it does not exist, the script asks for the local compare folder path.
4. Wait for the compare run to finish.
5. Review the workbook:
   - `Missing From Reference`
   - `Changed After Cutoff`
   - `Cleanup Review`
   - `Ambiguous Matches`

## Default cutoff
- `2025-04-01`

That means files modified on or after April 1, 2025 are treated as changed after the March 2025 migration period.

## Report meaning
- `Missing From Reference`: files in the primary folder that do not appear to exist in the reference folder yet.
- `Changed After Cutoff`: files on `P:` changed after the migration cutoff; these need human review even if a ProjectWise match exists.
- `Cleanup Review`: files where an equivalent ProjectWise file appears to exist and the `P:` copy was not changed after cutoff.
- `Ambiguous Matches`: duplicates or weak-confidence matches that should not be treated as resolved automatically.
- `Scan Issues`: paths that could not be read while scanning either side.

## Matching rule in v1
A file is treated as matched when:
- filename matches,
- size matches exactly,
- last modified time is within plus/minus 2 days.

## Default output folder
`%USERPROFILE%\Documents\FileStorageAudit\PwReconcile_<timestamp>`
