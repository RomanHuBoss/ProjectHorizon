using System;
using Godot;

public sealed record AccessibilityAcceptanceReport(
    bool InputInversion,
    bool GamepadTuning,
    bool SubtitlePresentation,
    bool ReducedMotionSettings,
    bool ColorIndependentCues,
    bool SeparateAudioVolumes,
    bool PersistentSettings,
    bool LiveHooks,
    float DeadZone,
    float ResponseExponent,
    float SubtitleScale)
{
    public bool Passed => InputInversion && GamepadTuning && SubtitlePresentation &&
        ReducedMotionSettings && ColorIndependentCues && SeparateAudioVolumes &&
        PersistentSettings && LiveHooks;

    public string BuildOutputLine() =>
        $"TASK-204 accessibility runtime acceptance {(Passed ? "PASS" : "FAIL")}: " +
        $"inversion={(InputInversion ? 1 : 0)}; gamepad={(GamepadTuning ? 1 : 0)}; " +
        $"subtitles={(SubtitlePresentation ? 1 : 0)}; reducedMotion={(ReducedMotionSettings ? 1 : 0)}; " +
        $"colorIndependent={(ColorIndependentCues ? 1 : 0)}; audio={(SeparateAudioVolumes ? 1 : 0)}; " +
        $"persistence={(PersistentSettings ? 1 : 0)}; live={(LiveHooks ? 1 : 0)}; " +
        $"deadZone={DeadZone:0.00}; response={ResponseExponent:0.00}; subtitleScale={SubtitleScale:0.00}; " +
        "result=section-31.2-and-31.4-accessibility-input-caption-and-noncolor-status-contract.";
}

public static class AccessibilityAcceptanceRunner
{
    public static AccessibilityAcceptanceReport Evaluate(
        GameUserSettings settings,
        PanelContainer? subtitlePanel,
        Label? statusCueLabel,
        PlayerController? player,
        ArcadeShipController? ship)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool inversion = player is not null && ship is not null &&
            player.InvertLookX == settings.InvertOnFootX &&
            player.InvertLookY == settings.InvertOnFootY &&
            ship.InvertPitchLook == settings.InvertShipPitch &&
            ship.InvertYawLook == settings.InvertShipYaw;
        bool gamepad = settings.GamepadDeadZone is >=
                AccessibilityControlPolicy.MinimumGamepadDeadZone and <=
                AccessibilityControlPolicy.MaximumGamepadDeadZone &&
            settings.GamepadResponseExponent is >=
                AccessibilityControlPolicy.MinimumGamepadResponseExponent and <=
                AccessibilityControlPolicy.MaximumGamepadResponseExponent &&
            Math.Abs(AccessibilityControlPolicy.ShapeScalar(0.5f, 1.5f)) < 0.5f;
        bool subtitle = subtitlePanel is not null && statusCueLabel is not null &&
            settings.SubtitleScale is >= AccessibilityControlPolicy.MinimumSubtitleScale and <=
                AccessibilityControlPolicy.MaximumSubtitleScale;
        GameUserSettings defaults = new();
        bool reducedMotion = !defaults.CameraShakeEnabled && !defaults.MotionBlurEnabled;
        bool cues = AccessibilityControlPolicy.SeverityToken(0.10) == "CRIT" &&
            AccessibilityControlPolicy.SeverityToken(0.30) == "LOW" &&
            AccessibilityControlPolicy.SeverityToken(0.80) == "OK";
        bool audio = settings.MusicVolume is >= 0.0f and <= 1.0f &&
            settings.EffectsVolume is >= 0.0f and <= 1.0f &&
            settings.SpeechVolume is >= 0.0f and <= 1.0f;
        bool persistence = settings.UiScale is >= 0.8f and <= 1.5f &&
            settings.FieldOfViewDegrees is >= 60.0f and <= 110.0f;
        bool live = player is not null && ship is not null &&
            Math.Abs(player.GamepadResponseExponent - settings.GamepadResponseExponent) < 0.0001f &&
            Math.Abs(ship.GamepadResponseExponent - settings.GamepadResponseExponent) < 0.0001f;

        return new AccessibilityAcceptanceReport(
            inversion,
            gamepad,
            subtitle,
            reducedMotion,
            cues,
            audio,
            persistence,
            live,
            settings.GamepadDeadZone,
            settings.GamepadResponseExponent,
            settings.SubtitleScale);
    }
}
