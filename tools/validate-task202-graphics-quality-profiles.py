#!/usr/bin/env python3
from pathlib import Path
import json, sys

ROOT = Path(__file__).resolve().parents[1]
fail=[]
def text(p): return (ROOT/p).read_text(encoding='utf-8')
def need(c,m):
    if not c: fail.append(m)

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218'}, f'VERSION must preserve alpha.202 or later accepted revision, got {version}')
policy=text('src/Game.Domain/Architecture/GraphicsQualityProfilePolicy.cs')
settings=text('src/Game.Client/Scripts/Application/GameUserSettings.cs')
panel=text('src/Game.Client/Scripts/Application/GameSettingsPanel.cs')
slice202=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGraphicsQualityProfiles.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/GraphicsQualityAcceptance.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
perf=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceRuntimePerformance.cs')
veg=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVegetationRegional.cs')
cloud=text('src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudNode.cs')
water=text('src/Game.Client/Scripts/VerticalSlice/PlanetaryWaterSurfaceNode.cs')
stream=text('src/Game.Client/Scripts/VerticalSlice/WorldStreamingRuntime.cs')
coord=text('src/Game.Client/Scripts/VerticalSlice/WorldStreamingCoordinatorNode.cs')
app=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceApplicationShell.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/GraphicsQualityProfileTests.cs')

for token in ('Low = 0','Medium = 1','High = 2','Compatibility = 3'):
    need(token in policy, f'graphics profile missing: {token}')
for token in ('VegetationDensityScale: 0.55','VegetationDistanceScale: 0.58','SurfaceDistanceScale: 0.58',
              'VegetationDensityScale: 0.85','SurfaceDistanceScale: 1.00',
              'VegetationDensityScale: 1.00','VegetationDistanceScale: 1.18','SurfaceDistanceScale: 1.20'):
    need(token in policy, f'profile scaling missing: {token}')
need('ShadowQuality: GraphicsShadowQuality.Disabled' in policy and 'SimplifiedShaders: true' in policy and
     'HeavyEffectsAllowed: false' in policy, 'Compatibility must disable shadows/heavy effects and use simplified shaders')
need('GraphicsQualityProfile.Low or GraphicsQualityProfile.Compatibility' in policy and
     'RuntimePerformanceProfile.Low' in policy, 'Low/Compatibility TASK-200 budget mapping missing')
need('graphics_quality_profile' in settings and 'GraphicsQualityProfilePolicy.IsValid' in settings,
     'graphics profile persistence/normalization missing')
need('_graphicsQualityOption' in panel and 'GraphicsQualityProfile.Compatibility' in panel,
     'graphics settings UI missing')
need('ApplyGraphicsQualityFromUserSettings(printReady: false);' in app,
     'live settings apply hook missing')

for loc in ('src/Game.Client/Content/localization.en.json','src/Game.Client/Content/localization.ru.json'):
    data=json.loads(text(loc)); strings=data['strings']
    for key in ('ui.settings.graphics_section','ui.settings.graphics_quality','ui.settings.graphics.low',
                'ui.settings.graphics.medium','ui.settings.graphics.high','ui.settings.graphics.compatibility','ui.settings.graphics.note'):
        need(key in strings and str(strings[key]).strip(), f'{loc} missing {key}')

need('SetGraphicsQuality(' in cloud and 'simplified_shading' in cloud and '_graphicsCloudLayerLimit' in cloud,
     'cloud quality/simplified shader hook missing')
need('SetGraphicsQuality(' in water and 'wave_quality' in water and 'depth_quality' in water and
     'simplified_shading' in water, 'water quality/simplified shader hook missing')
need('SetPresentationDistanceScale' in coord and 'double presentationDistanceScale' in stream and
     'ResolveFullDetailRadiusMeters(observer.TravelMode) * scale' in stream,
     'TASK-194 presentation distance scaling hook missing')
need('_runtimePerformanceQualitySettings.VegetationDistanceScale *' in perf and 'GraphicsVegetationDistanceScale' in perf,
     'TASK-200 adaptive ceiling composition missing')
need('ShouldRenderVegetationBatchForGraphics' in veg and 'GraphicsVegetationDensityScale' in slice202,
     'vegetation density profile hook missing')
need('directional_shadow_max_distance' in slice202 and 'directional_shadow_mode' in slice202,
     'shadow profile hook missing')
need('glow_enabled' in slice202 and 'amount_ratio' in slice202,
     'post-effect/particle profile hooks missing')
need('renderer.IsCompatibilityRenderer' in slice202 and 'GraphicsQualityProfile.Compatibility' in slice202,
     'renderer Compatibility override missing')
need('TASK-202 graphics quality READY' in slice202 and 'RunGraphicsQualityAcceptance();' in main and
     'TASK-202 (F5)' in main and '_graphicsQualityAcceptancePassed == true' in main,
     'TASK-202 live/F5/final gate missing')
need('GraphicsQualityAcceptanceRunner' in accept and 'profiles={(ProfilesComplete ? 4 : 0)}/4' in accept,
     'TASK-202 acceptance output missing')
for name in ('LowUsesFiftyToSixtyPercentSurfaceAndVegetationDistance','HighExceedsMediumPresentationQuality',
             'CompatibilityDisablesHeavyEffectsAndUsesSimplifiedShaders','PerformanceBudgetMappingRespectsProfileCeiling',
             'WorldStreamingPresentationScalePreservesNormativeDefault'):
    need(name in tests, f'TASK-202 xUnit missing: {name}')
need((ROOT/'docs/GRAPHICS_QUALITY_PROFILES.md').exists(), 'TASK-202 docs missing')
need('TASK-202' in text('README.md'), 'README TASK-202 missing')
need('## [0.1.0-alpha.202]' in text('CHANGELOG.md'), 'CHANGELOG alpha.202 missing')
need('TASK-202' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-202 missing')
for p in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task202-graphics-quality-profiles.py' in text(p), f'release gate missing in {p}')

if fail:
    print('TASK-202 GRAPHICS QUALITY PROFILE CONTRACT FAIL:')
    for x in fail: print('ERROR:',x)
    sys.exit(1)
print('TASK-202 GRAPHICS QUALITY PROFILE CONTRACT PASS: profiles=4; lowSurface=58%; medium=100%; high=120%; compatibility=simplified; vegetation=density+distance; shadows=profiled; clouds=profiled; water=profiled; post=profiled; particles=profiled; adaptive=TASK-200-ceiling; f5=1; xunit=1.')
