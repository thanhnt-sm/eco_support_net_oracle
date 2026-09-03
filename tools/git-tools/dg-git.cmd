@echo off
setlocal
if exist "C:\Program Files\Git\bin\bash.exe" (
    "C:\Program Files\Git\bin\bash.exe" "%~dp0dg-git" %*
) else (
    bash "%~dp0dg-git" %*
)
