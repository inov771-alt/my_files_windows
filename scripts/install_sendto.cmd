@echo off
rem Adds "Send To -> My Files" shortcut for the current user. No admin rights needed.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install_sendto.ps1" %*
echo.
pause
