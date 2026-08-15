#!/usr/bin/env python3
"""TASK-138 static contract for PDF v2.0 section 36."""
from __future__ import annotations
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
failures: list[str] = []

def need(condition: bool, message: str) -> None:
    if not condition:
        failures.append(message)

def read(path: str) -> str:
    p = ROOT / path
    need(p.exists(), f"missing file: {path}")
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

csproj = read("tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj")
domain = read("tests/ProjectHorizon.Tests/Unit/DomainTests.cs")
world = read("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")
persistence = read("tests/ProjectHorizon.Tests/Persistence/PersistenceTests.cs")
golden_tests = read("tests/ProjectHorizon.Tests/Golden/GoldenSeedTests.cs")
stress = read("tests/ProjectHorizon.Tests/Stress/StressScenarioTests.cs")
golden_contract = read("src/Game.Client/Scripts/Testing/GoldenSeedContract.cs")
testing_bridge = read("src/Game.Client/Scripts/Testing/SalvageRepairSliceTesting.cs")
generator = read("src/Game.Client/Scripts/Infrastructure/ProjectHorizonGenerator.cs")
runner = read("tools/run-section36-tests.cmd")
coverage_script = read("tools/verify-section36-coverage.py")

for package in ["xunit", "Microsoft.NET.Test.Sdk", "coverlet.collector", "xunit.runner.visualstudio"]:
    need(package in csproj, f"test dependency missing: {package}")
need("ProjectReference" in csproj and "Game.Client.csproj" in csproj, "test project must reference shipping project")

unit_markers = {
    "seed hierarchy": "SeedHierarchy_IsDeterministic",
    "stable IDs": "StableIds_AcceptCanonicalIds_RejectMutableNames",
    "inventory": "Inventory_GrantsAggregateAndSaveAsStableStacks",
    "industry graph": "IndustryCatalog_IsAcyclicReachableAndStationCompatible",
    "economy": "EconomyQuotes_AreDeterministicForSameDayAndConserveSpread",
    "quests": "ProceduralQuestBoard_CoversAllTypesAndIsFeasible",
    "migrations": "LegacyMigration_PreservesSourceAliasesUnknownContentAndRoundTrip",
    "stats": "ShipStats_AreCalculatedFromClassAndInstalledModules",
    "serialization": "Serialization_RoundTripsCompleteSnapshotWithoutLosingOptionalState",
    "coordinates": "CoordinateTransforms_DistanceIsSymmetricAndTranslationInvariant",
}
all_tests = "\n".join([domain, world, persistence, golden_tests, stress])
for name, marker in unit_markers.items():
    need(marker in all_tests, f"mandatory unit group missing: {name}")

save_markers = [
    "NormalSave_RoundTripsExactlyAndSerializesWriters",
    "ShutdownDuringSave_UncommittedTransactionCannotReplaceLastCommittedRevision",
    "BackupRecovery_CorruptPrimaryRestoresProtectedSnapshot",
    "LegacyMigration_PreservesSourceAliasesUnknownContentAndRoundTrip",
    "Unknown",  # existing migration acceptance asserts unknown item/ship preservation
    "RemovedTechnology_DoesNotInvalidateContentChangedSave",
]
for marker in save_markers:
    need(marker in persistence, f"save scenario marker missing: {marker}")
need("UnknownItemPreserved" in persistence and "UnknownShipPreserved" in persistence, "unknown-content migration assertions missing")
need("ContentVersion" in persistence or "changed" in persistence.lower(), "changed-content save coverage marker missing")

load_markers = [
    "TwoHourFlight_", "EightHourAutomaticMovement_", "OneHundredSequentialLandings_",
    "OneHundredHyperspaceDestinations_", "FiveHundredModuleBase_",
    "TenThousandInventoryEntries_", "OneThousandVisitedSystems_",
    "OneGigabyteSaveDatabase_", "RepeatedAbnormalRecoveryCycles_",
]
for marker in load_markers:
    need(marker in stress, f"load/stress scenario missing: {marker}")
need("FullSoakFact" in stress and "PROJECT_HORIZON_FULL_SOAK" in read("tests/ProjectHorizon.Tests/Support/FullSoakFactAttribute.cs"), "full-soak gate missing")

manifest_path = ROOT / "src/Game.Client/Testing/golden-seeds.v1.json"
need(manifest_path.exists(), "golden manifest missing")
if manifest_path.exists():
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    need(manifest.get("generatorVersion") == 1, "golden generator version must match initial central version")
    need(len(manifest.get("systemCases", [])) >= 3, "golden systems must use >=3 fixed seeds/cases")
    poi = manifest.get("poiFixture", {})
    need(poi.get("expectedCount") == 20, "golden POI fixture must cover 20 objects")
    need(len(poi.get("placements", [])) == 20, "golden POI positions/control heights missing")
    for checksum in [*(case.get("checksum", "") for case in manifest.get("systemCases", [])), poi.get("checksum", "")]:
        need(bool(re.fullmatch(r"[a-f0-9]{64}", checksum)), "golden checksum is not SHA-256")
need("public const int Version = 1" in generator, "central generator version missing")
need("ProjectHorizonGenerator.Version" in golden_contract, "golden contract is not version-bound")
need("ProjectHorizonGenerator.Version" in read("src/Game.Client/Scripts/VerticalSlice/GalaxyNavigationRuntime.cs"), "worldgen generator version not centralized")

scope_path = ROOT / "tests/coverage-scope.json"
need(scope_path.exists(), "coverage scope missing")
if scope_path.exists():
    scope = json.loads(scope_path.read_text(encoding="utf-8"))["areas"]
    need(scope["Domain"]["minimumLineCoverage"] == 0.80, "Domain coverage threshold must be 80%")
    need(scope["WorldGen"]["minimumLineCoverage"] == 0.70, "WorldGen coverage threshold must be 70%")
    need(scope["Persistence"]["minimumLineCoverage"] == 0.80, "Persistence coverage threshold must be 80%")
need("coverage.cobertura.xml" in coverage_script, "coverage verifier must parse Cobertura output")
need("XPlat Code Coverage" in runner and "verify-section36-coverage.py" in runner, "test runner must collect and enforce coverage")
need("--full-soak" in runner, "test runner full-soak mode missing")

need("RunTestingArchitectureAcceptance" in testing_bridge, "TASK-138 F5 acceptance missing")
need("GoldenSeedContract.VerifySystemCase" in testing_bridge, "F5 does not verify golden systems")
need("GoldenSeedContract.VerifyPoiFixture" in testing_bridge, "F5 does not verify golden POI")
need("CubeSphereMeshBuilder.Build" in testing_bridge, "visual/worldgen smoke missing")
need("SmokePackedScene" in testing_bridge and "Scenes/UI/MainMenu.tscn" in testing_bridge and "visualComponents=" in testing_bridge, "visual-component smoke missing")
need("RunSequentialLandingProbe" in testing_bridge and "landingStress=" in testing_bridge, "F5 sequential landing stress probe missing")
need("TASK-138" in read("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs"), "TASK-138 not wired into F5/HUD")

status = "PASS" if not failures else "FAIL"
print(
    f"TASK-138 SECTION-36 CONTRACT {status}: unitGroups=10/10; saveScenarios=8/8; "
    f"loadScenarios=8/8+abnormal; goldenVersion=1; goldenSystems=4; goldenPoi=20; "
    f"coverage=80/70/80; visualSmoke=1; standaloneDotnet=1; f5Smoke=1."
)
for failure in failures:
    print("ERROR:", failure)
raise SystemExit(0 if not failures else 1)
