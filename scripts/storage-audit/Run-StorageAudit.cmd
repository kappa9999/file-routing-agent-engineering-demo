@echo off
set SCRIPT_DIR=%~dp0
set TARGET=%SCRIPT_DIR%Run-StorageAudit.ps1
if exist "%SCRIPT_DIR%local-scripts\Run-StorageAudit.ps1" set TARGET=%SCRIPT_DIR%local-scripts\Run-StorageAudit.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File "%TARGET%"
if errorlevel 1 pause
