@echo off
setlocal EnableExtensions

set "PROJECT=%~dp0SoulPlayer.csproj"
set "SPT_ROOT=%~1"

if not defined SPT_ROOT set /p "SPT_ROOT=Enter the full path to your SPT 4.1.3 folder: "

if not exist "%SPT_ROOT%\SPT_Runtime\SPT.Server.exe" (
  echo.
  echo ERROR: SPT_Runtime\SPT.Server.exe was not found under:
  echo %SPT_ROOT%
  echo.
  pause
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: The .NET SDK was not found.
  echo Install the .NET SDK or build SoulPlayer.sln with Visual Studio 2022.
  pause
  exit /b 1
)

dotnet build "%PROJECT%" -c Release -p:SptRoot="%SPT_ROOT%"
if errorlevel 1 (
  echo.
  echo BUILD FAILED. No files were installed into SPT.
  pause
  exit /b 1
)

echo.
echo Build completed successfully:
echo %~dp0bin\Release\Soulplayer.dll
echo.
echo This script builds only. It does not modify your SPT installation.
pause
