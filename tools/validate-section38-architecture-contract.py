#!/usr/bin/env python3
from __future__ import annotations
import pathlib, re, sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / 'src' / 'Game.Client' / 'Scripts'
DOMAIN = ROOT / 'src' / 'Game.Domain'
APPLICATION = ROOT / 'src' / 'Game.Application'
PRODUCTION_CS_ROOTS = (SRC, DOMAIN, APPLICATION)
errors: list[str] = []


def require(cond: bool, message: str) -> None:
    if not cond:
        errors.append(message)


def text(path: pathlib.Path) -> str:
    return path.read_text(encoding='utf-8')

csproj = text(ROOT/'src/Game.Client/Game.Client.csproj')
props = text(ROOT/'Directory.Build.props')
require('<Nullable>enable</Nullable>' in csproj, 'Nullable is not enabled in Game.Client.csproj')
require('TreatWarningsAsErrors' in props and 'ContinuousIntegrationBuild' in props, 'CI warnings-as-errors contract missing')

# Public interfaces must have XML documentation immediately before declaration.
interfaces = []
for source_root in PRODUCTION_CS_ROOTS:
  for path in source_root.rglob('*.cs'):
    lines = path.read_text(encoding='utf-8').splitlines()
    for idx, line in enumerate(lines):
        if re.search(r'\bpublic\s+interface\s+\w+', line):
            interfaces.append((path, idx+1, line.strip()))
            window='\n'.join(lines[max(0,idx-4):idx])
            require('/// <summary>' in window, f'public interface undocumented: {path.relative_to(ROOT)}:{idx+1}')
require(len(interfaces) >= 5, f'expected >=5 public interfaces after architecture hardening, got {len(interfaces)}')

# Every production Task/ValueTask operation must expose CancellationToken explicitly.
async_api = re.compile(r'(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?(?:Task(?:<[^;{]+?>)?|ValueTask(?:<[^;{]+?>)?)\s+\w+\s*\((.*?)\)', re.S)
for source_root in PRODUCTION_CS_ROOTS:
    for path in source_root.rglob('*.cs'):
        source=path.read_text(encoding='utf-8')
        for match in async_api.finditer(source):
            require('CancellationToken' in match.group(1), f'async operation lacks CancellationToken: {path.relative_to(ROOT)}')

# Required typed domain events and a non-Godot event bus.
events_source=text(DOMAIN/'Architecture/DomainEvents.cs')
bus_source=text(APPLICATION/'Architecture/DomainEventBus.cs')
required_events=['ItemAdded','ItemRemoved','ResourceMined','PlanetEntered','PlanetExited','SystemDiscovered','QuestAccepted','QuestCompleted','ShipDamaged','BaseModulePlaced','SaveRequested']
for name in required_events:
    require(re.search(rf'public\s+sealed\s+record\s+{name}\b', events_source) is not None, f'missing typed event {name}')
require('using Godot' not in events_source and 'using Godot' not in bus_source and 'Godot.' not in events_source and 'Godot.' not in bus_source, 'domain event contracts depend on Godot')
require('IDomainEventBus' in bus_source and 'Subscribe<TEvent>' in bus_source and 'Publish<TEvent>' in bus_source, 'typed event bus incomplete')

# Save coordinator dependencies must be explicit at every construction site, including target-typed acceptance helpers.
constructor_pattern = re.compile(r'(?:using\s+)?SaveAutosaveCoordinator\s+\w+\s*=\s*new\s*\((.*?)\);', re.S)
for path in SRC.rglob('*.cs'):
    source = path.read_text(encoding='utf-8')
    for match in constructor_pattern.finditer(source):
        require('DomainEventBus' in match.group(1) or 'DomainEvents' in match.group(1),
                f'autosave coordinator dependency not explicitly passed: {path.relative_to(ROOT)}')
require('new SaveAutosaveCoordinator(database, DomainEvents)' in text(SRC/'VerticalSlice/SalvageRepairSlice.cs'),
        'shipping autosave coordinator does not receive the live domain event bus')

slice_arch=text(SRC/'VerticalSlice/SalvageRepairSliceArchitecture.cs')
for name in required_events:
    require(f'Subscribe<{name}>' in slice_arch, f'live event subscription missing for {name}')
