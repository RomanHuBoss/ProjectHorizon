@echo off
setlocal EnableExtensions
set "ROOT=%~dp0.."
set "PROJECT=%ROOT%\tests\ProjectHorizon.Tests\ProjectHorizon.Tests.csproj"
set "RESULTS=%ROOT%\artifacts\test-results"
set /p PH_VERSION=<"%ROOT%\VERSION"

if exist "%RESULTS%" rmdir /s /q "%RESULTS%"
mkdir "%RESULTS%" >nul 2>&1

where py >nul 2>&1
if not errorlevel 1 (
  set "PY=py -3"
) else (
  set "PY=python"
)

%PY% -m pip install --disable-pip-version-check "jsonschema>=4.20,<5" || exit /b 1
%PY% "%ROOT%\tools\ci\verify-version.py" || exit /b 1
dotnet restore "%PROJECT%" || exit /b 1
dotnet build "%PROJECT%" -c Debug --no-restore -p:ContinuousIntegrationBuild=true -p:Version=%PH_VERSION% -warnaserror || exit /b 1
%PY% "%ROOT%\tools\validate-json-content.py" || exit /b 1
%PY% "%ROOT%\tools\validate-godot-text-resource-structure.py" || exit /b 1
%PY% "%ROOT%\tools\validate-localization-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-audio-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-developer-diagnostics-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-section36-testing-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-section37-build-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-section38-architecture-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-platform-architecture-contract.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task146-base-construction-closure.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task148-world-scene-coordinator.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task149-runtime-regression-closure.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task150-planet-environment.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task152-interplanetary-travel.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task154-multi-planet-surface-content.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task1541-runtime-acceptance-hotfix.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task156-planet-surface-terrain.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task158-planet-surface-streaming.py" || exit /b 1
%PY% "%ROOT%\tools\validate-task1581-runtime-acceptance-hotfix.py" || exit /b 1
dotnet test "%PROJECT%" -c Debug --no-build --no-restore --collect:"XPlat Code Coverage" --settings "%ROOT%\tests\coverlet.runsettings" --results-directory "%RESULTS%" --logger "trx;LogFileName=section36.trx" || exit /b 1
%PY% "%ROOT%\tools\verify-section36-coverage.py" --results-dir "%RESULTS%" || exit /b 1
dotnet test "%PROJECT%" -c Debug --no-build --no-restore --filter "FullyQualifiedName~ProjectHorizon.Tests.Persistence.PersistenceTests" || exit /b 1

echo TASK-140 LOCAL QUALITY PASS: warningsAsErrors=1; tests=1; json=1; migrations=1; coverage=1; architecture=1.
exit /b 0

