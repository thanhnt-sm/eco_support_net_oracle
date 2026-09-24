@echo off
echo Building DataGuard Extensions...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-extensions.ps1" %*
set "buildExit=%ERRORLEVEL%"
echo.
if "%CI%"=="" if not "%NONINTERACTIVE%"=="1" (
    pause
)
exit /b %buildExit%
