@echo off
setlocal
call dotnet --version >nul 2>&1
if errorlevel 1 if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    call "%USERPROFILE%\.dotnet\dotnet.exe" --version >nul 2>&1
    if not errorlevel 1 set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)
if exist "C:\Program Files\Git\bin\bash.exe" (
    "C:\Program Files\Git\bin\bash.exe" "%~dp0dg-git" %*
) else (
    bash "%~dp0dg-git" %*
)
