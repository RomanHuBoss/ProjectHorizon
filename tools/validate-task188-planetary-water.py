#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
failures=[]

def need(cond,msg):
    if not cond: failures.append(msg)

def text(path): return (ROOT/path).read_text(encoding='utf-8')

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.188','0.1.0-alpha.192','0.1.0-alpha.192.1','0.1.0-alpha.194','0.1.0-alpha.196','0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must preserve alpha.188 or later accepted revision, got {version}')

runtime=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryWaterRuntime.cs')
surface=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryWaterSurfaceNode.cs')
integration=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetaryWater.cs')
player=text('src/Game.Client/Scripts/Player/PlayerController.cs')
survival=text('src/Game.Client/Scripts/VerticalSlice/PlayerSurvivalRuntime.cs')
slice_cs=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryWaterAcceptance.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/PlanetaryWaterTests.cs')

# §9.6: fixed spherical level, oceans + local lakes, waves/reflection/depth, underwater post, swimming, no fluid sim.
need('DefaultOceanSurfaceHeightMeters = 0.55' in runtime, 'fixed semantic water level missing')
need('OceanCoverageThreshold' in runtime and 'StarterLakes' in runtime, 'ocean/local lake policy missing')
need('TangentSagMeters' in surface and 'BuildCurvedGrid' in surface and 'BuildCurvedDisc' in surface, 'curved spherical water geometry missing')
need('TIME' in surface and 'wave_height' in surface and 'VERTEX.y' in surface, 'shader waves missing')
need('SPECULAR = 0.92' in surface and 'fresnel' in surface, 'sky/environment specular response missing')
need('hint_depth_texture' in surface and 'depth_below_surface' in surface and 'deep_color' in surface, 'depth-based darkening missing')
need('hint_screen_texture' in surface and 'UnderwaterPostShaderSource' in surface and 'underwater_tint' in surface, 'underwater screen post missing')
need('SwimmingEnterDepthMeters' in runtime and 'UnderwaterEnterDepthMeters' in runtime and 'wasSwimming' in runtime, 'water-state hysteresis missing')
need('SetWaterImmersion' in player and 'buoyancyTarget' in player, 'swimming/buoyancy locomotion missing')
need('UnderwaterMinimumOxygenDrainPerSecond' in survival and 'if (Underwater)' in survival, 'underwater oxygen drain missing')
need('if (Swimming)\n            {\n                oxygenRate' not in survival, 'oxygen must not drain merely because body is swimming')
need('Monitoring = false' in integration and 'Monitorable = false' in integration and 'WaterPool' in integration, 'legacy pool is not retired')
need('fluidSimulation=0' in integration and 'NoFluidSimulation: true' in acceptance, 'no-fluid-simulation contract missing')
need('RunPlanetaryWaterAcceptance();' in slice_cs and 'TASK-188 (F5)' in slice_cs and '_planetaryWaterAcceptancePassed == true' in slice_cs, 'F5/final acceptance wiring missing')
need('PlanetaryWaterAcceptanceRunner' in tests and 'WaterDepthStateUsesHysteresis' in tests, 'TASK-188 xUnit coverage missing')
need('TASK-188' in text('README.md'), 'README TASK-188 section missing')
need('## [0.1.0-alpha.188]' in text('CHANGELOG.md'), 'CHANGELOG alpha.188 section missing')
need('TASK-188' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-188 missing')
need((ROOT/'docs/PLANETARY_WATER_RUNTIME.md').exists(), 'water runtime document missing')

if failures:
    print('TASK-188 PLANETARY WATER CONTRACT FAIL:')
    for f in failures: print('ERROR:',f)
    sys.exit(1)
print('TASK-188 PLANETARY WATER CONTRACT PASS: fixedLevel=1; ocean=1; lakes=1; curved=1; waves=1; reflection=1; depth=1; underwaterPost=1; swim=1; oxygen=underwater-only; fluidSimulation=0; f5=1; xunit=1.')
