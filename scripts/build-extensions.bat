@echo off
echo Building DataGuard Extensions...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-extensions.ps1" %*
set "buildExit=%ERRORLEVEL%"
echo.
if "%CI%"=="" if not "%NONINTERACTIVE%"=="1" (
    rem Only pause if running in an interactive console session (not via cmd /c or subshell)
    echo "%cmdcmdline%" | findstr /i /c:"%~f0" >nul 2>&1
    if not errorlevel 1 pause
)
exit /b %buildExit%
