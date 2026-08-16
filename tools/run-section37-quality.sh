#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
VERSION_VALUE="$(tr -d '\r\n' < VERSION)"
RESULTS="$ROOT/artifacts/test-results"
rm -rf "$RESULTS"
mkdir -p "$RESULTS"

python3 -m pip install --disable-pip-version-check 'jsonschema>=4.20,<5'
python3 tools/ci/verify-version.py
dotnet restore tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj
dotnet build tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj -c Debug --no-restore \
  -p:ContinuousIntegrationBuild=true -p:Version="$VERSION_VALUE" -warnaserror
python3 tools/validate-json-content.py
python3 tools/validate-godot-text-resource-structure.py
python3 tools/validate-localization-contract.py
python3 tools/validate-audio-contract.py
python3 tools/validate-developer-diagnostics-contract.py
python3 tools/validate-section36-testing-contract.py
python3 tools/validate-section37-build-contract.py
python3 tools/validate-section38-architecture-contract.py
python3 tools/validate-platform-architecture-contract.py
python3 tools/validate-task146-base-construction-closure.py
python3 tools/validate-task148-world-scene-coordinator.py
python3 tools/validate-task149-runtime-regression-closure.py
python3 tools/validate-task150-planet-environment.py
python3 tools/validate-task152-interplanetary-travel.py
python3 tools/validate-task154-multi-planet-surface-content.py
python3 tools/validate-task1541-runtime-acceptance-hotfix.py
python3 tools/validate-task156-planet-surface-terrain.py
python3 tools/validate-task158-planet-surface-streaming.py
python3 tools/validate-task1581-runtime-acceptance-hotfix.py
python3 tools/validate-task160-surface-world-composition.py
python3 tools/validate-task1601-aerial-acceptance-hotfix.py
python3 tools/validate-task162-planet-global-surface-frame.py
python3 tools/validate-task1621-runtime-bootstrap-hotfix.py
python3 tools/validate-task1622-surface-presentation-hotfix.py
python3 tools/validate-task164-surface-visual-language.py
python3 tools/validate-task166-planetary-weather.py
python3 tools/validate-task168-planetary-globe-geodesy.py
python3 tools/validate-task170-radial-surface-frame.py
python3 tools/validate-task172-physical-radial-surface.py
dotnet test tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj -c Debug --no-build --no-restore \
  --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings \
  --results-directory "$RESULTS" --logger "trx;LogFileName=section36.trx"
python3 tools/verify-section36-coverage.py --results-dir "$RESULTS"
dotnet test tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj -c Debug --no-build --no-restore \
  --filter "FullyQualifiedName~ProjectHorizon.Tests.Persistence.PersistenceTests"
echo "TASK-140 LOCAL QUALITY PASS: warningsAsErrors=1; tests=1; json=1; migrations=1; coverage=1; architecture=1."

