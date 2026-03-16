@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-ProjectWiseReconcile.ps1"
if errorlevel 1 (
  echo.
  echo ProjectWise reconcile failed.
  pause
  exit /b %errorlevel%
)
pause
