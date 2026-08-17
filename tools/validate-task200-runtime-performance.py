#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
fail = []

def text(path):
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition, message):
    if not condition:
        fail.append(message)

version = text('VERSION').strip()
need(version in {'0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must preserve alpha.200 or later accepted revision, got {version}')
policy = text('src/Game.Domain/Architecture/RuntimePerformanceBudgetPolicy.cs')
telemetry = text('src/Game.Client/Scripts/VerticalSlice/RuntimePerformanceTelemetry.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/RuntimePerformanceAcceptance.cs')
slice200 = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceRuntimePerformance.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
vegetation = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVegetationRegional.cs')
cloud = text('src/Game.Client/Scripts/VerticalSlice/PlanetAtmosphereCloudNode.cs')
cloud_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetAtmosphereClouds.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/RuntimePerformanceBudgetTests.cs')
frequencies = text('src/Game.Domain/Architecture/SystemFrequencyPolicy.cs')

# §27.1-27.3 exact Medium targets and >=30% Low scene reductions.
for token in (
    'TargetFramesPerSecond: 60.0',
    'CpuFrameMilliseconds: 16.6',
    'GpuFrameMilliseconds: 16.6',
    'MaximumDrawCalls: 1500',
    'MaximumRenderedPrimitives: 2_000_000',
    'MaximumActivePhysicsBodies: 500',
    'MaximumFullAi: 20',
    'MaximumSimplifiedAi: 80',
    'MaximumVideoMemoryBytes: 4L * Gibibyte',
    'MaximumProcessMemoryBytes: 6L * Gibibyte',
    'MaximumManagedAllocationBytesPerFrame: 256L * Kibibyte'):
    need(token in policy, f'Medium section-27 budget missing: {token}')
for token in (
    'TargetFramesPerSecond: 30.0',
    'CpuFrameMilliseconds: 33.3',
    'MaximumDrawCalls: 1050',
    'MaximumRenderedPrimitives: 1_400_000',
    'MaximumActivePhysicsBodies: 350',
    'MaximumFullAi: 14',
    'MaximumSimplifiedAi: 56'):
    need(token in policy, f'Low reduced budget missing: {token}')
need('low.MaximumDrawCalls <= medium.MaximumDrawCalls * 0.70' in acceptance,
     'acceptance does not enforce >=30 percent Low draw-call reduction')

# Real engine/runtime telemetry, sampled at architecture telemetry cadence.
for token in (
    'Performance.Monitor.TimeFps',
    'Performance.Monitor.TimeProcess',
    'Performance.Monitor.TimePhysicsProcess',
    'Performance.Monitor.TimeNavigationProcess',
    'Performance.Monitor.RenderTotalDrawCallsInFrame',
    'Performance.Monitor.RenderTotalPrimitivesInFrame',
    'Performance.Monitor.RenderVideoMemUsed',
    'Performance.Monitor.Physics3DActiveObjects',
    'Performance.Monitor.ObjectNodeCount',
    'Performance.Monitor.ObjectResourceCount'):
    need(token in telemetry, f'Godot performance monitor missing: {token}')
need('SystemFrequencyPolicy.TelemetryFlushHz' in telemetry,
     'performance telemetry not tied to section-38 telemetry cadence')
need('GC.GetTotalAllocatedBytes(false)' in telemetry,
     'managed allocation telemetry missing')
need('Environment.WorkingSet' in telemetry,
     'process working-set telemetry missing')
need('FaunaSimulationTier.MidHigh' in slice200 and 'FaunaSimulationTier.MidLow' in slice200 and
     'FarFaunaSnapshot.Population' not in slice200,
     'simplified-AI telemetry must count individually scheduled mid-tier AI, not statistical far population')
need('ManagedAllocationBytesPerFrame' in policy and 'allocationDelta / frames' in telemetry,
     'per-frame allocation accounting missing')
need('RuntimePerformanceOverrun.FrameRate' in policy and
     'AddInverseRatio(sample.FramesPerSecond, budget.TargetFramesPerSecond' in policy,
     'observed FPS target is not evaluated as a runtime budget signal')

# Hysteretic adaptive quality must remain presentation-only.
for token in ('ConstrainedOverrunSamples = 4', 'CriticalOverrunSamples = 12',
              'RecoveryCleanSamples = 20', 'SeverePressureRatio = 1.35'):
    need(token in policy, f'adaptive governor hysteresis missing: {token}')
need('VegetationDistanceScale' in policy and 'MaximumCloudLayers' in policy,
     'presentation quality settings missing')
need('PerformanceVegetationDistanceScale' in vegetation and 'distance /= qualityScale' in vegetation,
     'regional vegetation not connected to adaptive presentation scaling')
need('SetPerformanceQuality' in cloud and '_performanceCloudLayerLimit' in cloud,
     'cloud layer adaptive presentation hook missing')
need('_runtimePerformanceQualitySettings.MaximumCloudLayers' in cloud_slice,
     'planet cloud profile does not preserve performance quality hook on reconfigure')
need('UpdateRuntimePerformanceBudgeting(delta);' in main,
     'live performance sampler not updated from main loop')
need('InitializeRuntimePerformanceBudgeting();' in main,
     'performance runtime not initialized')
need('SystemFrequencyPolicy.NearbyAiHz = 10.0' not in slice200 and
     'SystemFrequencyPolicy.DistantAiHz = 2.0' not in slice200,
     'TASK-200 must not mutate authoritative AI frequencies')
need('public const double NearbyAiHz = 10.0' in frequencies and
     'public const double DistantAiHz = 2.0' in frequencies,
     'section-38 AI frequencies changed during performance work')

# F5/release contract.
need('RunRuntimePerformanceAcceptance();' in main and 'TASK-200 (F5)' in main and
     '_runtimePerformanceAcceptancePassed == true' in main,
     'TASK-200 F5/final acceptance gate missing')
need('TASK-200 runtime performance READY' in slice200 and
     'adaptive=presentation-only-vegetation+clouds' in slice200,
     'TASK-200 READY evidence missing')
need('RuntimePerformanceAcceptanceRunner' in acceptance and
     'budgetStatus={RuntimeBudgetStatus}' in acceptance,
     'TASK-200 acceptance output missing')
for test_name in (
    'MediumProfileMatchesSection27SceneBudgets',
    'LowProfileReducesSceneBudgetsByAtLeastThirtyPercent',
    'EvaluationReportsFrameRenderAndAllocationOverruns',
    'AdaptiveGovernorUsesHysteresisAndRecoversGradually',
    'AdaptiveQualityChangesOnlyPresentationSettings'):
    need(test_name in tests, f'TASK-200 xUnit test missing: {test_name}')
need('TASK-200' in text('README.md'), 'README TASK-200 section missing')
need('## [0.1.0-alpha.200]' in text('CHANGELOG.md'), 'CHANGELOG alpha.200 section missing')
need('TASK-200' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-200 missing')
need((ROOT / 'docs/RUNTIME_PERFORMANCE_BUDGETS.md').exists(),
     'TASK-200 runtime performance document missing')

if fail:
    print('TASK-200 RUNTIME PERFORMANCE CONTRACT FAIL:')
    for item in fail:
        print('ERROR:', item)
    sys.exit(1)

print('TASK-200 RUNTIME PERFORMANCE CONTRACT PASS: medium=60fps/16.6ms/1500draw/2Mprim/500physics/20+80AI/4GiBVRAM/6GiBRAM; low=30fps+>=30%-scene-reduction; telemetry=GodotPerformance+GC+working-set@2Hz; allocation<=256KiB/frame; adaptive=hysteretic-presentation-only; f5=1; xunit=1.')
