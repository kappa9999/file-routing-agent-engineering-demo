# Remote Setup Quick Start (Simple Steps)

Use this when you have:
- `Machine A`: your main/dev machine (where this repo exists).
- `Machine B`: the test machine on the firm network.

Goal:
1. Install and validate the demo app on Machine B remotely.
2. Let engineers run setup/tests on Machine B.
3. Pull logs/support files back to Machine A with one command.

## Before You Start
1. Make sure both machines are on the same network.
2. Make sure you know a local admin username/password on Machine B.
3. Build the demo bundle on Machine A:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-DemoBundle.ps1
```

## Step 1 (Machine B, one-time): Enable remote access
1. Copy `remote-scripts` folder to Machine B.
2. Right-click `01-Enable-RemoteAccess.cmd` and choose **Run as administrator**.
3. Note the printed **Machine Name** (example: `ENG-LAPTOP-07`).
4. If you see a warning about `LocalAccountTokenFilterPolicy`, continue anyway (this is non-blocking in hardened environments).

## Step 2 (Machine A): Remote install + validation
1. Open `remote-scripts` on Machine A.
2. Double-click `02-Install-And-Validate-Remote.cmd`.
3. Enter Machine B name/IP when prompted.
4. Enter credentials when prompted.
5. Wait for `Remote install completed`.

## Step 3 (Machine B): Run real workflow test
1. Open `File Routing Agent` from desktop/start menu.
2. Run **Easy Setup Wizard (Recommended)**.
3. Set real project folders.
4. Perform test saves (PDF/CAD) as planned.
5. In tray menu, click **Export Support Bundle**.

## Step 4 (Machine A): Pull support/log package
1. Double-click `03-Collect-RemoteSupport.cmd`.
2. Enter Machine B name/IP when prompted.
2. The zip is saved under:
- `artifacts\remote-support\RemoteCapture_<MachineName>_<timestamp>.zip`

Send that zip for troubleshooting.

## If Connection Fails
1. Re-run Step 1 on Machine B as Administrator.
2. Confirm firewall/AV policy allows WinRM.
3. Try using IP instead of machine name:
```powershell
-ComputerName 10.0.0.25
```
4. If required by IT, use domain credentials:
```powershell
-UserName "DOMAIN\your.user"
```
