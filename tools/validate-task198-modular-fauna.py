#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT=Path(__file__).resolve().parents[1]
fail=[]
def text(path): return (ROOT/path).read_text(encoding='utf-8')
def need(cond,msg):
    if not cond: fail.append(msg)

version=text('VERSION').strip()
need(version in {'0.1.0-alpha.198','0.1.0-alpha.200','0.1.0-alpha.202','0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216','0.1.0-alpha.218','0.1.0-alpha.220'}, f'VERSION must preserve alpha.198 or later accepted revision, got {version}')
body=text('src/Game.Client/Scripts/VerticalSlice/FaunaBodyPlanRuntime.cs')
behavior=text('src/Game.Client/Scripts/VerticalSlice/FaunaBehaviorRuntime.cs')
statistical=text('src/Game.Client/Scripts/VerticalSlice/FaunaStatisticalSimulationRuntime.cs')
flock=text('src/Game.Client/Scripts/VerticalSlice/FaunaFlockRuntime.cs')
node=text('src/Game.Client/Scripts/VerticalSlice/EcologyFaunaNode.cs')
runtime=text('src/Game.Client/Scripts/VerticalSlice/EcologyRuntime.cs')
slice_ecology=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceEcology.cs')
slice_nav=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceNpcNavigation.cs')
slice198=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceFaunaModular.cs')
acceptance=text('src/Game.Client/Scripts/VerticalSlice/FaunaModularAcceptance.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/FaunaModularTests.cs')

# §12.1 six fixed skeleton families + compatible deterministic modules.
for plan in ('Biped','Quadruped','Hexapod','Flying','Aquatic','Crawler'):
    need(f'["{plan}"] = Define(' in body, f'fixed skeleton family missing: {plan}')
need('skeleton.biped.v1' in body and 'skeleton.crawler.v1' in body, 'versioned fixed skeleton IDs missing')
for token in ('HeadModule','TorsoModule','LimbModule','TailModule','HornModule','ShellModule'):
    need(token in body, f'procedural morphology module missing: {token}')
need('EcologyPlanner.StableHash' in body and 'TASK-198' in body, 'deterministic per-instance morphology seed missing')
need('IsCompatible' in body and 'StartsWith(prefix' in body, 'cross-skeleton compatibility guard missing')
need('WidthScale is >= 0.88 and <= 1.12' in body and 'HeightScale is >= 0.90 and <= 1.10' in body, 'bounded morphology proportions missing')
need('FaunaBodyPlanRuntime.Build(definition, spawn.InstanceId)' in node, 'live fauna not bound to modular morphology runtime')
need('skeleton_family' in node and 'fixed_joint_count' in node, 'fixed skeleton metadata not bound to live visuals')

# §12.2 HFSM + utility + steering + ground navmesh + boids.
for layer in ('Survival','Territory','Needs','Social','Awareness','Ambient'):
    need(layer in behavior, f'HFSM layer missing: {layer}')
need('ScoreBehaviors' in behavior and 'FaunaUtilityScore' in behavior, 'utility scoring missing')
for state in ('Idle','Wander','Graze','Drink','Sleep','Investigate','Flee','Threaten','Attack','ReturnToTerritory','FollowGroup'):
    need(f'"{state}"' in behavior or f'"{state}"' in acceptance, f'behavior state missing: {state}')
need('NavigationAgent3D' in node and 'EnableGroundNavigation' in node and 'GetNextPathPosition' in node, 'ground navmesh path missing')
need('fauna.EnableGroundNavigation(_npcNavigationSurface)' in slice_nav, 'ground fauna not attached to shared navigation map')
need('Compute(' in flock and 'Separation' in flock and 'Cohesion' in flock and 'Alignment' in flock, 'boids steering components missing')
need('UpdateFaunaFlocking' in slice_ecology and 'SetFlockSteering' in slice_ecology, 'live group boids update missing')
need('ApplyFlyingSteering' in node and 'AerialSteeringRuntime' in node, 'existing aerial steering regression lost')

# §12.3 10Hz / 2-5Hz / statistical far / per-frame interpolation.
need('NearFrequencyHz = SystemFrequencyPolicy.NearbyAiHz' in behavior, 'near 10Hz section-38 tier missing')
need('MidHighFrequencyHz = 5.0' in behavior and 'MidLowFrequencyHz = SystemFrequencyPolicy.DistantAiHz' in behavior, 'mid 2-5Hz section-38 tiers missing')
need('FarStatisticalFrequencyHz = 0.5' in behavior, 'far statistical cadence missing')
need('FaunaSimulationTier.Statistical' in behavior and 'GetDecisionFrequencyHz' in behavior, 'distance tier classifier missing')
need('FaunaStatisticalSimulationRuntime' in runtime and 'FarFaunaSnapshot' in runtime, 'simplified fauna not bound to statistical simulator')
need('GroupBy(spawn => spawn.FaunaId' in statistical and 'Population' in statistical, 'species-level statistical aggregation missing')
need('public override void _Process(double delta)' in node and '_visualInterpolationFrames++' in node, 'per-frame visual interpolation missing')

# Acceptance/release.
need('FaunaModularAcceptanceRunner' in acceptance and 'bodyPlans={BodyPlans}/6' in acceptance, 'TASK-198 acceptance runner missing')
need('RunFaunaModularAcceptance();' in main and 'TASK-198 (F5)' in main and '_faunaModularAcceptancePassed == true' in main, 'TASK-198 F5/final gate missing')
need('TASK-198 modular fauna READY' in slice198, 'TASK-198 READY runtime line missing')
need('SixBodyPlansHaveFixedCompatibleSkeletonFamilies' in tests and 'SimulationTiersAreTenFiveTwoThenStatistical' in tests and 'BoidsProducesGroupSteeringForCompatibleNeighbors' in tests, 'TASK-198 xUnit coverage incomplete')
need('TASK-198' in text('README.md'), 'README TASK-198 section missing')
need('## [0.1.0-alpha.198]' in text('CHANGELOG.md'), 'CHANGELOG alpha.198 section missing')
need('TASK-198' in text('REQUIREMENTS_STATUS.md'), 'requirements journal TASK-198 missing')
need((ROOT/'docs/MODULAR_FAUNA_RUNTIME.md').exists(), 'TASK-198 runtime document missing')

if fail:
    print('TASK-198 MODULAR FAUNA CONTRACT FAIL:')
    for e in fail: print('ERROR:',e)
    sys.exit(1)
print('TASK-198 MODULAR FAUNA CONTRACT PASS: bodyPlans=6-fixed-skeletons; modules=head+torso+limbs+tail+horns+shell; compatibility=skeleton-family-only; ai=hfsm+utility; movement=steering+navmesh+boids+aerial; tiers=10/5/2/statistical; interpolation=per-frame; f5=1; xunit=1.')
