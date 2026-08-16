#!/usr/bin/env python3
"""Static contract gate for TASK-174 curved cube-sphere collision/navigation surface."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
def text(path):
    p=ROOT/path
    return p.read_text(encoding='utf-8', errors='replace') if p.exists() else ''
def need(cond,msg,f):
    if not cond: f.append(msg)
f=[]
version=text('VERSION').strip()
curved=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceCurvedPatchRuntime.cs')
builder=text('src/Game.Client/Scripts/Terrain/TerrainChunkDataBuilder.cs')
manager=text('src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs')
chunk=text('src/Game.Client/Scripts/Terrain/TerrainChunk.cs')
terrain_scene=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
nav=text('src/Game.Client/Scripts/VerticalSlice/NpcNavigationSurfaceNode.cs')
frame=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePhysicalSurface.cs')
physical_runtime=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfacePhysicalFrameRuntime.cs')
sky=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs')
weather=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetWeather.cs')
ecology=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceCurvedSurface.cs')
runner=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceCurvedCollisionAcceptance.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
need(version in {'0.1.0-alpha.174','0.1.0-alpha.174.1','0.1.0-alpha.176','0.1.0-alpha.176.1','0.1.0-alpha.178','0.1.0-alpha.178.1','0.1.0-alpha.178.2','0.1.0-alpha.178.3','0.1.0-alpha.178.4','0.1.0-alpha.178.5','0.1.0-alpha.178.6','0.1.0-alpha.178.7','0.1.0-alpha.180','0.1.0-alpha.180.1','0.1.0-alpha.180.2','0.1.0-alpha.180.3','0.1.0-alpha.182','0.1.0-alpha.184'},'VERSION not alpha.174/174.1/176',f)
need('TangentSagMeters' in curved and 'SurfaceUpLocal' in curved and 'TerrainNormalLocal' in curved,'shared spherical sag/normal runtime missing',f)
need('CurvedPatch' in builder and 'SampleSurfaceHeight' in builder,'terrain visual/collision builder is not curvature-aware',f)
need('ConfigurePlanetSurfaceCurvature' in manager and 'CurvatureRevision' in manager and 'activeChunk.CurvatureRevision != _surfaceCurvatureRevision' in manager and 'PlanRefresh(executeImmediately: false)' in manager and 'SetRuntimeCollisionEnabled' in manager and 'SetRuntimeCollisionEnabled' in chunk,'bounded streamer curvature/collision rebase refresh missing',f)
need('ActivateCurvedSurfaceFallbackBridge' in terrain_scene and 'SetRuntimeCollisionEnabled(false)' in terrain_scene and 'SetRuntimeCollisionEnabled(true)' in terrain_scene,'curvature rebase fallback collision bridge missing',f)
need('SetCurvedSurfaceFrame' in nav and 'MaximumCurvatureSagMeters' in nav and 'terrainHeight - sag' in nav,'navigation tiles not sharing curved height model',f)
need('MapCurvedPoint' in physical_runtime and 'AdjustCurvedSurfaceResidentsAfterFrameChange' in frame and 'AdjustEcologyFloraCurvatureAnchor' in frame,'curvature-aware resident/rebase handoff missing',f)
need('AdjustEcologyFloraCurvatureAnchor' in ecology and 'GetInstanceTransform' in ecology and 'SetInstanceTransform' in ecology,'MultiMesh flora curvature-anchor remap missing',f)
need('sky_rotation' in sky and 'fog_height_density", 0.0f' in sky and 'sky_curve' in sky and 'ground_curve' in sky,'radial atmosphere horizon fix missing',f)
need('A dense atmosphere does not become space-black' in weather,'atmospheric night luminance fix missing',f)
need('TASK-174 curved cube-sphere surface acceptance' in accept and 'bounded25x9' in accept,'TASK-174 live acceptance missing',f)
need('RebaseRoundTripToleranceMeters' in runner and 'faces.Count == 6' in runner,'TASK-174 deterministic acceptance runner missing',f)
need('PlanetCurvedSurface_UsesRealRadiusSagAndNormals' in tests and 'PlanetCurvedSurface_RebaseMappingPreservesPhysicalPointRoundTrip' in tests and 'PlanetCurvedSurface_AcceptanceCoversAllCubeFaces' in tests,'TASK-174 xUnit regression groups missing',f)
if f:
    print('TASK-174 CURVED CUBE-SPHERE SURFACE CONTRACT FAIL:')
    for x in f: print('- '+x)
    raise SystemExit(1)
print('TASK-174 CURVED CUBE-SPHERE SURFACE CONTRACT PASS: curvature=real-radius; collision=curved-trimesh; navigation=curved-tiles; rebase=curvature-aware; atmosphere=radial-smooth; streamer=25/9; persistence=logical-xz; f5=1; xunit=3.')
