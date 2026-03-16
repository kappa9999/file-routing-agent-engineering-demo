# Folder Compare Tool v0.1.0

## Summary
First standalone release of the Folder Compare Tool for engineering teams.

## Included
- simple Windows desktop GUI for comparing any two folders,
- browse buttons for Primary Folder, Reference Folder, and optional Output Folder,
- cutoff date picker,
- saved settings between runs,
- read-only compare engine using file name, exact size, and modified date tolerance,
- Excel/CSV/JSON review package output,
- direct use case support for comparing `P:\1000_Software` against a local ProjectWise folder copy.

## Output Package
- `folder-compare-report.xlsx`
- `missing-from-reference.csv`
- `changed-after-cutoff.csv`
- `cleanup-review.csv`
- `ambiguous-matches.csv`
- `scan-issues.json`
- `run-manifest.json`
- `reconcile.log`

## Notes
- This tool never deletes, moves, archives, or modifies files.
- Output is blocked inside either compared folder.
- Default example paths are included for the software-folder ProjectWise comparison workflow.
