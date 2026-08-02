@echo off
setlocal
set "PROJECT_DIR=%~dp0..\src\Game.Client"

pushd "%PROJECT_DIR%" || exit /b 1

echo [Project Horizon] Removing stale Godot C# build cache...
if exist ".godot\mono\temp" rmdir /s /q ".godot\mono\temp"

echo [Project Horizon] Running a clean Debug build...
dotnet build "Game.Client.csproj" -c Debug -p:GodotTargetPlatform=windows
set "BUILD_EXIT=%ERRORLEVEL%"

popd
exit /b %BUILD_EXIT%
