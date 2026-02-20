# Demo Presentation Checklist (Live Project Safety)

Use this checklist before showing the File Routing Agent on a live project.

## 1) Prep
1. Start the app from the desktop shortcut.
2. Confirm tray status is `Monitoring On`.
3. Open tray menu -> `Easy Setup Wizard (Recommended)` and verify all paths.

## 2) Safety Setup
1. Click `Run Project Structure Check`.
2. Click `Build/Refresh Demo Mirror Now`.
3. Click `Demo Mode: Toggle On/Off`.
4. Confirm tray status shows `Status: Demo Mode (Mirror Only)`.

## 3) Validate Isolation
1. Create a test PDF in live project folder (outside `_FRA_Demo`):
   - Expected: no prompt.
2. Create a test PDF in mirror working folder:
   - `{ProjectRoot}\_FRA_Demo\60_CAD\...` or `{ProjectRoot}\_FRA_Demo\70_Design\...`
   - Expected: routing prompt appears.
3. Choose `Move` and verify destination is inside `_FRA_Demo`.

## 4) During Demo
1. Use tray -> `Diagnostics` to show demo mode state.
2. If needed, use tray -> `Review Pending Detections`.
3. Keep all test files inside `_FRA_Demo`.

## 5) After Demo
1. Export support bundle:
   - tray -> `Export Support Bundle`
2. Optional: turn demo mode off:
   - tray -> `Demo Mode: Toggle On/Off`
