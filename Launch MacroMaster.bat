@echo off
if exist "%~dp0MacroMaster.exe" (
    start "" "%~dp0MacroMaster.exe"
) else (
    start "" "%~dp0release\MacroMaster.exe"
)
exit
