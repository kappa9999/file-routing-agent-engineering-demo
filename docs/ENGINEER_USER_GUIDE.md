# Engineer User Guide (No Command Line Needed)

This guide is for civil/structural engineers using the File Routing Agent in daily work.

## What You Need To Know
- The app runs in the system tray (bottom-right corner near clock).
- You do not need to edit code or JSON.
- You do not need to use PowerShell or Command Prompt.

## First-Time Setup (2-3 Minutes)
1. Open the tray icon for `File Routing Agent`.
2. Click `Easy Setup Wizard (Recommended)`.
3. Enter:
   - Project ID (example: `Project123`)
   - Project Name
   - Project Root Folder (browse to your project folder)
4. Leave recommended checkboxes on unless your admin says otherwise.
5. Click `Apply Setup`.
6. You should see `Setup complete`.

## Daily Use
When you save a PDF/CAD file in a working or wrong folder:
1. A prompt appears.
2. Choose:
   - `Move` for PDFs in most cases
   - `Publish Copy` for CAD in most cases
   - `Snooze` if you are not ready yet
3. The agent routes the file to the official destination.

## If A File Already Exists
You will get a conflict prompt:
- `Keep Both (Versioned Copy)` (safest)
- `Overwrite Existing` (only if intentional)
- `Cancel`

The agent never silently overwrites files.

## Helpful Tray Menu Items
- `Review Pending Detections`
- `Run Reconciliation Scan Now`
- `Diagnostics`
- `Open Configuration`

## Common Questions
1. I clicked Ignore by mistake.  
Open `Review Pending Detections` and retry the item.

2. I do not see a prompt.  
Use `Run Reconciliation Scan Now` from tray menu.

3. I changed folders for my project.  
Run `Easy Setup Wizard` again and update Project Root.
