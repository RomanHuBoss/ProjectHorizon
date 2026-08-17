#!/usr/bin/env python3
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
fail = []

def text(path):
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition, message):
    if not condition:
        fail.append(message)

version = text('VERSION').strip()
need(version in {'0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218','0.1.0-alpha.220'}, f'VERSION must be alpha.212, got {version}')
policy = text('src/Game.Domain/CrossPlatformDeterminismPolicy.cs')
runtime = text('src/Game.Client/Scripts/VerticalSlice/CrossPlatformDeterminismRuntime.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/CrossPlatformDeterminismAcceptance.cs')
integration = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceCrossPlatformDeterminism.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests = text('tests/ProjectHorizon.Tests/Unit/CrossPlatformDeterminismTests.cs')
generator = text('src/Game.Domain/ProjectHorizonGenerator.cs')
golden = text('src/Game.Client/Scripts/Testing/GoldenSeedContract.cs')
ci = text('.github/workflows/ci.yml')
release = text('.github/workflows/release.yml')

for token in (
    'RequiredPlatformFamilies = 2',
    'PlatformSeedParityRequired = true',
    'GeneratorVersionBumpRequiredForWorldChanges = true',
    'SinglePlayerRequiresInternet = false',
    'CloudFeaturesOptional = true',
    'PermittedProductionNetworkDependencies = 0'):
    need(token in policy, f'cross-platform/offline policy missing: {token}')
need('WindowsX64' in policy and 'LinuxX64' in policy, 'Windows/Linux x64 platform policy missing')
need('public const int Version = 3' in generator, 'TASK-212 must not silently bump/change generator version')
need('regenerate the manifest in the same reviewed change' in golden, 'golden manifest version-bump guard missing')

for token in (
    'BuildCanonicalWorldSignature',
    'SHA256.HashData',
    'Encoding.UTF8',
    'CultureInfo.InvariantCulture',
    'MidpointRounding.AwayFromZero',
    'en-US',
    'ru-RU',
    'tr-TR',
    'PlanetSurfaceTerrainRuntime.Sample',
    'PlanetaryPoiPlanner.Plan'):
    need(token in runtime, f'canonical world signature/culture surface coverage missing: {token}')
need('ProjectHorizonGenerator.Version' in runtime and 'GalaxyNavigationRuntime.GeneratorVersion' in runtime,
     'generator-version binding missing from runtime acceptance')
need('CultureInfo.CurrentCulture = originalCulture' in runtime and
     'CultureInfo.CurrentUICulture = originalUiCulture' in runtime,
     'culture restoration missing')
need('TASK-212 cross-platform determinism/offline acceptance' in runtime,
     'TASK-212 output evidence missing')
need('TASK-212 cross-platform determinism/offline READY' in integration,
     'TASK-212 startup evidence missing')
need('CrossPlatformDeterminismAcceptanceRunner.Run' in integration,
     'TASK-212 live acceptance integration missing')
need('PrintCrossPlatformDeterminismReady();' in main and
     'RunCrossPlatformDeterminismAcceptance();' in main and
     'TASK-212 (F5)' in main and
     '_crossPlatformDeterminismAcceptancePassed == true' in main,
     'TASK-212 F5/final gate missing')
need('CrossPlatformDeterminismAcceptanceRunner' in acceptance,
     'TASK-212 acceptance runner missing')
for name in (
    'PlayerPlatformPolicyCoversWindowsAndLinuxX64',
    'SameSeedReplaysSameCanonicalWorldSignature',
    'CanonicalWorldSignatureIsCultureInvariant',
    'GeneratorChangesRemainBoundToExplicitVersioning',
    'SinglePlayerIsOfflineFirstByPolicy'):
    need(name in tests, f'TASK-212 xUnit missing: {name}')

# Section 41.2: one shared reviewed golden contract must run natively on both OS families.
need('determinism-parity' in ci and 'ubuntu-latest' in ci and 'windows-latest' in ci,
     'CI Windows/Linux determinism matrix missing')
need('GoldenSeedTests' in ci and 'CrossPlatformDeterminismTests' in ci,
     'CI matrix does not execute shared golden + TASK-212 tests')
need('determinism-parity' in release and 'ubuntu-latest' in release and 'windows-latest' in release,
     'release Windows/Linux determinism matrix missing')
need('needs: determinism-parity' in release,
     'release package is not gated by cross-platform determinism matrix')

# Section 41.11: production single-player code must not acquire a mandatory network stack.
network_patterns = [
    r'\bSystem\.Net\b', r'\bHttpClient\b', r'\bHttpRequestMessage\b',
    r'\bWebRequest\b', r'\bWebClient\b', r'\bTcpClient\b', r'\bUdpClient\b',
    r'\bSocket\b', r'\bWebSocket\b', r'\bGrpc\b', r'\bSignalR\b'
]
network_hits = []
for path in (ROOT / 'src').rglob('*.cs'):
    source = path.read_text(encoding='utf-8')
    for pattern in network_patterns:
        if re.search(pattern, source):
            network_hits.append(f'{path.relative_to(ROOT)}:{pattern}')
need(not network_hits, 'production network dependency detected: ' + ', '.join(network_hits[:8]))

need((ROOT / 'docs/CROSS_PLATFORM_DETERMINISM_OFFLINE.md').exists(), 'TASK-212 docs missing')
need('TASK-212' in text('README.md'), 'README TASK-212 missing')
need('## [0.1.0-alpha.212]' in text('CHANGELOG.md'), 'CHANGELOG alpha.212 missing')
need('TASK-212' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-212 missing')
for path in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task212-cross-platform-determinism.py' in text(path), f'release gate missing in {path}')

if fail:
    print('TASK-212 CROSS-PLATFORM DETERMINISM/OFFLINE CONTRACT FAIL:')
    for item in fail:
        print('ERROR:', item)
    sys.exit(1)

print('TASK-212 CROSS-PLATFORM DETERMINISM/OFFLINE CONTRACT PASS: platforms=windows-x64+linux-x64; sharedGolden=1; generatorVersion=3; canonicalSHA256=1; cultures=en-US/ru-RU/tr-TR; terrain+poi=1; productionNetworkDependencies=0; offlineSinglePlayer=1; ciMatrix=2OS; releaseMatrix=2OS; f5=1; xunit=1.')
