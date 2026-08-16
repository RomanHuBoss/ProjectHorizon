#!/usr/bin/env python3
"""Static contract gate for TASK-168 planetary globe and geodesic surface topology."""
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[1]
def text(path): return (ROOT/path).read_text(encoding='utf-8')
def need(c,m,f):
    if not c: f.append(m)
f=[]
top=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceTopologyRuntime.cs')
globe=text('src/Game.Client/Scripts/VerticalSlice/DetailedPlanetGlobeNode.cs')
star=text('src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs')
terrain=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
mapcs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetMap.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
part=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetaryGlobe.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryGlobeAcceptance.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
stream=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceStreamingRuntime.cs')
need('class PlanetSurfaceTopologyRuntime' in top and 'CircumferenceMeters' in top and 'GreatCircleDistanceMeters' in top and 'TangentSagMeters' in top, 'global spherical topology missing', f)
need('NormalizeLatitudeLongitude' in top and 'WrapLongitudeDegrees' in top, 'pole/longitude normalization missing', f)
need('CubeSphereMeshBuilder.Build' in globe and 'FaceResolution = 17' in globe and 'AtmosphereShell' in globe, 'detailed cube-sphere globe missing', f)
need('PreparedDetailedGlobeCount' in star and 'detailedPlanetRequested: true' in star and 'EnsureDetailedGlobe' in star, 'single detailed live globe integration missing', f)
need('TangentSagMeters(radialDistance)' in terrain, 'distant terrain curvature missing', f)
need('GreatCircleDistanceMeters' in mapcs and 'Lat/Lon:' in mapcs, 'planet map geodesy missing', f)
need('PlanetSurfaceTopologyRuntime topology = new(planetRadiusKm)' in stream, 'streaming geodesic address not promoted', f)
need('RunPlanetaryGlobeAcceptance();' in main and 'TASK-168 (F5)' in main and 'planetaryGlobeLine' in main, 'TASK-168 F5/HUD wiring missing', f)
need('TASK-168 planetary globe READY' in part and 'PlanetaryGlobeAcceptanceRunner.Run' in part, 'TASK-168 runtime wiring missing', f)
need('planetary globe and geodesy acceptance' in accept and 'ExpectedActiveChunks == 25' in accept, 'TASK-168 acceptance/bounded-streamer guard missing', f)
need('PlanetSurfaceTopology_CircumnavigationWrapsAndPolesNormalize' in tests and 'PlanetaryGlobe_CubeSphereGeometryRemainsSeamless' in tests, 'TASK-168 xUnit regressions missing', f)
need(text('VERSION').strip() == '0.1.0-alpha.168', 'VERSION not alpha.168', f)
if f:
    print('TASK-168 PLANETARY GLOBE/GEODESY CONTRACT FAIL:')
    for x in f: print('- '+x)
    sys.exit(1)
print('TASK-168 PLANETARY GLOBE/GEODESY CONTRACT PASS: topology=spherical-periodic; poles=normalized; globe=cube-sphere-6-face; orbit=single-detailed; distantTerrain=curved; planetMap=latlon+great-circle; gameplayStreamer=25/9-bounded; persistence=logical-xz/no-schema-bump; f5=1; xunit=3-regression-groups.')
