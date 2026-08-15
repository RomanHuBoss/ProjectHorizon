#!/usr/bin/env python3
"""Verify Project Horizon PDF v2.0 section 36.5 coverage thresholds."""
from __future__ import annotations
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCOPE_PATH = ROOT / "tests" / "coverage-scope.json"


def normalize(value: str) -> str:
    return value.replace("\\", "/").lstrip("./")


def find_report(results_dir: Path) -> Path:
    reports = sorted(results_dir.rglob("coverage.cobertura.xml"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not reports:
        raise FileNotFoundError(f"coverage.cobertura.xml not found below {results_dir}")
    return reports[0]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--results-dir", default=str(ROOT / "artifacts" / "test-results"))
    parser.add_argument("--coverage", default="")
    args = parser.parse_args()
    report = Path(args.coverage).resolve() if args.coverage else find_report(Path(args.results_dir).resolve())
    scope = json.loads(SCOPE_PATH.read_text(encoding="utf-8"))
    tree = ET.parse(report)
    line_hits: dict[tuple[str, int], int] = {}
    for cls in tree.findall(".//class"):
        filename = normalize(cls.attrib.get("filename", ""))
        for line in cls.findall("./lines/line"):
            try:
                number = int(line.attrib["number"])
                hits = int(float(line.attrib.get("hits", "0")))
            except (KeyError, ValueError):
                continue
            key = (filename, number)
            line_hits[key] = max(line_hits.get(key, 0), hits)

    failures: list[str] = []
    summary: list[str] = []
    for area, config in scope["areas"].items():
        suffixes = [normalize(path) for path in config["files"]]
        selected = {
            key: hits for key, hits in line_hits.items()
            if any(key[0].endswith(suffix) for suffix in suffixes)
        }
        seen_suffixes = {
            suffix for suffix in suffixes
            if any(filename.endswith(suffix) for filename, _ in selected)
        }
        missing = [suffix for suffix in suffixes if suffix not in seen_suffixes]
        valid = len(selected)
        covered = sum(1 for hits in selected.values() if hits > 0)
        ratio = covered / valid if valid else 0.0
        minimum = float(config["minimumLineCoverage"])
        summary.append(f"{area}={ratio * 100:.2f}%({covered}/{valid},min={minimum*100:.0f}%)")
        if missing:
            failures.append(f"{area}: coverage report missing scoped files: {', '.join(missing)}")
        if ratio + 1e-12 < minimum:
            failures.append(f"{area}: {ratio*100:.2f}% < {minimum*100:.0f}%")

    status = "PASS" if not failures else "FAIL"
    print(f"TASK-138 COVERAGE {status}: " + "; ".join(summary) + f"; report={report}")
    for failure in failures:
        print("ERROR:", failure)
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
