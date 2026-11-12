@echo off
rem Removes "Send To -> My Files" shortcut of the current user.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall_sendto.ps1" %*
echo.
pause
