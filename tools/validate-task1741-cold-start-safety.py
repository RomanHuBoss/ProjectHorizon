#!/usr/bin/env python3
"""Static contract gate for TASK-174.1 curved-surface cold-start safety."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
def text(path):
    p=ROOT/path
    return p.read_text(encoding='utf-8', errors='replace') if p.exists() else ''
def need(cond,msg,f):
    if not cond: f.append(msg)
f=[]
version=text('VERSION').strip()
terrain=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlicePlanetTerrain.cs')
spawn=text('src/Game.Client/Scripts/VerticalSlice/PlanetSurfaceSpawnSafetyRuntime.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
need(version in {'0.1.0-alpha.174.1','0.1.0-alpha.176','0.1.0-alpha.176.1','0.1.0-alpha.178','0.1.0-alpha.178.1','0.1.0-alpha.178.2','0.1.0-alpha.178.3','0.1.0-alpha.178.4','0.1.0-alpha.178.5','0.1.0-alpha.178.6','0.1.0-alpha.178.7','0.1.0-alpha.180','0.1.0-alpha.180.1','0.1.0-alpha.180.2','0.1.0-alpha.180.3','0.1.0-alpha.182'},'VERSION not alpha.174.1/176',f)
need('MinimumBodyCenterClearanceMeters = 1.02' in spawn and 'RequiredSemanticHeight' in spawn,'shared spawn safety policy missing',f)
need('ApplyPlanetSurfaceStartupClearanceGuard("terrain-bootstrap")' in terrain,'cold-start clearance is not applied synchronously during terrain bootstrap',f)
need('ApplyPlanetSurfaceStartupClearanceGuard("streamer-handoff")' in terrain,'async streamer handoff clearance guard missing',f)
need(terrain.count('BackfaceCollision = true') >= 2,'fallback/bridge collision is not backface-safe',f)
need('TASK-174.1 curved surface cold-start guard READY' in terrain and 'TASK-174.1 curved surface cold-start safety acceptance' in terrain,'TASK-174.1 runtime diagnostics/acceptance missing',f)
need('RunPlanetSurfaceStartupSafetyAcceptance();' in main,'TASK-174.1 F5 acceptance not wired',f)
need('PlanetCurvedSurface_ColdStartGuardKeepsBodyAboveCurvedCollider' in tests,'TASK-174.1 xUnit regression missing',f)
if f:
    print('TASK-174.1 CURVED SURFACE COLD-START SAFETY CONTRACT FAIL:')
    for x in f: print('- '+x)
    raise SystemExit(1)
print('TASK-174.1 CURVED SURFACE COLD-START SAFETY CONTRACT PASS: spawn=terrain-aware; fallback=backface-safe; handoff=guarded; clearance=1.02m; f5=1; xunit=1.')