require('PublishDomainEvent(new ResourceMined' in text(SRC/'VerticalSlice/SalvageRepairSlice.cs'), 'resource mining bypasses domain event bus')
require('PublishDomainEvent(new ShipDamaged' in text(SRC/'VerticalSlice/SalvageRepairSlice.cs'), 'ship damage bypasses domain event bus')
require('PublishDomainEvent(new BaseModulePlaced' in text(SRC/'VerticalSlice/SalvageRepairSlice.cs'), 'base placement bypasses domain event bus')
require('PublishDomainEvent(new SystemDiscovered' in text(SRC/'VerticalSlice/SalvageRepairSliceGalaxy.cs'), 'system discovery bypasses domain event bus')
require('PublishDomainEvent(new PlanetExited' in text(SRC/'VerticalSlice/SalvageRepairSliceVoyage.cs'), 'planet exit bypasses domain event bus')
require('PublishDomainEvent(new PlanetEntered' in text(SRC/'VerticalSlice/SalvageRepairSliceVoyage.cs'), 'planet entry bypasses domain event bus')

# Frequencies and batched telemetry.
project_config=text(ROOT/'src/Game.Client/project.godot')
require('common/physics_ticks_per_second=60' in project_config, 'Godot physics tick rate is not explicitly pinned to 60 Hz')
player_controller=text(SRC/'Player/PlayerController.cs')
require('public override void _PhysicsProcess(double delta)' in player_controller, 'player controller is not integrated on the physics tick')
freq=text(DOMAIN/'Architecture/SystemFrequencyPolicy.cs')
for token in ['PhysicsHz = 60.0','PlayerControllerHz = 60.0','NearbyAiHz = 10.0','DistantAiHz = 2.0','BackgroundEconomyMinimumHz = 0.2','BackgroundEconomyMaximumHz = 1.0']:
    require(token in freq, f'frequency policy missing {token}')
require('SystemFrequencyPolicy.NearbyAiHz' in text(SRC/'VerticalSlice/NpcFactionAgentNode.cs'), 'ground NPC decisions not gated at nearby AI frequency')
require('SystemFrequencyPolicy.NearbyAiHz' in text(SRC/'VerticalSlice/NpcShipNavigationNode.cs'), 'NPC ship decisions not gated at nearby AI frequency')
ecology=text(SRC/'VerticalSlice/EcologyRuntime.cs')
require('SystemFrequencyPolicy.NearbyAiHz' in ecology and 'SystemFrequencyPolicy.DistantAiHz' in ecology, 'ecology tiers do not use section 38 frequencies')
logger=text(SRC/'Infrastructure/StructuredGameLogger.cs')
require('PendingLines' in logger and 'FlushPending' in logger, 'telemetry is not batched')
require('SystemFrequencyPolicy.TelemetryFlushHz' in slice_arch, 'telemetry flush frequency not scheduled')
require('StructuredGameLogger.FlushPending();' in text(SRC/'Application/MainMenuController.cs'), 'main menu does not flush batched telemetry on scene exit')
require('StructuredGameLogger.FlushPending();' in text(SRC/'Developer/DeveloperWorkbenchController.cs'), 'developer workbench does not flush batched telemetry on scene exit')
require('SystemFrequencyPolicy.DefaultBackgroundEconomyHz' in slice_arch, 'background economy frequency not scheduled')

# SQL is confined to persistence/developer inspection code; never scene files.
# Persistence SQL must not interpolate runtime values into CommandText. DeveloperWorkbench has
# one read-only dynamic table-name query sourced from sqlite_master and quotes the identifier.
for path in (SRC/'Persistence').rglob('*.cs'):
    source = path.read_text(encoding='utf-8')
    require(re.search(r'CommandText\s*=\s*\$', source) is None,
            f'interpolated persistence SQL is forbidden: {path.relative_to(ROOT)}')
    require(re.search(r'CommandText\s*=\s*string\.(?:Format|Concat)', source) is None,
            f'formatted persistence SQL is forbidden: {path.relative_to(ROOT)}')
for path in SRC.rglob('*.cs'):
    source=path.read_text(encoding='utf-8')
    if re.search(r'\b(SELECT|INSERT|UPDATE|DELETE|PRAGMA)\b', source):
        rel=path.relative_to(SRC).as_posix()
        require(rel.startswith('Persistence/') or rel.startswith('Developer/'), f'SQL outside persistence/developer boundary: {rel}')
for scene in (ROOT/'src/Game.Client/Scenes').rglob('*.tscn'):
    source=scene.read_text(encoding='utf-8', errors='ignore')
    require(re.search(r'\b(SELECT|INSERT|UPDATE|DELETE|PRAGMA)\b', source) is None, f'SQL embedded in scene: {scene.relative_to(ROOT)}')

# No swallowed exceptions.
for path in SRC.rglob('*.cs'):
    source=path.read_text(encoding='utf-8')
    require(re.search(r'catch\s*(?:\([^)]*\))?\s*\{\s*\}', source, re.S) is None, f'empty catch suppresses exception: {path.relative_to(ROOT)}')

