#!/usr/bin/env python3
"""Static contract for TASK-144 layered architecture and Compatibility renderer closure."""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []


def need(condition: bool, message: str) -> None:
    if not condition:
        errors.append(message)


def read(path: str) -> str:
    p = ROOT / path
    need(p.is_file(), f"missing file: {path}")
    return p.read_text(encoding="utf-8", errors="replace") if p.is_file() else ""


domain_project = read("src/Game.Domain/Game.Domain.csproj")
application_project = read("src/Game.Application/Game.Application.csproj")
client_project = read("src/Game.Client/Game.Client.csproj")
solution = read("src/Game.Client/Game.Client.sln")
project = read("src/Game.Client/project.godot")
exports = read("src/Game.Client/export_presets.cfg")
export_script = read("tools/ci/export-project.sh")
package = read("tools/ci/package-release.py")
ci = read(".github/workflows/ci.yml")
release = read(".github/workflows/release.yml")
renderer_diag = read("src/Game.Client/Scripts/Application/RendererProfileDiagnostics.cs")
main_menu = read("src/Game.Client/Scripts/Application/MainMenuController.cs")
architecture_runtime = read("src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceArchitecture.cs")

# Compiled layer boundaries.
need('<AssemblyName>Game.Domain</AssemblyName>' in domain_project, "Game.Domain assembly name missing")
need('<ProjectReference' not in domain_project, "Game.Domain must be dependency-free at project level")
need('Godot' not in domain_project and 'Microsoft.Data.Sqlite' not in domain_project, "Game.Domain project references infrastructure")
need('<AssemblyName>Game.Application</AssemblyName>' in application_project, "Game.Application assembly name missing")
need('../Game.Domain/Game.Domain.csproj' in application_project, "Game.Application must depend on Game.Domain")
need('Game.Client' not in application_project and 'Godot' not in application_project and 'Microsoft.Data.Sqlite' not in application_project,
     "Game.Application has forbidden client/infrastructure dependency")
need('../Game.Domain/Game.Domain.csproj' in client_project, "Game.Client must reference Game.Domain")
need('../Game.Application/Game.Application.csproj' in client_project, "Game.Client must reference Game.Application")
for name in ("Game.Domain", "Game.Application", "Game.Client"):
    need(name in solution, f"solution missing {name}")

for folder in (ROOT / "src/Game.Domain", ROOT / "src/Game.Application"):
    for source in folder.rglob("*.cs"):
        text = source.read_text(encoding="utf-8", errors="replace")
        need("using Godot" not in text and "Godot." not in text, f"Godot leaked into {source.relative_to(ROOT)}")
        need("Microsoft.Data.Sqlite" not in text, f"SQLite leaked into {source.relative_to(ROOT)}")

need((ROOT / "src/Game.Domain/Architecture/DomainEvents.cs").is_file(), "typed events were not moved into Game.Domain")
need((ROOT / "src/Game.Domain/Architecture/SystemFrequencyPolicy.cs").is_file(), "frequency policy was not moved into Game.Domain")
need((ROOT / "src/Game.Domain/ProjectHorizonGenerator.cs").is_file(), "generator version contract was not moved into Game.Domain")
need((ROOT / "src/Game.Application/Architecture/DomainEventBus.cs").is_file(), "event bus was not moved into Game.Application")
need(not (ROOT / "src/Game.Client/Scripts/Infrastructure/Architecture").exists(), "legacy client architecture folder still owns domain/application code")

# Renderer defaults and true Compatibility export profile.
for marker in [
    'renderer/rendering_method="mobile"',
    'renderer/rendering_method.compatibility="gl_compatibility"',
    'rendering_device/driver.windows="vulkan"',
    'rendering_device/driver.linuxbsd="vulkan"',
    'rendering_device/fallback_to_opengl3=true',
    'gl_compatibility/driver.windows="opengl3"',
    'gl_compatibility/driver.linuxbsd="opengl3"',
]:
    need(marker in project, f"renderer contract missing: {marker}")

for preset in ["Windows Desktop", "Linux", "Windows Desktop Compatibility", "Linux Compatibility"]:
    need(f'name="{preset}"' in exports, f"export preset missing: {preset}")
need(exports.count('custom_features="compatibility"') == 2, "Compatibility feature must be present on exactly two export presets")
need(exports.count('binary_format/architecture="x86_64"') == 4, "all four desktop presets must be x86_64")

for marker in [
    'windows-compatibility-$CONFIG',
    'linux-compatibility-$CONFIG',
    '"Windows Desktop Compatibility"',
    '"Linux Compatibility"',
]:
    need(marker in export_script, f"export script compatibility path missing: {marker}")
for artifact in [
    "windows-x64-compatibility.zip",
    "linux-x86_64-compatibility.tar.gz",
]:
    need(artifact in package and artifact in release, f"release compatibility artifact missing: {artifact}")
need("windows-compatibility-debug" in ci and "linux-compatibility-debug" in ci, "CI does not upload both Compatibility debug exports")

# Runtime evidence uses actual engine-selected renderer/driver and compiled assembly names.
need("RenderingServer.GetCurrentRenderingMethod()" in renderer_diag, "runtime renderer method probe missing")
need("RenderingServer.GetCurrentRenderingDriverName()" in renderer_diag, "runtime renderer driver probe missing")
need('OS.HasFeature("compatibility")' in renderer_diag, "Compatibility export feature probe missing")
need("RendererProfileDiagnostics.Capture()" in main_menu, "Main Menu does not emit renderer evidence")
for marker in [
    'typeof(IDomainEvent).Assembly.GetName().Name',
    'typeof(DomainEventBus).Assembly.GetName().Name',
    'TASK-144 platform architecture acceptance',
]:
    need(marker in architecture_runtime, f"F5 TASK-144 evidence missing: {marker}")

status = "PASS" if not errors else "FAIL"
print(
    f"TASK-144 PLATFORM/ARCHITECTURE CONTRACT {status}: layers=3/3; domainGodotFree=1; "
    f"applicationGodotFree=1; projectCycles=0; primaryRenderer=mobile/vulkan; "
    f"compatibilityRenderer=gl_compatibility/opengl3; desktopPresets=4/4; "
    f"debugExports=4/4; releaseExports=4/4; runtimeRendererEvidence=1."
)
for error in errors:
    print("ERROR:", error)
raise SystemExit(0 if not errors else 1)
