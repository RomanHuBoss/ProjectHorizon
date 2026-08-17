@echo off
setlocal
set "ROOT=%~dp0.."
if "%~1"=="" (
  echo Usage: tools\run-task214-endurance.cmd ^<path-to-Godot-4.7.1-mono.exe^>
  exit /b 2
)
set "GODOT=%~1"
if not exist "%GODOT%" (
  echo Godot executable not found: %GODOT%
  exit /b 2
)
echo TASK-214: launching uninterrupted 8-hour endurance certification.
"%GODOT%" --path "%ROOT%\src\Game.Client" -- --developer --endurance-soak=8
exit /b %ERRORLEVEL%
