@echo off
rem Starts MacroMaster straight from the source code (no release build / installer).
rem Needs the .NET 10 SDK. Compiles only what changed, then launches the app.
cd /d "%~dp0"

rem A running copy locks the exe and makes the build fail, so close it first.
taskkill /im MacroMaster.exe /f >nul 2>&1

dotnet build MacroMaster.csproj -c Debug -nologo -v q
if errorlevel 1 (
    echo.
    echo Build failed - see the errors above.
    pause
    exit /b 1
)

start "" "%~dp0bin\Debug\net10.0-windows\MacroMaster.exe"
