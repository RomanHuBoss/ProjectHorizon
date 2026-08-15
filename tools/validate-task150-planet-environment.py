#!/usr/bin/env python3
"""Static contract gate for TASK-150 multi-planet environment subsystem."""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)


failures: list[str] = []
content = json.loads(text("src/Game.Client/Content/planet_environments.json"))
ecology = json.loads(text("src/Game.Client/Content/ecology.json"))
galaxy = text("src/Game.Client/Scripts/VerticalSlice/GalaxyNavigationRuntime.cs")
catalog = text("src/Game.Client/Scripts/VerticalSlice/PlanetEnvironmentCatalog.cs")
runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetEnvironmentRuntime.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/PlanetEnvironmentAcceptance.cs")
slice_main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
slice_env = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetEnvironment.cs")
slice_galaxy = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs")
preview = text("src/Game.Client/Scripts/Planet/CubeSpherePrototypeEnvironment.cs")
workbench = text("src/Game.Client/Scripts/Developer/DeveloperWorkbenchController.cs")
models = text("src/Game.Client/Scripts/Persistence/SaveGameModels.cs")
save_database = text("src/Game.Client/Scripts/Persistence/SaveDatabase.cs")
planet_map = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetMap.cs")
audio = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAudio.cs")
survival = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlayerSurvival.cs")
atmosphere_shader = text("src/Game.Client/Shaders/planet_atmosphere_shell.gdshader")
tests = text("tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs")

archetypes = content.get("Archetypes", [])
expected = {
    "temperate", "desert", "frozen", "volcanic", "toxic",
    "radioactive", "barren", "oceanic", "gas_giant",
}
actual = {item.get("Archetype") for item in archetypes}
biomes = {item.get("BiomeId") for item in ecology.get("Biomes", [])}

need(content.get("SchemaVersion") == 1, "environment schema version is not 1", failures)
need(len(archetypes) == 9 and actual == expected,
     "planet environment catalog does not cover exactly nine archetypes", failures)
for item in archetypes:
    name = item.get("Archetype", "<missing>")
    need(20 <= item.get("RadiusMinKm", -1) <= item.get("RadiusMaxKm", 999) <= 80,
         f"{name}: radius is outside 20..80 km", failures)
    need(0 <= item.get("CloudLayersMin", -1) <= item.get("CloudLayersMax", 99) <= 2,
         f"{name}: cloud layer count is outside 0..2", failures)
    ids = item.get("BiomeIds", [])
    if name == "gas_giant":
        need(item.get("Landable") is False and ids == [],
             "gas giant must be non-landable with no surface biomes", failures)
    else:
        need(item.get("Landable") is True and 1 <= len(ids) <= 8,
             f"{name}: landable planet must expose 1..8 biomes", failures)
        need(all(value in biomes for value in ids),
             f"{name}: unknown ecology biome reference", failures)

need('StarterPlanetArchetypes' in galaxy and
     '"temperate",\n        "desert",\n        "frozen",\n        "volcanic"' in galaxy,
     "starter system does not define four distinct Stage 2 planets", failures)
need("CurrentPlanetId => _currentPlanetId" in galaxy and
     "TrySelectCurrentPlanet" in galaxy and
     "ResolveSavedPlanetId" in galaxy,
     "current-planet runtime/persistence selection is incomplete", failures)
need('string CurrentPlanetId = ""' in models,
     "galaxy save data does not persist current planet compatibly", failures)
need("navigation.CurrentPlanetId" in save_database and
     "navigation.CurrentPlanetId.StartsWith(" in save_database and
     '"planet."' in save_database,
     "persistence boundary does not validate current planet identity", failures)
need("ValidateBiomeReferences" in catalog and "ExpectedArchetypeCount = 9" in catalog,
     "planet environment catalog validation is incomplete", failures)
for factor in ("latitudeCooling", "elevation", "waterDistance", "localNoise", "BaseMoisture"):
    need(factor in runtime, f"biome factor missing from runtime: {factor}", failures)
need("CloudLayerCount" in runtime and "WaterCoverage" in runtime and
     "AtmosphereDensity" in runtime,
     "environment profile lacks water/atmosphere/cloud data", failures)
need("TASK-150 planet environment acceptance" in acceptance and
     "starterPlanets == 4" in acceptance and
     "currentPlanetRoundTrip" in acceptance,
     "TASK-150 acceptance contract is incomplete", failures)
need("RunPlanetEnvironmentAcceptance();" in slice_main and
     'TASK-150 (F5)' in slice_main,
     "TASK-150 is not wired into the F5 acceptance matrix", failures)
need("BuildPlanetEnvironmentMapDetail" in slice_env and
     "BuildPlanetEnvironmentMapDetail(planet, current.StarType)" in slice_galaxy,
     "system map does not expose generated environment profiles", failures)
need("planet_atmosphere_shell.gdshader" in preview and
     "planet_cloud_shell.gdshader" in preview and
     "planet_water_shell.gdshader" in preview,
     "Planet Preview environment presentation is incomplete", failures)
need("PlanetEnvironmentProfile environment" in workbench and
     "Water={environment.WaterCoverage:P0}" in workbench,
     "developer Planet Preview does not report environment profile", failures)
need("GalaxyNavigation.CurrentPlanetId" in planet_map and
     "GalaxyNavigation.CurrentPlanet.HasAtmosphere" in audio and
     "GalaxyNavigation.CurrentPlanet.Archetype" in survival,
     "current-planet consumers still read starter Planets[0]", failures)
need("star_direction_world" in atmosphere_shader and "VIEW_MATRIX" in atmosphere_shader,
     "atmosphere shell does not vary color by star direction", failures)
for shader in (
    "src/Game.Client/Shaders/planet_atmosphere_shell.gdshader",
    "src/Game.Client/Shaders/planet_cloud_shell.gdshader",
    "src/Game.Client/Shaders/planet_water_shell.gdshader",
):
    need((ROOT / shader).is_file(), f"missing shader: {shader}", failures)
need("StarterSystem_ProvidesFourDistinctStageTwoPlanets" in tests and
     "PlanetEnvironmentProfiles_AreDeterministicBoundedAndBiomeSafe" in tests and
     "CurrentPlanetSelection_RoundTripsThroughGalaxySave" in tests and
     "GasGiantEnvironment_IsNonLandableAndHasNoSurfaceBiomes" in tests,
     "TASK-150 xUnit regression coverage is incomplete", failures)

if failures:
    print("TASK-150 PLANET ENVIRONMENT CONTRACT FAIL:")
    for failure in failures:
        print(f"- {failure}")
    sys.exit(1)

print(
    "TASK-150 PLANET ENVIRONMENT CONTRACT PASS: "
    "starterPlanets=4/4; archetypes=9/9; radius=20-80km; biomes=max8; "
    "water=1; atmosphere=1; clouds=0-2; climateFactors=1; persistence=1; "
    "systemMap=1; planetPreview=1; currentPlanetConsumers=1; persistenceBoundary=1; starDirection=1; shaders=3/3; f5=1; xunit=4/4."
)
