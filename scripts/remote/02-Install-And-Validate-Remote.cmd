@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set /p COMPUTER_NAME=Enter remote machine name or IP: 
if "%COMPUTER_NAME%"=="" (
  echo Machine name/IP is required.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%02-Install-And-Validate-Remote.ps1" -ComputerName "%COMPUTER_NAME%" -AddTrustedHost
echo.
echo Press any key to close.
pause >nul
endlocal
