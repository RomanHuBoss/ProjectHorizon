#!/usr/bin/env python3
"""CI JSON gate for Project Horizon PDF v2.0 §37.3.

The normative Industry Content v2 catalogs are validated against the checked-in
JSON Schema. Remaining runtime/testing JSON files are checked against a strict
repository contract for schema version, required top-level fields and obvious
empty/duplicate localization data. Every JSON file in the repository must parse.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

try:
    import jsonschema
except ImportError as exc:  # pragma: no cover - environment guard
    print("ERROR: jsonschema is required (python -m pip install 'jsonschema>=4.20,<5').", file=sys.stderr)
    raise SystemExit(2) from exc

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "src" / "Game.Client" / "Content"
TESTING = ROOT / "src" / "Game.Client" / "Testing"
SCHEMA_PATH = ROOT / "Technical_Specification" / "2.0" / "Project_Horizon_Industry_Content_Schema_v2.0.json"
INDUSTRY = {"items.json", "resources.json", "recipes.json", "stations.json", "technologies.json"}

# Required top-level contract for non-industry JSON. This is intentionally strict
# enough to catch accidental format drift while production loaders remain the
# authoritative semantic validators.
REQUIRED: dict[str, tuple[str, ...]] = {
    "base_construction.json": ("schemaVersion", "gridSizeMeters", "limits", "definitions"),
    "catalog_manifest.json": ("catalogVersion", "schemaVersion", "items", "worldResources", "recipes", "stations", "technologies"),
    "ecology.json": ("SchemaVersion", "WorldSeed", "RegionKey", "ActiveFaunaLimit", "SimplifiedFaunaLimit", "Biomes", "Flora", "Fauna"),
    "localization.en.json": ("schemaVersion", "strings"),
    "localization.ru.json": ("schemaVersion", "strings"),
    "npc_factions.json": ("schemaVersion", "worldSeed", "regionKey", "archetypes", "agents", "dialogues"),
    "planetary_pois.json": ("schemaVersion", "worldSeed", "regionKey", "minimumPoiSpacing", "definitions"),
    "player_survival.json": ("schemaVersion", "suitSlotLimit", "multitoolSlotLimit", "baseStats", "suitModules", "multitoolModules", "consumables", "environments"),
    "procedural_quests.json": ("schemaVersion", "worldSeed", "boardSize", "maximumActive", "objectiveTypes"),
    "ships.json": ("schemaVersion", "starterClassId", "classes", "systems", "modules"),
    "station_services.json": ("schemaVersion", "economyTypes", "factions", "markets", "dialogues", "npcs", "quests"),
    "golden-seeds.v1.json": ("schemaVersion", "generatorVersion", "systemCases", "poiFixture"),
    "section36-suite.json": ("schemaVersion", "task", "unitGroups", "saveScenarios", "loadScenarios", "coverage", "visualSmokeRequired"),
    "coverage-scope.json": ("schemaVersion", "areas"),
}


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def load(path: Path, failures: list[str]) -> Any:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:  # noqa: BLE001 - diagnostic gate
        fail(f"{path.relative_to(ROOT)}: parse error: {exc}", failures)
        return None


def main() -> int:
    failures: list[str] = []
    json_files = sorted(p for p in ROOT.rglob("*.json") if not any(part in {"bin", "obj", ".godot", "artifacts"} for part in p.parts))
    schema = load(SCHEMA_PATH, failures)
    if schema is None:
        return 1

    parsed: dict[Path, Any] = {}
    for path in json_files:
        parsed[path] = load(path, failures)

    for name in sorted(INDUSTRY):
        path = CONTENT / name
        data = parsed.get(path)
        if data is None:
            fail(f"missing normative industry catalog: {path.relative_to(ROOT)}", failures)
            continue
        try:
            jsonschema.Draft202012Validator(schema).validate(data)
        except jsonschema.ValidationError as exc:
            where = "/".join(str(x) for x in exc.absolute_path) or "<root>"
            fail(f"{path.relative_to(ROOT)}: schema failure at {where}: {exc.message}", failures)

    for path, data in parsed.items():
        if data is None or path == SCHEMA_PATH or path.name in INDUSTRY:
            continue
        required = REQUIRED.get(path.name)
        if required is None:
            # Technical/specification JSON may have its own schema and is still parse-checked.
            continue
        if not isinstance(data, dict):
            fail(f"{path.relative_to(ROOT)}: expected object root", failures)
            continue
        missing = [key for key in required if key not in data]
        if missing:
            fail(f"{path.relative_to(ROOT)}: missing top-level fields: {', '.join(missing)}", failures)

    # Localization parity is a JSON-level invariant even though TASK-132 has a deeper gate.
    en = parsed.get(CONTENT / "localization.en.json")
    ru = parsed.get(CONTENT / "localization.ru.json")
    if isinstance(en, dict) and isinstance(ru, dict):
        en_strings = en.get("strings")
        ru_strings = ru.get("strings")
        if not isinstance(en_strings, dict) or not isinstance(ru_strings, dict):
            fail("localization catalogs must contain object 'strings'", failures)
        else:
            if set(en_strings) != set(ru_strings):
                fail("localization key sets differ between en and ru", failures)
            blanks = [key for key, value in {**en_strings, **ru_strings}.items() if not isinstance(value, str) or not value.strip()]
            if blanks:
                fail(f"localization contains blank/non-string values: {', '.join(blanks[:8])}", failures)

    status = "PASS" if not failures else "FAIL"
    print(
        f"TASK-140 JSON CONTRACT {status}: json={len(json_files)}; parsed={sum(v is not None for v in parsed.values())}; "
        f"industrySchema={len(INDUSTRY)}/5; localizationParity={0 if failures else 1}."
    )
    for failure in failures:
        print("ERROR:", failure)
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
