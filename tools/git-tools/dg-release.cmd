@echo off
setlocal
if exist "%USERPROFILE%\AppData\Local\Programs\Python\Python313\python.exe" (
    set "PATH=%USERPROFILE%\AppData\Local\Programs\Python\Python313;%PATH%"
)
if exist "C:\Program Files\Git\bin\bash.exe" (
    "C:\Program Files\Git\bin\bash.exe" "%~dp0dg-release" %*
) else (
    bash "%~dp0dg-release" %*
)
