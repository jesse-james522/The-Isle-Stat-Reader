@echo off
setlocal
set VERSION=%~1
if "%VERSION%"=="" set /p VERSION=Version (e.g. 1.1):
if "%VERSION%"=="" (echo ERROR: No version. & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_release.ps1" -Version "%VERSION%"
