#!/usr/bin/env python3
"""Static regression gate for TASK-180 production procedural visual language."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8", errors="replace")

def need(condition: bool, message: str, failures: list[str]) -> None:
    if not condition:
        failures.append(message)

f: list[str] = []
version = text("VERSION").strip()
ship = text("src/Game.Client/Scenes/Ship/ArcadeShip.tscn")
station = text("src/Game.Client/Scenes/VerticalSlice/SalvageRepairSlice.tscn")
npc = text("src/Game.Client/Scripts/VerticalSlice/NpcShipNavigationNode.cs")
globe = text("src/Game.Client/Scripts/VerticalSlice/DetailedPlanetGlobeNode.cs")
star = text("src/Game.Client/Scripts/VerticalSlice/StarSystemSimulationNode.cs")
acceptance = text("src/Game.Client/Scripts/VerticalSlice/ProductionVisualLanguageAcceptance.cs")
live = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceProductionVisualLanguage.cs")
slice_cs = text("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs")
tests = text("tests/ProjectHorizon.Tests/Unit/ProductionVisualLanguageTests.cs")
readme = text("README.md")
changelog = text("CHANGELOG.md")
status = text("REQUIREMENTS_STATUS.md")
quality_sh = text("tools/run-section37-quality.sh")
quality_cmd = text("tools/run-section37-quality.cmd")
ci = text(".github/workflows/ci.yml")
release = text(".github/workflows/release.yml")

need(version in {"0.1.0-alpha.180", "0.1.0-alpha.180.1", "0.1.0-alpha.180.2", "0.1.0-alpha.180.3", "0.1.0-alpha.182", "0.1.0-alpha.184", "0.1.0-alpha.184.1", "0.1.0-alpha.186", "0.1.0-alpha.188", "0.1.0-alpha.190"}, "VERSION must be alpha.180 or alpha.180.1", f)
for node in (
    'name="LeftChine"', 'name="RightChine"', 'name="DorsalSpine"',
    'name="CockpitInterior"', 'name="InstrumentPanel"', 'name="PrimaryDisplay"',
    'name="LeftConsole"', 'name="RightConsole"', 'name="LeftDisplay"',
    'name="RightDisplay"', 'name="CanopyLeft"', 'name="CanopyRight"', 'name="CanopyTop"'):
    need(node in ship, f"player ship visual node missing: {node}", f)
need('Material_display' in ship and 'emission_energy_multiplier = 2.8' in ship,
     "cockpit emissive display material missing", f)
need(ship.count('[node name="CollisionShape3D" type="CollisionShape3D" parent="."]') == 1,
     "player ship collision contract changed", f)
for node in ('name="VisualDetail"', 'name="HabitationRing"', 'name="CentralHub"',
             'name="PortRadiator"', 'name="StarboardRadiator"',
             'name="UpperAntenna"', 'name="LowerAntenna"'):
    need(node in station, f"orbital station visual node missing: {node}", f)
visual_detail_block = station.split('[node name="VisualDetail"', 1)[1].split('[node name="DockLight"', 1)[0]
need('CollisionShape3D' not in visual_detail_block,
     "TASK-180 station VisualDetail must remain collision-free", f)
need('ProductionVisualPartCount = 9' in npc and
     'Name = "Canopy"' in npc and 'Name = "LeftNacelle"' in npc and
     'Name = "RightNacelle"' in npc and 'production_visual_profile' in npc,
     "NPC compound production silhouette missing", f)
need('ProductionTerrainMaterialVariants = 6' in globe and
     'BuildTerrainMaterial(definition.Archetype, faceIndex)' in globe and
     'BuildTerrainVertexColor' in globe and 'VertexColorUseAsAlbedo = true' in globe and
     'surface.SetColor' in globe and 'roughness: 0.28f' in globe and 'roughness: 0.90f' in globe,
     "detailed globe seam-safe material breakup/shell profiles missing", f)
need('BuildSemanticMaterial' in star and ('EmissionEnergyMultiplier = 3.2f' in star or 'EmissionEnergyMultiplier = 7.5f' in star) and
     'ProductionVisualProfileCount' in star and 'DetailedPlanetTerrainMaterialVariants' in star and
     'TerrainMaterialInstanceCount' in globe,
     "semantic star-system PBR profile missing", f)
need('MinimumPlayerExteriorParts = 11' in acceptance and
     'MinimumCockpitDetailParts = 9' in acceptance and
     'MinimumStationDetailParts = 6' in acceptance and
     'RequiredPlanetMaterialVariants = 6' in acceptance,
     "TASK-180 model acceptance thresholds missing", f)
need('PrintProductionVisualLanguageReady' in live and
     'RunProductionVisualLanguageAcceptance' in live and 'RUNNING' in live and
     '_productionVisualLanguageAcceptancePassed' in live and
     '_npcShipNavigationNodes.Min' in live and 'visualOnlyDetails' in live,
     "TASK-180 live acceptance missing", f)
need('RunProductionVisualLanguageAcceptance();' in slice_cs and
     '_productionVisualLanguageAcceptancePassed == true' in slice_cs and
     'TASK-180 (F5)' in slice_cs,
     "TASK-180 is not wired into final F5 gate", f)
need('Task180_ProductionVisualContractPassesAtDeclaredBudgets' in tests and
     'Task180_RejectsUnderBudgetVisualContracts' in tests and
     'Task180_RejectsGameplayCollisionMutation' in tests,
     "TASK-180 xUnit coverage missing", f)
need('TASK-180' in readme and '0.1.0-alpha.180' in changelog and 'TASK-180' in status,
     "TASK-180 docs/status evidence missing", f)
validator = 'validate-task180-production-visual-language.py'
need(validator in quality_sh and validator in quality_cmd and validator in ci and validator in release,
     "TASK-180 validator is not enforced in local/CI/release gates", f)

if f:
    print("TASK-180 PRODUCTION VISUAL LANGUAGE CONTRACT FAIL:")
    for item in f:
        print(f"- {item}")
    sys.exit(1)

print(
    "TASK-180 PRODUCTION VISUAL LANGUAGE CONTRACT PASS: "
    "playerShip=compound; cockpit=interior; station=visual-detail; npcShip=compound; "
    "planetMaterials=6; semanticPBR=1; collisionMutation=0; f5=1; xunit=1."
)
