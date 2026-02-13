@echo off
setlocal
set SCRIPT_DIR=%~dp0
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Install-FileRoutingAgentDemo.ps1"
if errorlevel 1 (
  echo.
  echo Installation failed. Press any key to close.
  pause >nul
  exit /b 1
)
exit /b 0
