#!/usr/bin/env python3
"""Static contract gate for Project Horizon PDF v2.0 §37 build/version control."""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
failures: list[str] = []


def need(condition: bool, message: str) -> None:
    if not condition:
        failures.append(message)


def read(path: str) -> str:
    p = ROOT / path
    need(p.is_file(), f"missing file: {path}")
    return p.read_text(encoding="utf-8", errors="replace") if p.is_file() else ""

ci = read(".github/workflows/ci.yml")
release = read(".github/workflows/release.yml")
exports = read("src/Game.Client/export_presets.cfg")
bootstrap = read("tools/ci/bootstrap-godot.sh")
export_script = read("tools/ci/export-project.sh")
package = read("tools/ci/package-release.py")
json_gate = read("tools/validate-json-content.py")
version_gate = read("tools/ci/verify-version.py")
policy = read("docs/BUILD_AND_RELEASE.md")
props = read("Directory.Build.props")
attrs = read(".gitattributes")
ignore = read(".gitignore")
changelog = read("CHANGELOG.md")
version = read("VERSION").strip()

for branch in ["main", "develop", "feature/*", "fix/*", "release/*"]:
    need(branch in policy, f"branch policy missing: {branch}")
need("main" in ci and "develop" in ci and "release/**" in ci, "CI push branches are incomplete")
need("pull_request:" in ci, "CI must run for pull requests")

for marker in [
    "dotnet restore", "dotnet build", "-warnaserror", "ContinuousIntegrationBuild=true",
    "dotnet test", "validate-json-content.py", "verify-section36-coverage.py",
    "Persistence.PersistenceTests", "export-project.sh debug",
]:
    need(marker in ci, f"CI stage missing: {marker}")
need("TreatWarningsAsErrors" in props, "Directory.Build.props lacks CI warnings-as-errors policy")

need('name="Windows Desktop"' in exports and 'platform="Windows Desktop"' in exports, "Windows export preset missing")
need('name="Linux"' in exports and 'platform="Linux"' in exports, "Linux export preset missing")
need('name="Windows Desktop Compatibility"' in exports, "Windows Compatibility export preset missing")
need('name="Linux Compatibility"' in exports, "Linux Compatibility export preset missing")
need(exports.count('binary_format/architecture="x86_64"') == 4, "all four desktop export presets must be x86_64")
need(exports.count('custom_features="compatibility"') == 2, "Compatibility feature tag missing from fallback presets")
need("--headless" in export_script and "--export-debug" not in export_script, "export script must use generic headless debug/release mode")
need('mode="--export-$CONFIG"' in export_script, "export script does not select debug/release CLI mode")
need("godotengine/godot-builds/releases/download" in bootstrap, "Godot bootstrap is not pinned to official build releases")
need("mono_linux_x86_64" in bootstrap and "mono_export_templates" in bootstrap, "Godot .NET editor/templates pin missing")
need('GODOT_VERSION="${GODOT_VERSION:-4.7.1}"' in bootstrap, "Godot 4.7.1 default pin missing")

for marker in [
    "dotnet restore", "dotnet build", "-warnaserror", "dotnet test",
    "validate-json-content.py", "Persistence.PersistenceTests", "export-project.sh release",
    "package-release.py", "SHA256SUMS.txt", "gh release create",
]:
    need(marker in release, f"release stage missing: {marker}")
for artifact in [
    "windows-x64.zip", "linux-x86_64.tar.gz",
    "windows-x64-compatibility.zip", "linux-x86_64-compatibility.tar.gz",
    "symbols.zip", "release-manifest.json", "SHA256SUMS.txt"
]:
    need(artifact in package or artifact in release, f"release artifact contract missing: {artifact}")
need("*.pdb" in package and "no Project Horizon portable PDB symbols" in package, "symbols archive does not enforce PDB presence")
need("sha256" in package.lower(), "release checksums are not generated")
need("CHANGELOG.md" in package and "VERSION" in package, "version/changelog are not packaged")
need(bool(re.fullmatch(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version)), "VERSION is not SemVer-like")
need(f"## [{version}]" in changelog, "CHANGELOG lacks current VERSION section")
need("tag" in version_gate and "CHANGELOG" in version_gate, "release version/tag/changelog consistency gate missing")

for pattern in ["*.blend", "*.ogg", "*.mp4"]:
    need(pattern in attrs and "filter=lfs" in attrs, f"Git LFS pattern missing: {pattern}")
for pattern in ["**/.godot/", "**/bin/", "**/obj/", "*.db", "*.log"]:
    need(pattern in ignore, f"ignore policy missing: {pattern}")

need("Draft202012Validator" in json_gate, "normative JSON Schema validation missing")
need("Project_Horizon_Industry_Content_Schema_v2.0.json" in json_gate, "industry schema is not wired into JSON gate")
need("localizationParity" in json_gate, "JSON gate lacks cross-catalog parity check")

# Official action majors current when TASK-140 was authored. This is a static
# repository contract, not a guarantee that GitHub will never publish newer majors.
for action in ["actions/checkout@v7", "actions/setup-dotnet@v6", "actions/upload-artifact@v7"]:
    need(action in ci or action in release, f"official action pin missing: {action}")

status = "PASS" if not failures else "FAIL"
print(
    f"TASK-140 SECTION-37 CONTRACT {status}: branches=5/5; prPipeline=8/8; "
    f"debugExports=4/4; releaseExports=4/4; symbols=1; checksums=1; version=1; changelog=1; "
    f"jsonSchema=1; migrations=1; warningsAsErrors=1; headlessGodot=1."
)
for failure in failures:
    print("ERROR:", failure)
raise SystemExit(0 if not failures else 1)
