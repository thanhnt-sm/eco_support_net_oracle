@echo off
setlocal
call "%~dp0github_automator.cmd" %*
exit /b %ERRORLEVEL%
