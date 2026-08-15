#!/usr/bin/env python3
"""Project Horizon shipping localization contract validator (TASK-132).

Validates the RU/EN catalog pair, key-only content references, shipping-scene
text keys, and a narrow set of player-facing source sinks. Development
prototype scenes and acceptance/log diagnostics are intentionally outside the
shipping UI contract.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTENT = ROOT / "src" / "Game.Client" / "Content"
SCRIPTS = ROOT / "src" / "Game.Client" / "Scripts"
SHIPPING_SCENE = ROOT / "src" / "Game.Client" / "Scenes" / "VerticalSlice" / "SalvageRepairSlice.tscn"

LOCALES = {
    "en": CONTENT / "localization.en.json",
    "ru": CONTENT / "localization.ru.json",
}
KEY_FIELDS = {
    "localizationkey",
    "displaynamekey",
    "greetingkey",
    "farewellkey",
    "textkey",
    "consequencekey",
    "namepoolkeys",
}
FORBIDDEN_CONTENT_FIELDS = {
    "displayNameEn", "displayNameRu", "DisplayNameEn", "DisplayNameRu",
    "nameEn", "nameRu", "greetingEn", "greetingRu", "farewellEn", "farewellRu",
    "textEn", "textRu", "consequenceEn", "consequenceRu", "namePool",
}
KEY_ONLY_CONTENT = [
    CONTENT / "station_services.json",
    CONTENT / "npc_factions.json",
    CONTENT / "ecology.json",
]
SHIPPING_SOURCE_ROOTS = [
    SCRIPTS / "Application",
    SCRIPTS / "VerticalSlice",
]
SHIPPING_SOURCE_FILES = [
    SCRIPTS / "Player" / "PlayerController.cs",
]

# Phrases that were historically visible in the shipping UI and therefore
# must not reappear as raw literals. Internal identifiers and TASK diagnostics
# are deliberately not listed here.
LEGACY_PLAYER_PHRASES = {
    "ship management closed",
    "base construction closed",
    "discovery catalog closed",
    "station services closed",
    "recipe selector closed",
    "ecology catalogue closed",
    "mission journal closed",
    "planet map closed",
    "aimed at ",
    "no target in interaction range",
    "Production network: unavailable",
    "Galaxy navigation: unavailable",
    "Star system: unavailable",
    "Aerial navigation: unavailable",
    "NPC navigation: unavailable",
    "NPC/Factions: unavailable",
    "Missions: unavailable",
    "Ecology: unavailable",
    "Pending recipes: none",
    "Objective: collect salvage",
    "Autosave: unavailable",
    "VERTICAL SLICE 1 - SALVAGE",
    "WASD/Space - move",
}


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def load_catalog(path: Path) -> dict[str, str]:
    doc = json.loads(path.read_text(encoding="utf-8"))
    if doc.get("schemaVersion") != 1 or not isinstance(doc.get("strings"), dict):
        raise ValueError(f"invalid localization schema: {path}")
    return {str(k): str(v) for k, v in doc["strings"].items()}


def walk_key_fields(value, found: set[str], field_name: str | None = None) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized = key.lower()
            if normalized in KEY_FIELDS:
                walk_key_fields(child, found, key)
            else:
                walk_key_fields(child, found, None)
        return
    if isinstance(value, list):
        for child in value:
            walk_key_fields(child, found, field_name)
        return
    if field_name is not None and isinstance(value, str) and value.strip():
        found.add(value.strip())


def collect_field_names(value, found: set[str]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            found.add(key)
            collect_field_names(child, found)
    elif isinstance(value, list):
        for child in value:
            collect_field_names(child, found)


def shipping_sources() -> list[Path]:
    files: set[Path] = set()
    for root in SHIPPING_SOURCE_ROOTS:
        files.update(root.rglob("*.cs"))
    files.update(path for path in SHIPPING_SOURCE_FILES if path.exists())
    return sorted(files)


def main() -> int:
    failures: list[str] = []
    catalogs = {locale: load_catalog(path) for locale, path in LOCALES.items()}
    en_keys = set(catalogs["en"])
    ru_keys = set(catalogs["ru"])
    if en_keys != ru_keys:
        fail(f"catalog key parity failed: en-only={len(en_keys-ru_keys)} ru-only={len(ru_keys-en_keys)}", failures)
    blanks = [(locale, key) for locale, catalog in catalogs.items() for key, value in catalog.items() if not value.strip()]
    if blanks:
        fail(f"blank translations: {len(blanks)}", failures)
    if len(en_keys) < 1000:
        fail(f"unexpectedly small shipping catalog: {len(en_keys)} keys", failures)

    content_keys: set[str] = set()
    for path in sorted(CONTENT.glob("*.json")):
        if path.name.startswith("localization."):
            continue
        walk_key_fields(json.loads(path.read_text(encoding="utf-8")), content_keys)
    missing_content = sorted(content_keys - en_keys)
    if missing_content:
        fail("missing content localization keys: " + ", ".join(missing_content[:20]), failures)

    # Dynamic display tokens are built from validated runtime/content enums.
    dynamic_keys: set[str] = set()
    ecology = json.loads((CONTENT / "ecology.json").read_text(encoding="utf-8"))
    for definition in ecology.get("Flora", []):
        dynamic_keys.add("ui.ecology.token." + str(definition.get("Shape", "")).lower())
        dynamic_keys.add("ui.ecology.token." + str(definition.get("Hazard", "")).lower())
    for definition in ecology.get("Fauna", []):
        for field in ("MovementMode", "BodyPlan", "Diet"):
            dynamic_keys.add("ui.ecology.token." + str(definition.get(field, "")).lower())
    base = json.loads((CONTENT / "base_construction.json").read_text(encoding="utf-8"))
    for definition in base.get("definitions", []):
        dynamic_keys.add("ui.base.category." + str(definition.get("category", "")).lower())
    ships = json.loads((CONTENT / "ships.json").read_text(encoding="utf-8"))
    for definition in ships.get("modules", []):
        dynamic_keys.add("ui.ship.slot." + str(definition.get("slotType", "")).lower())
    dynamic_keys.update({
        "ui.galaxy.star.red_dwarf", "ui.galaxy.star.orange_dwarf",
        "ui.galaxy.star.yellow_star", "ui.galaxy.star.white_star",
        "ui.galaxy.star.blue_star", "ui.galaxy.star.binary_decorative",
    })
    dynamic_keys.update("ui.galaxy.economy." + token for token in (
        "extractive", "industrial", "scientific", "commercial", "agricultural", "frontier"))
    dynamic_keys.update("ui.galaxy.planet." + token for token in (
        "temperate", "desert", "frozen", "volcanic", "toxic",
        "radioactive", "barren", "oceanic", "gas_giant"))
    missing_dynamic = sorted(key for key in dynamic_keys if key and key not in en_keys)
    if missing_dynamic:
        fail("missing dynamic localization keys: " + ", ".join(missing_dynamic[:30]), failures)

    forbidden_hits: list[str] = []
    for path in KEY_ONLY_CONTENT:
        doc = json.loads(path.read_text(encoding="utf-8"))
        fields: set[str] = set()
        collect_field_names(doc, fields)
        bad = sorted(fields & FORBIDDEN_CONTENT_FIELDS)
        if bad:
            forbidden_hits.append(f"{path.name}: {','.join(bad)}")
    if forbidden_hits:
        fail("bilingual/raw content fields remain: " + "; ".join(forbidden_hits), failures)

    source_files = shipping_sources()
    ui_literal = re.compile(r'"(ui\.[A-Za-z0-9_.-]+)"')
    source_ui_keys: set[str] = set()
    legacy_hits: list[str] = []
    sink_hits: list[str] = []
    sink_patterns = [
        re.compile(r'(?:_status|\w*Feedback|\bmessage|\bresult|\bdescription)\s*=\s*"([^"\\]+)"'),
        re.compile(r'\.Text\s*=\s*"([^"\\]+)"'),
        re.compile(r'Close\w+\(\s*"([^"\\]+)"'),
    ]
    for path in source_files:
        text = path.read_text(encoding="utf-8", errors="replace")
        source_ui_keys.update(key for key in ui_literal.findall(text) if not key.endswith("."))
        for phrase in LEGACY_PLAYER_PHRASES:
            if phrase in text:
                legacy_hits.append(f"{path.relative_to(ROOT)}: {phrase}")
        for line_no, line in enumerate(text.splitlines(), 1):
            if any(token in line for token in ("GD.Print", "GD.Push", "throw new", "TASK-", "res://", "user://")):
                continue
            for pattern in sink_patterns:
                match = pattern.search(line)
                if not match:
                    continue
                raw = match.group(1).strip()
                if not raw or raw.startswith("ui.") or raw in {"+", "-", "—", "?", "*"}:
                    continue
                # Internal machine tokens are permitted; natural-language sinks are not.
                if re.search(r"\s", raw) and re.search(r"[A-Za-zА-Яа-я]{3,}", raw):
                    sink_hits.append(f"{path.relative_to(ROOT)}:{line_no}: {raw}")
    if legacy_hits:
        fail("legacy player-facing literals remain: " + " | ".join(legacy_hits[:20]), failures)
    if sink_hits:
        fail("raw player-facing source sinks remain: " + " | ".join(sink_hits[:20]), failures)

    missing_source = sorted(source_ui_keys - en_keys)
    if missing_source:
        fail("missing source ui.* keys: " + ", ".join(missing_source[:30]), failures)

    scene_text = SHIPPING_SCENE.read_text(encoding="utf-8")
    scene_values = re.findall(r'^(?:text|placeholder_text|tooltip_text) = "([^"]*)"', scene_text, re.MULTILINE)
    scene_keys = {value for value in scene_values if value.startswith("ui.")}
    raw_scene = [value for value in scene_values if value and not value.startswith("ui.") and value not in {"+", "-", "—", "?", "*"}]
    if raw_scene:
        fail("raw shipping-scene text: " + ", ".join(raw_scene[:20]), failures)
    missing_scene = sorted(scene_keys - en_keys)
    if missing_scene:
        fail("missing shipping-scene keys: " + ", ".join(missing_scene), failures)

    status = "PASS" if not failures else "FAIL"
    print(
        f"TASK-132 LOCALIZATION CONTRACT {status}: locales={len(catalogs)}; "
        f"keys={len(en_keys)}; parity={int(en_keys == ru_keys)}; blanks={len(blanks)}; "
        f"contentKeys={len(content_keys)}; dynamicKeys={len(dynamic_keys)}; sourceUiKeys={len(source_ui_keys)}; "
        f"sceneKeys={len(scene_keys)}; keyOnlyContent={int(not forbidden_hits)}; "
        f"sourceSinks={len(sink_hits)}; legacyLiterals={len(legacy_hits)}."
    )
    for item in failures:
        print("ERROR:", item)
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
