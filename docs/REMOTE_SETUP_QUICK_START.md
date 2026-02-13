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
1. Copy the repo folder or just `scripts\remote\01-Enable-RemoteAccess.ps1` to Machine B.
2. Right-click PowerShell and choose **Run as administrator**.
3. Run:
```powershell
powershell -ExecutionPolicy Bypass -File .\01-Enable-RemoteAccess.ps1
```
4. Note the printed **Machine Name** (example: `ENG-LAPTOP-07`).

## Step 2 (Machine A): Remote install + validation
1. In PowerShell on Machine A, go to repo root.
2. Run:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\remote\02-Install-And-Validate-Remote.ps1 -ComputerName ENG-LAPTOP-07 -AddTrustedHost
```
3. Enter credentials when prompted.
4. Wait for `Remote install completed`.

## Step 3 (Machine B): Run real workflow test
1. Open `File Routing Agent` from desktop/start menu.
2. Run **Easy Setup Wizard (Recommended)**.
3. Set real project folders.
4. Perform test saves (PDF/CAD) as planned.
5. In tray menu, click **Export Support Bundle**.

## Step 4 (Machine A): Pull support/log package
1. Run:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\remote\03-Collect-RemoteSupport.ps1 -ComputerName ENG-LAPTOP-07 -AddTrustedHost
```
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
