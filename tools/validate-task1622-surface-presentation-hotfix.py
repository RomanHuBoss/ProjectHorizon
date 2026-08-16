#!/usr/bin/env python3
"""Static regression gate for TASK-162.2 live planet-surface presentation."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
terrain_runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceTerrainRuntime.cs")
terrain_scene = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs")
world_runtime = text("src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceWorldCompositionRuntime.cs")
world_scene = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs")
atmosphere_clouds = text("src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudNode.cs")
main = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
star_system = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceStarSystem.cs")
terrain_manager = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
terrain_chunk = text("src/Game.Client/Scripts/Terrain/TerrainChunk.cs")

need('_ => (7.00, 0.024, 34.0)' in terrain_runtime,
     'temperate terrain is still prototype-scale/flat', failures)
need('"volcanic" => (12.00, 0.026, 39.0)' in terrain_runtime and
     '"oceanic" => (5.50, 0.022, 32.0)' in terrain_runtime,
     'archetype relief promotion is incomplete', failures)
need('PlanetSurfaceDistantTerrainResolution = 49' in terrain_scene and
     'PlanetSurfaceDistantTerrainHalfExtentMeters = 420.0' in terrain_scene and
     'PlanetSurfaceDistantTerrainInnerHalfExtentMeters = 58.0' in terrain_scene,
     'bounded-streamer distant terrain proxy contract missing', failures)
need('BuildPlanetDistantTerrainMesh' in terrain_scene and
     'EnsurePlanetSurfaceDistantTerrain(profile, force: false);' in terrain_scene,
     'distant terrain proxy is not refreshed with streamer center', failures)
need('EnsurePlayerAbovePlanetSurfaceFloor' in terrain_scene and
     'EnsurePlayerAbovePlanetSurfaceFloor();' in main,
     'saved/reset player can spawn inside promoted relief', failures)
need('0.0044 + atmosphere * 0.0022' in world_runtime and
     '0.0045, 0.0105' in world_runtime,
     'atmospheric perspective is too weak to hide proxy boundary', failures)
need('PlanetSurfaceSunVisual' in world_scene and
     'EmissionEnergyMultiplier = 5.0f' in world_scene and
     'UpdatePlanetSurfaceSunVisual();' in world_scene and
     ('sun.Set("sky_mode", 0)' in world_scene or ('sun.Set("sky_mode", 1)' in world_scene and 'sky_rotation' in world_scene)),
     'visible stellar-disc + procedural-sky binding missing', failures)
need((
        'random.RandfRange(105.0f, 165.0f)' in world_scene and
        'random.RandfRange(1.4f, 3.2f)' in world_scene
     ) or (
        'SphericalCloudLayer' in atmosphere_clouds and
        'sampler2D noise_a' in atmosphere_clouds and
        'RetireLegacyCloudClusters();' in world_scene
     ),
     'cloud presentation is neither legacy high-flat nor TASK-190 spherical noise-layer', failures)
need('Gameplay/PlanetSurfaceDistantTerrain' in star_system and
     'Gameplay/PlanetSurfaceSunVisual' in star_system,
     'new presentation nodes are not part of surface residency', failures)
need('RunSurfacePresentationHotfixAcceptance();' in main and
     'TASK-162.2 (F5)' in main and
     'TASK-162.2 surface presentation acceptance' in world_scene,
     'TASK-162.2 runtime acceptance/HUD wiring missing', failures)
need('VerboseGenerationLogging = false' in terrain_scene and
     'if (VerboseGenerationLogging)' in terrain_manager and
     'if (!VerboseGenerationLogging)' in terrain_chunk,
     'live streamer still floods Output with per-worker/per-chunk logs', failures)

if failures:
    print('TASK-162.2 SURFACE PRESENTATION HOTFIX CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-162.2 SURFACE PRESENTATION HOTFIX CONTRACT PASS: '
    'relief=macro; gameplayStreamer=25; distantProxy=840m; proxyHole=116m; '
    'atmosphere=edge-hiding; sunDisc=emissive+sky-bound; clouds=high-flat-or-spherical-noise; '
    'playerClearance=guarded; terrainLogs=summary-only; surfaceResidency=1; f5=1.'
)
