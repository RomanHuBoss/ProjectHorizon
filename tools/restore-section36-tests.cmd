@echo off
setlocal
set "ROOT=%~dp0.."
dotnet restore "%ROOT%\tests\ProjectHorizon.Tests\ProjectHorizon.Tests.csproj"
exit /b %ERRORLEVEL%
