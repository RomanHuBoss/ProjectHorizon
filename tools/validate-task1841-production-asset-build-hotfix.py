#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
VERSION = ROOT / 'VERSION'
LIVE = ROOT / 'src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionAssetPipeline.cs'
CHANGELOG = ROOT / 'CHANGELOG.md'
README = ROOT / 'README.md'
STATUS = ROOT / 'REQUIREMENTS_STATUS.md'


def need(cond: bool, msg: str):
    if not cond:
        print(f'TASK-184.1 PRODUCTION ASSET BUILD HOTFIX CONTRACT FAIL: {msg}', file=sys.stderr)
        raise SystemExit(1)

version = VERSION.read_text(encoding='utf-8').strip()
need(version in {'0.1.0-alpha.184.1', '0.1.0-alpha.186'}, 'VERSION must preserve alpha.184.1 hotfix or later accepted revision')
live = LIVE.read_text(encoding='utf-8')
need('ProductionGlbResources.Count(path => ResourceLoader.Exists(path))' in live,
     'ResourceLoader.Exists must be adapted with an explicit string lambda')
need('ProductionGlbResources.Count(ResourceLoader.Exists)' not in live,
     'compile-breaking ResourceLoader.Exists method-group usage remains')
for path, token in [
    (CHANGELOG, '0.1.0-alpha.184.1'),
    (README, 'TASK-184.1'),
    (STATUS, 'TASK-184.1')]:
    need(token in path.read_text(encoding='utf-8'), f'{path.name} missing {token}')
print('TASK-184.1 PRODUCTION ASSET BUILD HOTFIX CONTRACT PASS: cs1503=guarded; resourceLoaderExists=lambda; version>=0.1.0-alpha.184.1.')
