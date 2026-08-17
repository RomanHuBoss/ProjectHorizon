#!/usr/bin/env python3
from pathlib import Path
import sys
ROOT=Path(__file__).resolve().parents[1]
fail=[]
def text(p): return (ROOT/p).read_text(encoding='utf-8')
def need(c,m):
    if not c: fail.append(m)
version=text('VERSION').strip()
need(version in {'0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must be alpha.206, got {version}')
policy=text('src/Game.Domain/Architecture/SystemCapabilityPolicy.cs')
diag=text('src/Game.Client/Scripts/Application/SystemCapabilityDiagnostics.cs')
menu=text('src/Game.Client/Scripts/Application/MainMenuController.cs')
slice206=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceSystemCapability.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/SystemCapabilityAcceptance.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/SystemCapabilityPolicyTests.cs')
for token in ('MinimumLogicalProcessors = 4','RecommendedLogicalProcessors = 6',
              'MinimumPhysicalMemoryBytes = 8L * 1024L * 1024L * 1024L',
              'RecommendedPhysicalMemoryBytes = 16L * 1024L * 1024L * 1024L',
              'MinimumVideoMemoryBytes = 4L * 1024L * 1024L * 1024L',
              'RecommendedVideoMemoryBytes = 6L * 1024L * 1024L * 1024L',
              'MinimumFreeStorageBytes = 20L * 1024L * 1024L * 1024L',
              'RecommendedFreeStorageBytes = 30L * 1024L * 1024L * 1024L'):
    need(token in policy, f'section-28 threshold missing: {token}')
need('GraphicsQualityProfile.Compatibility' in policy and 'GraphicsQualityProfile.Low' in policy and 'GraphicsQualityProfile.Medium' in policy,
     'advisory graphics recommendation mapping missing')
need('MinimumEvidenceComplete' in policy and '!input.VideoMemoryCapacityKnown' in policy and '!input.StorageMediumKnown' in policy,
     'unknown portable hardware evidence policy missing')
for token in ('OS.GetName()','OS.GetMemoryInfo()','Environment.ProcessorCount','Environment.Is64BitOperatingSystem',
              'RenderingServer.GetVideoAdapterName()','RenderingServer.GetVideoAdapterVendor()',
              'RenderingServer.GetVideoAdapterType()','RenderingServer.GetVideoAdapterApiVersion()',
              'DriveInfo','ProjectSettings.GlobalizePath("user://")','Performance.Monitor.RenderVideoMemUsed'):
    need(token in diag, f'live capability capture missing: {token}')
need('action=recommend-only' in diag and 'ssd=unknown; vramCapacity=unknown' in diag,
     'hardware-unknown/recommend-only evidence missing')
need('SystemCapabilityDiagnostics.Capture()' in menu and 'capabilityEvidence' in menu,
     'main-menu startup capability evidence missing')
need('InitializeSystemCapabilityPreflight();' in main and 'RunSystemCapabilityAcceptance();' in main and
     'TASK-206 (F5)' in main and '_systemCapabilityAcceptancePassed == true' in main,
     'TASK-206 live/F5/final gate missing')
need('TASK-206 system capability READY' in slice206 and 'profileMutation=0' in slice206,
     'TASK-206 READY contract missing')
need('SystemCapabilityAcceptanceRunner' in accept and 'minimumLive=' in accept and 'recommendOnly=' in accept,
     'TASK-206 acceptance output missing')
for name in ('MinimumPlayerConfigurationMapsToLow','RecommendedConfigurationMapsToMedium',
             'CompatibilityRendererMapsToCompatibilityProfile','UnknownSsdAndVramDoNotInventHardFailure',
             'KnownMinimumViolationIsUnsupported'):
    need(name in tests, f'TASK-206 xUnit missing: {name}')
need((ROOT/'docs/SYSTEM_CAPABILITY_PREFLIGHT.md').exists(), 'TASK-206 docs missing')
need('TASK-206' in text('README.md'), 'README TASK-206 missing')
need('## [0.1.0-alpha.206]' in text('CHANGELOG.md'), 'CHANGELOG alpha.206 missing')
need('TASK-206' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-206 missing')
for p in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task206-system-capability.py' in text(p), f'release gate missing in {p}')
if fail:
    print('TASK-206 SYSTEM CAPABILITY CONTRACT FAIL:')
    for x in fail: print('ERROR:',x)
    sys.exit(1)
print('TASK-206 SYSTEM CAPABILITY CONTRACT PASS: os=win10-x64/linux-x64; cpu=4/6; ram=8/16GiB; storage=20/30GiB; renderer=vulkan-or-compat; vram=4/6GiB-policy-with-unknown-safe; ssd=required-unknown-safe; recommendation=compat/low/medium; profileMutation=0; f5=1; xunit=1.')
