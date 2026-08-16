#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
failures=[]
def need(cond,msg):
    if not cond: failures.append(msg)
def text(path): return (ROOT/path).read_text(encoding='utf-8')

version=text('VERSION').strip()
need(version=='0.1.0-alpha.190', f'VERSION must be 0.1.0-alpha.190, got {version}')
runtime=text('src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudRuntime.cs')
node=text('src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudNode.cs')
integration=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetAtmosphereClouds.cs')
weather=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetWeather.cs')
world=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldComposition.cs')
safety=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceShipSurfaceSafety.cs')
latch=text('src/Game.Client/Scripts/VerticalSlice/SurfaceContactLatchRuntime.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudAcceptance.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/PlanetAtmosphereCloudTests.cs')

# §9.7 atmosphere: shell + gradient + star-direction + horizon + density + sunset; low-cost no ray marching.
need('AtmosphereScatteringShell' in node and 'SphereMesh' in node and 'cull_front' in node, 'spherical atmosphere shell missing')
need('star_direction' in node and 'dot(direction, star_dir)' in node, 'star-direction atmospheric colour missing')
need('horizon_amplification' in node and '1.0 - vertical' in node, 'horizon amplification missing')
need('atmosphere_opacity' in node and 'AtmosphereDensity' in runtime, 'atmosphere density response missing')
need('sunset_color' in node and 'sunset_factor' in node, 'sunset colour response missing')
need('raymarch' not in node.lower(), 'raymarch implementation is forbidden')
need('while (' not in node and 'for (' not in node.split('public const string AtmosphereShaderSource',1)[1].split('""";',1)[0], 'atmosphere shader must not ray-march/loop')

# §9.8 clouds: 1..2 spherical layers, scrolling noise textures, density, simple surface shadow.
need('MaximumCloudLayers = 2' in runtime and 'Math.Clamp(environment.CloudLayerCount, 0, MaximumCloudLayers)' in runtime, '1..2 cloud layer policy missing')
need('SphericalCloudLayer' in node and 'sampler2D noise_a' in node and 'sampler2D noise_b' in node, 'spherical noise cloud layers missing')
need('TIME' in node and 'scroll_a' in node and 'scroll_b' in node, 'scrolling cloud noise missing')
need('uniform float density' in node and 'CloudMultiplier' in runtime, 'weather cloud density response missing')
need('ApplyCloudShadow' in runtime and 'CurrentCloudShadowFactor' in integration and 'ApplyCloudShadow' in weather, 'simplified surface cloud shadow dimming missing')
need('CloudCluster_' not in world and 'Lobe_' not in world and 'RetireLegacyCloudClusters' in world, 'legacy local cloud blobs not retired')
for name in ('cloud_noise_1.png','cloud_noise_2.png'):
    p=ROOT/'src/Game.Client/Assets/Textures/Environment'/name
    need(p.exists() and p.stat().st_size>1024, f'{name} missing/empty')
    if p.exists(): need(p.read_bytes()[:8]==b'\x89PNG\r\n\x1a\n', f'{name} is not PNG')

# Owner-log regression: one contact episode stays latched until stable clearance.
need('ReleaseClearanceMeters = 4.35' in latch and 'ReleaseStableFrames = 12' in latch, 'surface-contact release hysteresis missing')
need('SurfaceContactLatchRuntime.UpdateReleaseFrames' in safety and 'latch=TASK-190' in safety, 'surface-contact latch not wired')
need('LethalNormalImpactSpeed' not in safety or 'PlanetaryImpactRuntime.IsLethalSurfaceImpact' in safety, 'lethal crash arbiter must remain')

need('RunPlanetAtmosphereCloudAcceptance();' in slice_cs and 'TASK-190 (F5)' in slice_cs and '_planetAtmosphereCloudAcceptancePassed == true' in slice_cs, 'TASK-190 F5/final gate missing')
need('PlanetAtmosphereCloudAcceptanceRunner' in tests and 'SurfaceContactLatchRequiresStableClearanceBeforeRecovery' in tests, 'TASK-190 xUnit coverage missing')
need('TASK-190' in text('README.md'), 'README TASK-190 section missing')
need('## [0.1.0-alpha.190]' in text('CHANGELOG.md'), 'CHANGELOG alpha.190 section missing')
need('TASK-190' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-190 missing')
need((ROOT/'docs/PLANETARY_ATMOSPHERE_CLOUDS.md').exists(), 'TASK-190 runtime doc missing')

if failures:
    print('TASK-190 ATMOSPHERE/CLOUD CONTRACT FAIL:')
    for f in failures: print('ERROR:',f)
    sys.exit(1)
print('TASK-190 ATMOSPHERE/CLOUD CONTRACT PASS: shell=1; starDirectional=1; horizon=1; sunset=1; density=1; cloudLayers<=2; noiseScroll=1; surfaceShadow=1; noRayMarch=1; legacyBlobs=0; contactLatch=1; f5=1; xunit=1.')