# Domain runtime/catalog/model classes must stay free of Godot Node inheritance.
for path in (SRC/'VerticalSlice').glob('*.cs'):
    if not any(token in path.stem for token in ('Runtime','Catalog','Domain')) or path.stem.endswith('Node'):
        continue
    source=path.read_text(encoding='utf-8')
    require(re.search(r'class\s+\w+\s*:\s*(?:Godot\.)?(?:Node|Node3D|CharacterBody3D|Control)\b', source) is None, f'domain class uses Godot Node as model: {path.relative_to(ROOT)}')

# No direct world generation in _Process methods (simple source-level guard).
for path in SRC.rglob('*.cs'):
    source=path.read_text(encoding='utf-8')
    for m in re.finditer(r'(?:public\s+override\s+void\s+_Process|void\s+_Process)\s*\([^)]*\)\s*\{', source):
        start=m.end(); depth=1; i=start
        while i < len(source) and depth:
            if source[i]=='{': depth+=1
            elif source[i]=='}': depth-=1
            i+=1
        body=source[start:i]
        require('CubeSphereMeshBuilder.Build(' not in body and 'GenerateSystem(' not in body and 'PlanetaryPoiPlanner.Plan(' not in body,
                f'world generation performed directly in _Process: {path.relative_to(ROOT)}')

# Project dependency direction is explicit and acyclic: Domain <- Application <- Client; tests -> Client.
domain_project=text(DOMAIN/'Game.Domain.csproj')
application_project=text(APPLICATION/'Game.Application.csproj')
game_project=csproj
require('<ProjectReference' not in domain_project, 'Game.Domain must not reference another project')
require('Godot' not in domain_project and 'Microsoft.Data.Sqlite' not in domain_project, 'Game.Domain project has forbidden infrastructure dependency')
require('../Game.Domain/Game.Domain.csproj' in application_project, 'Game.Application must reference Game.Domain')
require('Game.Client' not in application_project and 'Godot' not in application_project and 'Microsoft.Data.Sqlite' not in application_project, 'Game.Application has forbidden client/infrastructure dependency')
require('../Game.Domain/Game.Domain.csproj' in game_project and '../Game.Application/Game.Application.csproj' in game_project, 'Game.Client must compose Domain and Application projects')
test_project=text(ROOT/'tests/ProjectHorizon.Tests/ProjectHorizon.Tests.csproj')
require('Game.Client.csproj' in test_project, 'test project does not reference production project')

# Game/content IDs must never derive from CLR class names. Exception type names are allowed only
# in diagnostics; ID assignments themselves may not use GetType().Name/FullName or typeof/nameof.
for path in SRC.rglob('*.cs'):
    source = path.read_text(encoding='utf-8')
    require(re.search(r'\b(?:Id|ID)\s*=\s*[^;\n]*GetType\(\)\.(?:Name|FullName)', source) is None,
            f'game ID derived from CLR runtime type: {path.relative_to(ROOT)}')
    require(re.search(r'\b(?:Id|ID)\s*=\s*(?:nameof|typeof)\s*\(', source) is None,
            f'game ID derived from CLR symbol/type: {path.relative_to(ROOT)}')

# Serializable/persistent structures remain versioned by explicit contracts.
save_source=text(SRC/'Persistence/SaveDatabase.cs')
require('CurrentSchemaVersion' in save_source and 'CurrentContentVersion' in save_source, 'save structures lack explicit schema/content versions')
require((DOMAIN/'ProjectHorizonGenerator.cs').exists(), 'generator version contract missing')

# UI/application shell must not directly mutate inventory/crafting domain state.
for path in (SRC/'Application').glob('*.cs'):
    source=path.read_text(encoding='utf-8')
    for forbidden in ['TryConsumeInventory(', 'GrantInventory(', 'TryCraft(', 'TryDismantle(']:
        require(forbidden not in source, f'item/domain mutation in UI application code: {path.relative_to(ROOT)} -> {forbidden}')

if errors:
    print('TASK-142 SECTION-38 CONTRACT FAIL:')
    for error in errors:
        print(' -', error)
    sys.exit(1)

print('TASK-142 SECTION-38 CONTRACT PASS: nullable=1; warningsAsErrors=1; publicInterfaces=%d; asyncCancellation=1; typedEvents=11/11; eventBus=1; frequencies=60/60/10/2; backgroundEconomy=0.2-1Hz; telemetryBatched=1; sqlBoundary=1; exceptions=1; stableLayers=1; nodeDomainSeparation=1; noWorldgenInProcess=1; projectCycles=0; serializationVersioned=1; uiDomainSeparation=1.' % len(interfaces))
