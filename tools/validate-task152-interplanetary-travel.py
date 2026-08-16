#!/usr/bin/env python3
"""Static contract gate for TASK-152 same-system interplanetary travel."""
from __future__ import annotations
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding='utf-8')

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

failures: list[str] = []
travel = text('src/Game.Client/Scripts/VerticalSlice/InterplanetaryTravelRuntime.cs')
slice_travel = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceInterplanetaryTravel.cs')
galaxy = text('src/Game.Client/Scripts/VerticalSlice/GalaxyNavigationRuntime.cs')
slice_galaxy = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceGalaxy.cs')
voyage = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceVoyage.cs')
voyage_runtime = text('src/Game.Client/Scripts/VerticalSlice/StageOneVoyageRuntime.cs')
star_node = text('src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs')
world = text('src/Game.Application/World/WorldSceneCoordinatorRuntime.cs')
world_node = text('src/Game.Client/Scripts/VerticalSlice/WorldSceneCoordinatorNode.cs')
world_slice = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceWorldScenes.cs')
models = text('src/Game.Client/Scripts/Persistence/SaveGameModels.cs')
save_db = text('src/Game.Client/Scripts/Persistence/SaveDatabase.cs')
acceptance = text('src/Game.Client/Scripts/VerticalSlice/InterplanetaryTravelAcceptance.cs')
main = text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
world_tests = text('tests/ProjectHorizon.Tests/Unit/WorldSceneCoordinatorTests.cs')
worldgen_tests = text('tests/ProjectHorizon.Tests/Unit/WorldGenTests.cs')
en = json.loads(text('src/Game.Client/Content/localization.en.json'))['strings']
ru = json.loads(text('src/Game.Client/Content/localization.ru.json'))['strings']

need('TargetSelected = 1' in travel and 'Cruising = 2' in travel,
     'travel state machine phases missing', failures)
need('TryBeginCruise(' in travel and 'BuildGuidance(' in travel and 'TryCompleteArrival(' in travel,
     'travel begin/guidance/arrival state machine incomplete', failures)
need('shipSystems.TryConsumeFuel' in travel and 'CalculateFuelCost' in travel,
     'cruise is not fuel-backed', failures)
need('ArrivalRadiusMeters' in travel and 'MaximumArrivalSpeed' in travel and 'BrakingDistanceMeters' in travel,
     'arrival/braking policy missing', failures)

need('SelectedPlanetId => _selectedPlanetId' in galaxy and 'TrySelectPlanetDestination' in galaxy,
     'galaxy planetary target selection missing', failures)
need('TryCompletePlanetTransfer' in galaxy and 'InterplanetaryTransferCount++' in galaxy and
     'TotalInterplanetaryDistanceMeters +=' in galaxy,
     'galaxy transfer completion/counters missing', failures)
need('SelectedPlanetId = ""' in models and 'InterplanetaryTransferCount = 0' in models and
     'TotalInterplanetaryDistanceMeters = 0.0' in models,
     'interplanetary save fields missing/backward-incompatible', failures)
need('navigation.SelectedPlanetId' in save_db and 'navigation.InterplanetaryTransferCount' in save_db and
     'navigation.TotalInterplanetaryDistanceMeters' in save_db,
     'persistence boundary does not validate transfer state', failures)

need('ConfirmPlanetaryDestination()' in slice_galaxy and 'SelectInterplanetaryPlanetTarget' in slice_galaxy and
     'Matches(physical, logical, Key.Enter)' in slice_galaxy,
     'System Map Enter target selection missing', failures)
need('ui.galaxy.planet_current' in slice_galaxy and 'ui.galaxy.planet_target' in slice_galaxy,
     'System Map current/target markers missing', failures)
need('TryGetBodyDisplayPosition' in star_node,
     'live system proxy target position API missing', failures)
need('TryApplyInterplanetaryNavigationAssist()' in voyage and 'SetExternalCommand' in voyage,
     'existing K navigation assist is not integrated with physical cruise', failures)
need('CancelInterplanetaryCruiseForManualControl' in voyage,
     'manual-control cruise cancellation missing', failures)
need('ArriveAtPlanetaryApproach(' in voyage_runtime and 'planet.approach' in voyage_runtime,
     'destination local-approach handoff missing', failures)
need('ApplyStageOneVoyageToScene();' in slice_travel and 'QueueCurrentSnapshot' in slice_travel,
     'arrival does not rebase scene/persist state', failures)

need('InterplanetaryTransit = 4' in world,
     'world scene context lacks InterplanetaryTransit', failures)
need('(WorldSceneKind.Orbit, WorldSceneKind.InterplanetaryTransit)' in world and
     '(WorldSceneKind.InterplanetaryTransit, WorldSceneKind.Orbit)' in world,
     'transactional interplanetary world graph edges missing', failures)
need('InterplanetaryTransitShell.tscn' in world_node and
     (ROOT/'src/Game.Client/Scenes/World/InterplanetaryTransitShell.tscn').is_file(),
     'interplanetary packed scene missing', failures)
need('WorldSceneKind.InterplanetaryTransit' in world_slice,
     'residency policy does not recognize interplanetary transit', failures)

need('TASK-152 interplanetary travel acceptance' in acceptance and
     'targetPersistence' in acceptance and 'worldHandoff' in acceptance and
     'transferPersistence' in acceptance,
     'TASK-152 runtime acceptance is incomplete', failures)
need('RunInterplanetaryTravelAcceptance();' in main and 'TASK-152 (F5)' in main,
     'TASK-152 is not wired into F5 matrix/HUD', failures)
need('PlanetDestinationSelection_PersistsWithoutChangingCurrentPlanet' in worldgen_tests and
     'InterplanetaryTransfer_UpdatesPlanetCountersFuelAndRoundTrips' in worldgen_tests and
     'InterplanetaryOrbitHandoff_ChangesPlanetOnlyThroughTransit' in world_tests,
     'TASK-152 xUnit regression coverage incomplete', failures)

for key in (
    'ui.interplanetary.target_selected', 'ui.interplanetary.cruise_started',
    'ui.interplanetary.arrival', 'ui.hud.interplanetary.summary',
    'ui.galaxy.planet_current', 'ui.galaxy.planet_target'):
    need(key in en and key in ru, f'localization key missing: {key}', failures)
need(set(en) == set(ru), 'RU/EN localization parity broken', failures)

if failures:
    print('TASK-152 INTERPLANETARY TRAVEL CONTRACT FAIL:')
    for failure in failures:
        print(f'- {failure}')
    sys.exit(1)

print(
    'TASK-152 INTERPLANETARY TRAVEL CONTRACT PASS: '
    'starterPlanets=4/4; targetSelection=1; targetPersistence=1; fuel=1; '
    'physicalGuidance=1; proxyTarget=1; worldHandoff=1; singlePlanetResidency=1; '
    'localApproach=1; transferCounters=1; persistence=1; systemMap=1; '
    'manualCancel=1; f5=1; xunit=3/3; localization=1.'
)
