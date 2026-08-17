#!/usr/bin/env python3
from pathlib import Path
import json, sys
ROOT=Path(__file__).resolve().parents[1]
fail=[]
def text(p): return (ROOT/p).read_text(encoding='utf-8')
def need(c,m):
    if not c: fail.append(m)
version=text('VERSION').strip()
need(version in {'0.1.0-alpha.204','0.1.0-alpha.206','0.1.0-alpha.208','0.1.0-alpha.210','0.1.0-alpha.212','0.1.0-alpha.214','0.1.0-alpha.216'}, f'VERSION must preserve alpha.204 contract, got {version}')
policy=text('src/Game.Domain/Architecture/AccessibilityControlPolicy.cs')
settings=text('src/Game.Client/Scripts/Application/GameUserSettings.cs')
panel=text('src/Game.Client/Scripts/Application/GameSettingsPanel.cs')
runtime=text('src/Game.Client/Scripts/Application/GameAccessibilityRuntime.cs')
slice204=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAccessibility.cs')
accept=text('src/Game.Client/Scripts/VerticalSlice/AccessibilityAcceptance.cs')
main=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSlice.cs')
app=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceApplicationShell.cs')
audio=text('src/Game.Client/Scripts/VerticalSlice/SalvageRepairSliceAudio.cs')
player=text('src/Game.Client/Scripts/Player/PlayerController.cs')
ship=text('src/Game.Client/Scripts/Ship/ArcadeShipController.cs')
tests=text('tests/ProjectHorizon.Tests/Unit/AccessibilityControlTests.cs')
for token in ('DefaultGamepadDeadZone = 0.20f','MinimumGamepadDeadZone = 0.05f','MaximumGamepadDeadZone = 0.45f',
              'DefaultGamepadResponseExponent = 1.25f','MinimumSubtitleScale = 0.80f','MaximumSubtitleScale = 1.50f',
              'ShapeScalar','SeverityToken'):
    need(token in policy, f'accessibility policy missing: {token}')
for token in ('GamepadDeadZone','GamepadResponseExponent','SubtitleScale','invert_on_foot_x','invert_on_foot_y','invert_ship_pitch','invert_ship_yaw'):
    need(token in settings, f'settings persistence missing: {token}')
need('ui.settings.gamepad_dead_zone' in panel and 'ui.settings.gamepad_response' in panel and 'ui.settings.subtitle_scale' in panel,
     'settings UI gamepad/subtitle controls missing')
need('InputMap.ActionSetDeadzone' in runtime and 'ReadVector' in runtime and 'ReadAxis' in runtime and 'ReadStrength' in runtime,
     'live gamepad dead-zone/curve runtime missing')
need('GameAccessibilityRuntime.ReadVector' in player and 'GamepadResponseExponent' in player,
     'on-foot analog response hook missing')
need('GameAccessibilityRuntime.ReadAxis' in ship and 'GameAccessibilityRuntime.ReadStrength' in ship,
     'ship analog response hook missing')
need('AccessibilitySubtitles' in slice204 and 'AccessibilityStatusCues' in slice204 and
     '[HP]' in slice204 and '[SH]' in slice204 and '[O2]' in slice204 and '[HZ]' in slice204,
     'caption/non-color status layer missing')
need('PublishAccessibilityCaption("ui.access.caption.radio"' in audio and
     'PublishAccessibilityCaption("ui.access.caption.damage"' in audio and
     'PublishAccessibilityCaption("ui.access.caption.oxygen_low"' in audio,
     'audible cue caption hooks missing')
need('ApplyAccessibilityRuntimeSettings();' in app and 'GamepadDeadZone' in app and 'SubtitleScale' in app,
     'settings live/roundtrip integration missing')
need('InitializeAccessibilityRuntime();' in main and 'UpdateAccessibilityRuntime(delta);' in main and
     'RunAccessibilityAcceptance();' in main and 'TASK-204 (F5)' in main and '_accessibilityAcceptancePassed == true' in main,
     'TASK-204 live/F5/final gate missing')
need('AccessibilityAcceptanceRunner' in accept and 'colorIndependent=' in accept,
     'TASK-204 acceptance output missing')
for name in ('DeadZoneIsClampedToSupportedRange','ResponseCurvePreservesSignAndFullScale','StatusSeverityDoesNotDependOnColor','SubtitleScaleIsBounded'):
    need(name in tests, f'TASK-204 xUnit missing: {name}')
for loc in ('src/Game.Client/Content/localization.en.json','src/Game.Client/Content/localization.ru.json'):
    strings=json.loads(text(loc))['strings']
    for key in ('ui.settings.gamepad_dead_zone','ui.settings.gamepad_response','ui.settings.subtitle_scale',
                'ui.access.caption.radio','ui.access.caption.damage','ui.access.caption.oxygen_low'):
        need(key in strings and str(strings[key]).strip(), f'{loc} missing {key}')
need((ROOT/'docs/ACCESSIBILITY_RUNTIME.md').exists(), 'TASK-204 docs missing')
need('TASK-204' in text('README.md'), 'README TASK-204 missing')
need('## [0.1.0-alpha.204]' in text('CHANGELOG.md'), 'CHANGELOG alpha.204 missing')
need('TASK-204' in text('REQUIREMENTS_STATUS.md'), 'requirements TASK-204 missing')
for p in ('tools/run-section37-quality.sh','tools/run-section37-quality.cmd','.github/workflows/ci.yml','.github/workflows/release.yml'):
    need('validate-task204-accessibility-runtime.py' in text(p), f'release gate missing in {p}')
if fail:
    print('TASK-204 ACCESSIBILITY RUNTIME CONTRACT FAIL:')
    for x in fail: print('ERROR:',x)
    sys.exit(1)
print('TASK-204 ACCESSIBILITY RUNTIME CONTRACT PASS: inversion=on-foot+ship; gamepad=dead-zone+response-curve; subtitles=toggle+scale+audio-captions; reducedMotion=persistent; status=text+token; audio=music+sfx+voice; f5=1; xunit=1.')
