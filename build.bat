@echo off
setlocal EnableExtensions
title Grimlite-Li local build

rem ============================================================
rem  Local build for Grimlite-Li-attempted-fix (.NET Framework)
rem  Usage: build.bat [Configuration]   (default: Release)
rem  Place in: I:\VIbeSkua CGPT\Grimlite-Li-attempted-fix
rem ============================================================

rem Always build from the folder this script lives in
cd /d "%~dp0"

set "CONFIG=%~1"
if not defined CONFIG set "CONFIG=Release"
set "SLN=Project Grimlite.sln"

if not exist "%SLN%" goto :nosln

rem ---------- 1. Locate MSBuild (vswhere first, then PATH) ----------
set "MSBUILD="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD for /f "delims=" %%i in ('where msbuild 2^>nul') do if not defined MSBUILD set "MSBUILD=%%i"
if not defined MSBUILD goto :nomsbuild
echo Using MSBuild: %MSBUILD%

rem ---------- 2. Locate or download nuget.exe (packages.config restore) ----------
set "NUGET="
for /f "delims=" %%i in ('where nuget 2^>nul') do if not defined NUGET set "NUGET=%%i"
if not defined NUGET set "NUGET=%~dp0.nuget\nuget.exe"
if exist "%NUGET%" goto :restore
echo Downloading nuget.exe...
if not exist "%~dp0.nuget" mkdir "%~dp0.nuget"
powershell -NoProfile -ExecutionPolicy Bypass -Command "[Net.ServicePointManager]::SecurityProtocol='Tls12'; Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%NUGET%'"
if errorlevel 1 goto :nonuget

rem ---------- 3. Restore packages ----------
:restore
echo Restoring NuGet packages...
"%NUGET%" restore "%SLN%" -NonInteractive
if errorlevel 1 goto :fail

rem ---------- 4. Rebuild (same flags as the repo's build.ps1) ----------
echo Building %SLN% [%CONFIG%]...
"%MSBUILD%" "%SLN%" /t:Rebuild /p:Configuration=%CONFIG% /p:GenerateManifests=false /m /v:minimal
if errorlevel 1 goto :fail

echo.
echo BUILD SUCCEEDED. Output is under bin\%CONFIG%\
goto :end

:nosln
echo ERROR: "%SLN%" not found in %CD%
goto :fail
:nomsbuild
echo ERROR: MSBuild not found. Install Visual Studio Build Tools with the ".NET desktop build tools" workload.
goto :fail
:nonuget
echo ERROR: Could not download nuget.exe. Install it manually and add it to PATH.
goto :fail
:fail
echo.
echo BUILD FAILED.
pause
exit /b 1
:end
pause
exit /b 0