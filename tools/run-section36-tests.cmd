@echo off
setlocal EnableExtensions
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\tests\ProjectHorizon.Tests\ProjectHorizon.Tests.csproj"
set "RESULTS=%ROOT%\artifacts\test-results"
set "PROJECT_HORIZON_FULL_SOAK=0"
if /I "%~1"=="--full-soak" set "PROJECT_HORIZON_FULL_SOAK=1"

if exist "%RESULTS%" rmdir /s /q "%RESULTS%"
mkdir "%RESULTS%" >nul 2>&1

echo [Project Horizon] Section 36 test suite (full-soak=%PROJECT_HORIZON_FULL_SOAK%)...
dotnet test "%PROJECT%" -c Debug --collect:"XPlat Code Coverage" --settings "%ROOT%\tests\coverlet.runsettings" --results-directory "%RESULTS%" --logger "trx;LogFileName=section36.trx"
if errorlevel 1 exit /b %ERRORLEVEL%

where py >nul 2>&1
if not errorlevel 1 (
  py -3 "%ROOT%\tools\verify-section36-coverage.py" --results-dir "%RESULTS%"
  exit /b %ERRORLEVEL%
)
where python >nul 2>&1
if not errorlevel 1 (
  python "%ROOT%\tools\verify-section36-coverage.py" --results-dir "%RESULTS%"
  exit /b %ERRORLEVEL%
)

echo ERROR: Python 3 is required for section-36 coverage verification.
exit /b 1
