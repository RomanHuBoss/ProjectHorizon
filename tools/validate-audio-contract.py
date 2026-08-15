#!/usr/bin/env python3
"""Static contract gate for Project Horizon TASK-134 / PDF v2.0 §32 Sound.

This does not replace a Godot runtime acceptance. It verifies that the shipping
source contains one coherent audio architecture: normative buses, bounded
pools, 3D attenuation, environment profiles, vacuum routing, music states,
settings routing and real gameplay hook points. It also prevents raw WAV/AIFF
source audio from silently entering the distributable project tree.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "src" / "Game.Client" / "Scripts"
APP = SRC / "Application"
VERT = SRC / "VerticalSlice"

AUDIO_DIRECTOR = APP / "AudioDirector.cs"
AUDIO_BANK = APP / "ProceduralAudioBank.cs"
SETTINGS = APP / "GameUserSettings.cs"
SLICE_AUDIO = VERT / "SalvageRepairSliceAudio.cs"
SLICE = VERT / "SalvageRepairSlice.cs"
SURVIVAL = VERT / "SalvageRepairSlicePlayerSurvival.cs"
MAIN_MENU = APP / "MainMenuController.cs"
PAUSE = APP / "GamePauseOverlay.cs"

EXPECTED_BUSES = [
    "Master", "Music", "Ambient", "SFX", "UI", "Voice", "Vehicle", "Weather"
]
EXPECTED_ENVIRONMENTS = {"Atmosphere", "Vacuum", "Interior", "Water"}
EXPECTED_MUSIC = {"None", "Menu", "Surface", "Space", "Interior", "Combat"}
PLAYER_HOOKS = {
    "weapon": "PlayPlayerWeaponAudio()",
    "resource": "PlayResourceCollectAudio(",
    "craft": "PlayCraftCompletionAudio(",
    "dialogue": "PlayDialogueVoiceAudio()",
    "damage": "PlayPlayerDamageAudio()",
    "lifeSupport": "PlayLifeSupportAlarm()",
}


def fail(message: str, failures: list[str]) -> None:
    failures.append(message)


def read(path: Path) -> str:
    if not path.is_file():
        raise FileNotFoundError(path)
    return path.read_text(encoding="utf-8", errors="replace")


def enum_members(text: str, enum_name: str) -> set[str]:
    match = re.search(rf"public\s+enum\s+{re.escape(enum_name)}\s*\{{([^}}]+)\}}", text, re.S)
    if not match:
        return set()
    return set(re.findall(r"\b([A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*[-0-9]+)?\s*(?:,|$)", match.group(1)))


def main() -> int:
    failures: list[str] = []
    try:
        director = read(AUDIO_DIRECTOR)
        bank = read(AUDIO_BANK)
        settings = read(SETTINGS)
        slice_audio = read(SLICE_AUDIO)
        slice_main = read(SLICE)
        survival = read(SURVIVAL)
        main_menu = read(MAIN_MENU)
        pause = read(PAUSE)
    except FileNotFoundError as exc:
        print(f"TASK-134 AUDIO CONTRACT FAIL: missing={exc}")
        return 1

    buses_match = re.search(
        r"RequiredBuses\s*=\s*\{(?P<body>.*?)\};", director, re.S
    )
    buses = re.findall(r'"([A-Za-z]+)"', buses_match.group("body")) if buses_match else []
    if buses != EXPECTED_BUSES:
        fail(f"required buses mismatch: {buses}", failures)

    pool2d = re.search(r"TwoDPoolSize\s*=\s*(\d+)", director)
    pool3d = re.search(r"ThreeDPoolSize\s*=\s*(\d+)", director)
    p2 = int(pool2d.group(1)) if pool2d else 0
    p3 = int(pool3d.group(1)) if pool3d else 0
    if p2 <= 0 or p3 <= 0 or p2 + p3 > 32:
        fail(f"invalid transient pool budget: 2d={p2}, 3d={p3}", failures)
    if "MaximumTransientVoices = TwoDPoolSize + ThreeDPoolSize" not in director:
        fail("maximum transient voices are not derived from bounded pools", failures)
    if "DedicatedLoopVoiceCount = 5" not in director or \
            "MaximumConcurrentVoices = MaximumTransientVoices + DedicatedLoopVoiceCount" not in director:
        fail("overall concurrent-voice budget is not fixed", failures)

    env = enum_members(director, "GameAudioEnvironment")
    if env != EXPECTED_ENVIRONMENTS:
        fail(f"environment coverage mismatch: {sorted(env)}", failures)
    music = enum_members(director, "GameMusicState")
    if music != EXPECTED_MUSIC:
        fail(f"music-state coverage mismatch: {sorted(music)}", failures)

    cue_constants = dict(re.findall(
        r'public\s+const\s+string\s+([A-Za-z0-9_]+)\s*=\s*"([^"]+)";',
        bank,
    ))
    registered = set(re.findall(r"Streams\[AudioCue\.([A-Za-z0-9_]+)\]\s*=", bank))
    if set(cue_constants) != registered:
        fail(
            "cue registration mismatch: missing=" +
            ",".join(sorted(set(cue_constants) - registered)) +
            "; extra=" + ",".join(sorted(registered - set(cue_constants))),
            failures,
        )
    if len(cue_constants) < 19:
        fail(f"cue bank unexpectedly small: {len(cue_constants)}", failures)

    positional_markers = [
        "AudioStreamPlayer3D", "MaxDistance", "UnitSize", "GlobalPosition",
        "AcquireThreeDVoice", "MaxPolyphony = 1",
    ]
    if not all(marker in director for marker in positional_markers):
        fail("3D positional/attenuation/pool contract incomplete", failures)

    environment_markers = [
        "ApplyEnvironmentFilterProfile", "AudioEffectLowPassFilter",
        "AmbientAtmosphere", "AmbientInterior", "AmbientWater", "WeatherWind",
        "GameAudioEnvironment.Vacuum", "externalInVacuum",
        "_vacuumSuppressed++",
    ]
    if not all(marker in director for marker in environment_markers):
        fail("environment/vacuum contract incomplete", failures)

    lifecycle_markers = [
        "_pendingInstallation",
        "root.CallDeferred(Node.MethodName.AddChild, director)",
        "if (!_ready || !IsInsideTree())",
        "ApplyEnvironmentState(requestedEnvironment, force: true, countTransition: false)",
        "ApplyMusicState(requestedMusicState, force: true, countTransition: false)",
    ]
    if not all(marker in director for marker in lifecycle_markers):
        fail("deferred audio installation/pre-ready playback guard incomplete", failures)
    if "root.AddChild(director);" in director:
        fail("AudioDirector still performs synchronous root AddChild during scene setup", failures)

    music_markers = [
        "MusicCrossfadeSeconds", "BeginMusicCrossfade", "UpdateMusicCrossfade",
        "MusicMenu", "MusicSurface", "MusicSpace", "MusicInterior", "MusicCombat",
    ]
    if not all(marker in director for marker in music_markers):
        fail("music state/crossfade contract incomplete", failures)

    for bus in EXPECTED_BUSES[1:]:
        if f'"{bus}"' not in settings:
            fail(f"settings routing missing bus {bus}", failures)
    if "AudioDirector.EnsureBusLayout();" not in settings:
        fail("settings do not establish the normative bus layout", failures)

    combined_hooks = "\n".join((slice_audio, slice_main, survival, main_menu, pause))
    missing_hooks = [name for name, marker in PLAYER_HOOKS.items() if marker not in combined_hooks]
    if missing_hooks:
        fail("missing gameplay audio hooks: " + ",".join(missing_hooks), failures)
    if "AttachUiSounds(this)" not in combined_hooks:
        fail("UI button pool hook is absent", failures)
    if "UpdateAudioRuntime(delta)" not in slice_main:
        fail("audio runtime is not driven from the gameplay process loop", failures)
    if "RunAudioArchitectureAcceptance();" not in slice_main:
        fail("TASK-134 F5 runtime acceptance is not wired", failures)

    # §32 distribution rule: do not ship raw uncompressed authoring sources.
    raw_audio = [
        p for p in (ROOT / "src" / "Game.Client").rglob("*")
        if p.is_file() and p.suffix.lower() in {".wav", ".wave", ".aif", ".aiff"}
    ]
    if raw_audio:
        fail("raw source audio present: " + ",".join(str(p.relative_to(ROOT)) for p in raw_audio[:20]), failures)

    # The acceptance/HUD labels are localized via the same service closed by TASK-132.
    loc_markers = [
        '"ui.hud.audio"', '"audio.environment.atmosphere"',
        '"audio.environment.vacuum"', '"audio.environment.interior"',
        '"audio.environment.water"', '"audio.music.combat"',
    ]
    if not all(marker in slice_audio for marker in loc_markers):
        fail("audio diagnostics are not routed through localization keys", failures)

    if failures:
        print(
            "TASK-134 AUDIO CONTRACT FAIL: "
            f"buses={len(buses)}/8; cues={len(cue_constants)}; pool2d={p2}; pool3d={p3}; "
            f"environments={len(env)}; musicStates={len(music)}; sourceAudioAssets={len(raw_audio)}."
        )
        for failure in failures:
            print("ERROR: " + failure)
        return 1

    print(
        "TASK-134 AUDIO CONTRACT PASS: "
        f"buses={len(buses)}/8; cues={len(cue_constants)}; "
        f"pool2d={p2}; pool3d={p3}; maxTransient={p2 + p3}; maxConcurrent={p2 + p3 + 5}; "
        f"environments={len(env)}; musicStates={len(music)}; "
        "positional=1; attenuation=1; pooling=1; vacuumRule=1; "
        "deferredInstall=1; preReadyPlaybackGuard=1; "
        f"gameplayHooks={len(PLAYER_HOOKS)}; settingsRouting=1; localization=1; "
        f"sourceAudioAssets={len(raw_audio)}."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
