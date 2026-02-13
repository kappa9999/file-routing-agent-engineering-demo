@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%01-Enable-RemoteAccess.ps1"
echo.
echo Press any key to close.
pause >nul
endlocal
