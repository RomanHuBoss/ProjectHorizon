@echo off
setlocal EnableExtensions
set "ROOT=%~dp0.."
set "CLIENT_DIR=%ROOT%\src\Game.Client"
set "DOMAIN_DIR=%ROOT%\src\Game.Domain"
set "APPLICATION_DIR=%ROOT%\src\Game.Application"

rem TASK-146: TASK-144 moved these types out of Game.Client. Overlay extraction over
rem an older checkout can leave the historical .cs files behind; delete them so
rem Godot/MSBuild cannot shadow Game.Domain/Game.Application types with stale copies.
echo [Project Horizon] Removing legacy TASK-144 source copies left by overlay upgrades...
for %%F in (
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\DomainEvents.cs"
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\DomainEvents.cs.uid"
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\SystemFrequencyPolicy.cs"
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\SystemFrequencyPolicy.cs.uid"
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\DomainEventBus.cs"
  "%CLIENT_DIR%\Scripts\Infrastructure\Architecture\DomainEventBus.cs.uid"
  "%CLIENT_DIR%\Scripts\Infrastructure\ProjectHorizonGenerator.cs"
  "%CLIENT_DIR%\Scripts\Infrastructure\ProjectHorizonGenerator.cs.uid"
) do (
  if exist "%%~F" del /f /q "%%~F"
)
if exist "%CLIENT_DIR%\Scripts\Infrastructure\Architecture" rmdir "%CLIENT_DIR%\Scripts\Infrastructure\Architecture" >nul 2>&1

rem A clean acceptance build must force CoreCompile in all three production layers.
echo [Project Horizon] Removing stale Godot and .NET build outputs for all production layers...
if exist "%CLIENT_DIR%\.godot\mono\temp" rmdir /s /q "%CLIENT_DIR%\.godot\mono\temp"
if exist "%CLIENT_DIR%\bin" rmdir /s /q "%CLIENT_DIR%\bin"
if exist "%CLIENT_DIR%\obj" rmdir /s /q "%CLIENT_DIR%\obj"
if exist "%DOMAIN_DIR%\bin" rmdir /s /q "%DOMAIN_DIR%\bin"
if exist "%DOMAIN_DIR%\obj" rmdir /s /q "%DOMAIN_DIR%\obj"
if exist "%APPLICATION_DIR%\bin" rmdir /s /q "%APPLICATION_DIR%\bin"
if exist "%APPLICATION_DIR%\obj" rmdir /s /q "%APPLICATION_DIR%\obj"

pushd "%CLIENT_DIR%" || exit /b 1

echo [Project Horizon] Running a clean Debug build of Game.Domain, Game.Application and Game.Client...
dotnet build "Game.Client.csproj" -c Debug -p:GodotTargetPlatform=windows
set "BUILD_EXIT=%ERRORLEVEL%"

popd
if not "%BUILD_EXIT%"=="0" exit /b %BUILD_EXIT%
echo [Project Horizon] CLEAN BUILD PASS: all production layers compiled.
exit /b 0
