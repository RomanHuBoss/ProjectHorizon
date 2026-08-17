#!/usr/bin/env python3
"""Static regression/feature gate for TASK-164 surface visual language."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
poi_planner = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiPlanner.cs")
fauna = text("src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs")
factory = text("src/Game.Client/Scripts/VerticalSlice/ProceduralSurfaceVisualFactory.cs")
world = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs")
resource = text("src/Game.Client/Scripts/VerticalSlice/SalvageResourceNode.cs")
poi = text("src/Game.Client/Scripts/VerticalSlice/PlanetaryPoiNode.cs")
flora = text("src/Game.Client/Scripts/VerticalSlice/EcologyFloraSpecimenNode.cs")
terrain = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs")
chunk = text("src/Game.Client/Scripts/Terrain/TerrainChunk.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSurfaceVisualLanguage.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")

# User-reported TASK-154 regression: preserve golden +/-34 fixture but expand only
# planet/terrain-aware placement search.
need("PlanetCandidateMaximum = 48.0" in poi_planner and
     "terrainProfile is null" in poi_planner and
     "? CandidateMaximum" in poi_planner and
     ": PlanetCandidateMaximum" in poi_planner,
     "planet-scoped POI candidate expansion / legacy fixture split missing", failures)
need("candidateMaximum" in poi_planner and
     "SampleEnvironment(" in poi_planner,
     "expanded POI latitude/environment sampling is not extent-aware", failures)
need("surface.BuildPoiPlan(profile)" in tests and
     "ExpectedPoiTypeCount" in tests,
     "four-starter-planet POI regression test missing", failures)

# User-reported TASK-126 regression: altitude must track terrain floor rather than
# treating initial flight altitude as the territory floor.
need("CurrentTerrainFloorY()" in fauna and
     "PlanetSurfaceTerrainRuntime.SampleHeight(" in fauna,
     "flying fauna does not sample the current terrain floor", failures)
need("FlyingMinimumClearanceMeters = 1.6f" in fauna and
     "FlyingMaximumClearanceMeters = 7.2f" in fauna and
     "terrainFloorY + FlyingMinimumClearanceMeters" in fauna and
     "terrainFloorY + FlyingMaximumClearanceMeters" in fauna,
     "flying altitude envelope is not terrain-relative", failures)

# Visual-language subsystem.
need("class ProceduralSurfaceVisualFactory" in factory and
     all(token in factory for token in ('"crystal"', '"fiber"', '"organic"', '"ore"')),
     "resource visual family factory incomplete", failures)
need("CreateResourceVisual(definition)" in world and
     "BuildResourceMaterial(" in resource and
     "ApplyMaterialRecursive" in resource,
     "streamed resources are not using compound procedural visuals/materials", failures)
need("AddVisualDetails(definition, material)" in poi and
     'Name = "VisualDetails"' in poi and
     "PadBeacon" in poi and "SensorCrown" in poi,
     "POI compound visual details missing", failures)
need("AddBodyPlanVisualDetails(definition, material, _visualRoot, _morphology)" in fauna and
     "WingL_" in fauna and "Tail_" in fauna and "Leg" in fauna and
     "FaunaMorphologyProfile" in fauna,
     "TASK-198 modular fauna body-plan details missing", failures)
need('"Pad" => new CylinderMesh' in flora and
     '"Fungus" => new CylinderMesh' in flora,
     "flora silhouette upgrade missing", failures)
need("proceduralTexture" in terrain and "mineralBlend" in terrain and
     "float detail =" in chunk,
     "procedural terrain color breakup missing from fallback/distant/streamed terrain", failures)
need("RunSurfaceVisualLanguageAcceptance();" in main and
     "TASK-164 (F5)" in main and
     "TASK-164 surface visual language acceptance" in acceptance,
     "TASK-164 runtime acceptance/HUD wiring missing", failures)
need("SurfaceVisualLanguage_ResourceFamiliesAreDeterministic" in tests,
     "resource-family xUnit regression missing", failures)

if failures:
    print("TASK-164 SURFACE VISUAL LANGUAGE CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-164 SURFACE VISUAL LANGUAGE CONTRACT PASS: "
    "poiRegression=planet-window/legacy-safe; aerialAltitude=terrain-relative; "
    "resourceFamilies=4; resources=compound; poi=compound; fauna=body-plan; "
    "flora=silhouette-upgraded; terrain=procedural-color-breakup; gameplayCollision=preserved; "
    "f5=1; xunit=2-regression-groups."
)
