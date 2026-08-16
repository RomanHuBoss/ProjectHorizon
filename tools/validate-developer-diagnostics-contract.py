#!/usr/bin/env python3
"""TASK-136 static contract: PDF v2.0 sections 34 and 35."""
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "Game.Client"
failures: list[str] = []

def need(cond: bool, message: str) -> None:
    if not cond:
        failures.append(message)

def text(path: str) -> str:
    p = ROOT / path
    need(p.exists(), f"missing file: {path}")
    return p.read_text(encoding="utf-8", errors="replace") if p.exists() else ""

logger = text("src/Game.Client/Scripts/Infrastructure/StructuredGameLogger.cs")
context = text("src/Game.Client/Scripts/Developer/DeveloperToolContext.cs")
workbench = text("src/Game.Client/Scripts/Developer/DeveloperWorkbenchController.cs")
console = text("src/Game.Client/Scripts/Developer/DeveloperDiagnosticsSuite.cs")
bridge = text("src/Game.Client/Scripts/Developer/SalvageRepairSliceDeveloperBridge.cs")
terrain = text("src/Game.Client/Scripts/Terrain/TerrainChunkManager.cs")
planet = text("src/Game.Client/Scripts/Planet/CubeSpherePrototype.cs")
main_menu = text("src/Game.Client/Scripts/Application/MainMenuController.cs")
scene = text("src/Game.Client/Scenes/Developer/DeveloperWorkbench.tscn")

required_tools = ["Seed Explorer", "Planet Preview", "Chunk Profiler", "Save Inspector", "Debug Console"]
for tool in required_tools:
    need(tool in workbench, f"missing tool: {tool}")

required_commands = [
    "teleport", "surface_warp", "spawn", "give", "damage", "heal", "set_time", "set_weather",
    "load_system", "load_planet", "show_chunks", "show_navmesh", "show_ai",
    "profile_worldgen", "save", "reload_content",
]
for command in required_commands:
    need(f'"{command}"' in console, f"command not registered: {command}")
    need(f'"{command}" =>' in bridge, f"command not dispatched: {command}")

required_categories = [
    "BOOT", "CONTENT", "WORLDGEN", "STREAMING", "DATABASE", "SAVE", "PLAYER",
    "SHIP", "AI", "QUEST", "NETWORK", "SERVER", "PERFORMANCE", "ERROR",
]
for category in required_categories:
    need(re.search(rf"\b{category}\b", logger) is not None, f"missing log category: {category}")

for field in ["timestampUtc", "level", "category", "sessionId", "message", "exception", "system", "scene", "worldSeed", "worldObject"]:
    need(field in logger, f"structured log field missing: {field}")
for secret in ["password", "token", "secret", "authorization", "cookie", "api_key", "email", "username", "phone"]:
    need(secret in logger.lower(), f"redaction token missing: {secret}")
need("[USER_HOME]" in logger and "[USER]" in logger, "personal path/name redaction missing")
need("DateTimeOffset.UtcNow" in logger, "logger must use UTC timestamps")
need(".jsonl" in logger, "logger must write JSONL")
need("OS.IsDebugBuild()" in context and "--developer" in context, "developer mode gate missing")
need("DeveloperToolContext.IsDeveloperModeAllowed()" in main_menu, "main menu does not gate developer tools")
need("DeveloperToolContext.IsDeveloperModeAllowed()" in console, "debug console does not gate itself")
need("SaveDatabase" in workbench and "BackupDatabase" in workbench and "SqliteOpenMode.ReadOnly" in workbench and "migration-copy.db" in workbench, "Save Inspector isolated copy-migration contract missing")
need("CreateReadOnlySnapshotCopy" in workbench and "inspector-working" in workbench, "Save Inspector must not initialize/migrate the source DB")
need("SqliteOpenMode.ReadOnly" in workbench and "sqlite_master" in workbench and "Exported {tables.Count} SQLite tables" in workbench, "Save Inspector read-only all-table export missing")
need("ResolveSaveInspectorPath" in workbench and "Use Primary" in workbench, "Save Inspector arbitrary-path/open-save contract missing")
need("GalaxyNavigationRuntime" in workbench and "GenerateSystem" in workbench and "ClipboardSet" in workbench, "Seed Explorer generation/copy contract missing")
need("CubeSphereMeshBuilder.Build" in workbench and "Generation CPU" in workbench and "Resource-density" in workbench, "Planet Preview metrics missing")
need("CubeSphereDebugMode.DeveloperPreview" in planet and "GetDeveloperPreviewColor" in planet, "Planet Preview visual overlay mode missing")
for overlay in ["PreviewChunkGrid", "PreviewBiomes", "PreviewHeight", "PreviewResourceDensity"]:
    need(overlay in planet, f"Planet Preview overlay not applied at runtime: {overlay}")
for metric in ["LoadedChunks", "QueuedWork", "WorkerCpuMilliseconds", "MainThreadApplyMilliseconds", "GpuUploadSubmissionMilliseconds", "ManagedMemoryBytes", "Vertices", "Collisions", "CancelledJobs"]:
    need(metric in terrain, f"Chunk Profiler metric missing: {metric}")
need("CaptureProfilerSnapshot" in terrain, "Chunk Profiler snapshot API missing")
need("DeveloperWorkbenchController" in scene, "developer workbench scene wiring missing")
need("AddDeveloperAiMarkers" in bridge and "developer_ai_debug_marker" in bridge, "show_ai visual markers missing")
need("RunDeveloperDiagnosticsAcceptance" in bridge, "TASK-136 runtime acceptance missing")
need("StructuredGameLogger.UpdateContext" in bridge, "runtime logger context updates missing")

status = "PASS" if not failures else "FAIL"
print(
    f"TASK-136 DEVELOPER DIAGNOSTICS CONTRACT {status}: "
    f"tools={len(required_tools)}/5; commands={len(required_commands)}/16; "
    f"logCategories={len(required_categories)}/14; logFields=10/10; devGate={int(not any('gate' in f for f in failures))}; "
    f"seedExplorer={int('Seed Explorer' in workbench)}; planetPreview={int('Planet Preview' in workbench)}; "
    f"chunkProfiler={int('CaptureProfilerSnapshot' in terrain)}; saveInspector={int('Save Inspector' in workbench)}; "
    f"debugConsole={int('Debug Console' in workbench)}; redaction={int('SensitiveFieldTokens' in logger)}."
)
for failure in failures:
    print("ERROR:", failure)
raise SystemExit(0 if not failures else 1)
