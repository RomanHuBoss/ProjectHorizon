using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;

public sealed class GameUserSettings
{
    public const float DefaultOnFootSensitivity = 0.0025f;
    public const float DefaultShipSensitivity = 0.0035f;

    public string LanguageCode { get; set; } = GameLocalizationService.AutomaticLanguage;
    public float OnFootMouseSensitivity { get; set; } = DefaultOnFootSensitivity;
    public float ShipMouseSensitivity { get; set; } = DefaultShipSensitivity;
    public bool InvertOnFootX { get; set; }
    public bool InvertOnFootY { get; set; }
    public bool InvertShipPitch { get; set; }
    public bool InvertShipYaw { get; set; }
    public float GamepadDeadZone { get; set; } = AccessibilityControlPolicy.DefaultGamepadDeadZone;
    public float GamepadResponseExponent { get; set; } = AccessibilityControlPolicy.DefaultGamepadResponseExponent;
    public float SubtitleScale { get; set; } = AccessibilityControlPolicy.DefaultSubtitleScale;
    public float FieldOfViewDegrees { get; set; } = 75.0f;
    public float UiScale { get; set; } = 1.0f;
    public bool SubtitlesEnabled { get; set; } = true;
    public bool CameraShakeEnabled { get; set; }
    public bool MotionBlurEnabled { get; set; }
    public GraphicsQualityProfile GraphicsQualityProfile { get; set; } = GraphicsQualityProfile.Medium;
    public float MusicVolume { get; set; } = 0.75f;
    public float EffectsVolume { get; set; } = 0.85f;
    public float SpeechVolume { get; set; } = 0.90f;
    public Dictionary<string, long> KeyboardBindings { get; } = new(StringComparer.Ordinal);

    public GameUserSettings()
    {
        ResetKeyboardBindings();
    }

    public void ResetKeyboardBindings()
    {
        KeyboardBindings.Clear();
        foreach ((string action, Key key) in GameUserSettingsService.DefaultKeyboardBindings)
        {
            KeyboardBindings[action] = (long)key;
        }
    }

    public GameUserSettings Clone()
    {
        GameUserSettings clone = new()
        {
            LanguageCode = LanguageCode,
            OnFootMouseSensitivity = OnFootMouseSensitivity,
            ShipMouseSensitivity = ShipMouseSensitivity,
            InvertOnFootX = InvertOnFootX,
            InvertOnFootY = InvertOnFootY,
            InvertShipPitch = InvertShipPitch,
            InvertShipYaw = InvertShipYaw,
            GamepadDeadZone = GamepadDeadZone,
            GamepadResponseExponent = GamepadResponseExponent,
            SubtitleScale = SubtitleScale,
            FieldOfViewDegrees = FieldOfViewDegrees,
            UiScale = UiScale,
            SubtitlesEnabled = SubtitlesEnabled,
            CameraShakeEnabled = CameraShakeEnabled,
            MotionBlurEnabled = MotionBlurEnabled,
            GraphicsQualityProfile = GraphicsQualityProfile,
            MusicVolume = MusicVolume,
            EffectsVolume = EffectsVolume,
            SpeechVolume = SpeechVolume
        };
        clone.KeyboardBindings.Clear();
        foreach ((string action, long key) in KeyboardBindings)
        {
            clone.KeyboardBindings[action] = key;
        }
        return clone;
    }
}

public static class GameProfilePaths
{
    public const string PrimarySlotId = "save_1";

    public static string PrimaryDatabasePath => Path.Combine(
        ProjectSettings.GlobalizePath("user://"),
        "profiles",
        "profile_vertical_slice",
        "save_1.db");

    public static string SettingsPath => "user://settings.cfg";
}

public static class GameUserSettingsService
{
    private const string SectionGeneral = "general";
    private const string SectionAudio = "audio";
    private const string SectionControls = "controls";
    private const string SectionBindings = "bindings";

    private static GameUserSettings? _current;

    public static readonly (string Action, Key Key)[] DefaultKeyboardBindings =
    {
        ("pause", Key.Escape),
        ("planet_map", Key.N),
        ("move_forward", Key.W),
        ("move_backward", Key.S),
        ("move_left", Key.A),
        ("move_right", Key.D),
        ("jump", Key.Space),
        ("interact", Key.E),
        ("player_sprint", Key.Shift),
        ("player_crouch", Key.Ctrl),
        ("ship_forward", Key.W),
        ("ship_reverse", Key.S),
        ("ship_strafe_left", Key.A),
        ("ship_strafe_right", Key.D),
        ("ship_lift_up", Key.Space),
        ("ship_lift_down", Key.C),
        ("ship_roll_left", Key.Q),
        ("ship_roll_right", Key.E),
        ("ship_pitch_up", Key.Up),
        ("ship_pitch_down", Key.Down),
        ("ship_yaw_left", Key.Left),
        ("ship_yaw_right", Key.Right),
        ("ship_boost", Key.B),
        ("ship_brake", Key.X),
        ("ship_camera", Key.F2),
        ("ship_stabilize", Key.G)
    };

    public static GameUserSettings Current => _current ??= Load();

    public static GameUserSettings Load()
    {
        GameUserSettings settings = new();
        ConfigFile config = new();
        Error load = config.Load(GameProfilePaths.SettingsPath);
        if (load != Error.Ok)
        {
            Normalize(settings);
            return settings;
        }

        settings.LanguageCode = ReadString(
            config,
            SectionGeneral,
            "language",
            GameLocalizationService.AutomaticLanguage);
        settings.OnFootMouseSensitivity = ReadFloat(
            config,
            SectionControls,
            "on_foot_mouse_sensitivity",
            settings.OnFootMouseSensitivity);
        settings.ShipMouseSensitivity = ReadFloat(
            config,
            SectionControls,
            "ship_mouse_sensitivity",
            settings.ShipMouseSensitivity);
        settings.InvertOnFootX = ReadBool(config, SectionControls, "invert_on_foot_x", false);
        settings.InvertOnFootY = ReadBool(config, SectionControls, "invert_on_foot_y", false);
        settings.InvertShipPitch = ReadBool(config, SectionControls, "invert_ship_pitch", false);
        settings.InvertShipYaw = ReadBool(config, SectionControls, "invert_ship_yaw", false);
        settings.GamepadDeadZone = ReadFloat(config, SectionControls, "gamepad_dead_zone", AccessibilityControlPolicy.DefaultGamepadDeadZone);
        settings.GamepadResponseExponent = ReadFloat(config, SectionControls, "gamepad_response_exponent", AccessibilityControlPolicy.DefaultGamepadResponseExponent);
        settings.SubtitleScale = ReadFloat(config, SectionGeneral, "subtitle_scale", AccessibilityControlPolicy.DefaultSubtitleScale);
        settings.FieldOfViewDegrees = ReadFloat(config, SectionGeneral, "fov_degrees", 75.0f);
        settings.UiScale = ReadFloat(config, SectionGeneral, "ui_scale", 1.0f);
        settings.SubtitlesEnabled = ReadBool(config, SectionGeneral, "subtitles", true);
        settings.CameraShakeEnabled = ReadBool(config, SectionGeneral, "camera_shake", false);
        settings.MotionBlurEnabled = ReadBool(config, SectionGeneral, "motion_blur", false);
        settings.GraphicsQualityProfile = (GraphicsQualityProfile)ReadLong(
            config, SectionGeneral, "graphics_quality_profile", (long)GraphicsQualityProfile.Medium);
        settings.MusicVolume = ReadFloat(config, SectionAudio, "music", 0.75f);
        settings.EffectsVolume = ReadFloat(config, SectionAudio, "effects", 0.85f);
        settings.SpeechVolume = ReadFloat(config, SectionAudio, "speech", 0.90f);

        foreach ((string action, Key defaultKey) in DefaultKeyboardBindings)
        {
            long value = ReadLong(config, SectionBindings, action, (long)defaultKey);
            settings.KeyboardBindings[action] = value;
        }

        Normalize(settings);
        return settings;
    }

    public static Error SaveAndApply(GameUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        ConfigFile config = new();
        config.SetValue(SectionGeneral, "language", settings.LanguageCode);
        config.SetValue(SectionControls, "on_foot_mouse_sensitivity", settings.OnFootMouseSensitivity);
        config.SetValue(SectionControls, "ship_mouse_sensitivity", settings.ShipMouseSensitivity);
        config.SetValue(SectionControls, "invert_on_foot_x", settings.InvertOnFootX);
        config.SetValue(SectionControls, "invert_on_foot_y", settings.InvertOnFootY);
        config.SetValue(SectionControls, "invert_ship_pitch", settings.InvertShipPitch);
        config.SetValue(SectionControls, "invert_ship_yaw", settings.InvertShipYaw);
        config.SetValue(SectionControls, "gamepad_dead_zone", settings.GamepadDeadZone);
        config.SetValue(SectionControls, "gamepad_response_exponent", settings.GamepadResponseExponent);
        config.SetValue(SectionGeneral, "subtitle_scale", settings.SubtitleScale);
        config.SetValue(SectionGeneral, "fov_degrees", settings.FieldOfViewDegrees);
        config.SetValue(SectionGeneral, "ui_scale", settings.UiScale);
        config.SetValue(SectionGeneral, "subtitles", settings.SubtitlesEnabled);
        config.SetValue(SectionGeneral, "camera_shake", settings.CameraShakeEnabled);
        config.SetValue(SectionGeneral, "motion_blur", settings.MotionBlurEnabled);
        config.SetValue(SectionGeneral, "graphics_quality_profile", (long)settings.GraphicsQualityProfile);
        config.SetValue(SectionAudio, "music", settings.MusicVolume);
        config.SetValue(SectionAudio, "effects", settings.EffectsVolume);
        config.SetValue(SectionAudio, "speech", settings.SpeechVolume);
        foreach ((string action, long key) in settings.KeyboardBindings)
        {
            config.SetValue(SectionBindings, action, key);
        }

        Error save = config.Save(GameProfilePaths.SettingsPath);
        if (save == Error.Ok)
        {
            _current = settings.Clone();
            ApplyRuntime(_current);
        }
        return save;
    }

    public static void ReloadAndApply()
    {
        _current = Load();
        ApplyRuntime(_current);
    }

    public static void ApplyRuntime(GameUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        GameLocalizationService.ApplyConfiguredLanguage(settings.LanguageCode);
        ApplyInputMap(settings);
        GameAccessibilityRuntime.ApplyInputMap(settings);
        ApplyAudio(settings);
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            tree.Root.ContentScaleFactor = settings.UiScale;
        }
    }

    public static void ApplyToPlayer(PlayerController player)
    {
        ArgumentNullException.ThrowIfNull(player);
        GameUserSettings settings = Current;
        ApplyRuntime(settings);
        player.MouseSensitivity = settings.OnFootMouseSensitivity;
        player.InvertLookX = settings.InvertOnFootX;
        player.InvertLookY = settings.InvertOnFootY;
        player.GamepadResponseExponent = settings.GamepadResponseExponent;
        player.SetFieldOfView(settings.FieldOfViewDegrees);
    }

    public static void ApplyToShip(ArcadeShipController ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        GameUserSettings settings = Current;
        ApplyRuntime(settings);
        ship.MouseSensitivity = settings.ShipMouseSensitivity;
        ship.InvertPitchLook = settings.InvertShipPitch;
        ship.InvertYawLook = settings.InvertShipYaw;
        ship.GamepadResponseExponent = settings.GamepadResponseExponent;
        ship.SetFieldOfView(settings.FieldOfViewDegrees);
    }

    public static string DescribeKey(string action, GameUserSettings? settings = null)
    {
        GameUserSettings source = settings ?? Current;
        return source.KeyboardBindings.TryGetValue(action, out long code)
            ? ((Key)code).ToString()
            : GameLocalizationService.Text("ui.common.unbound");
    }

    private static void ApplyInputMap(GameUserSettings settings)
    {
        foreach ((string action, Key defaultKey) in DefaultKeyboardBindings)
        {
            EnsureAction(action);
            InputMap.ActionEraseEvents(action);
            long code = settings.KeyboardBindings.TryGetValue(action, out long configured)
                ? configured
                : (long)defaultKey;
            InputMap.ActionAddEvent(action, new InputEventKey
            {
                PhysicalKeycode = (Key)code
            });
            AddFixedGamepadEvents(action);
        }

        EnsureAction("fire_primary");
        if (!HasMouseButton("fire_primary", MouseButton.Left))
        {
            InputMap.ActionAddEvent("fire_primary", new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left
            });
        }
        if (!HasJoyButton("fire_primary", 10))
        {
            AddJoyButton("fire_primary", 10);
        }
    }

    private static void AddFixedGamepadEvents(string action)
    {
        switch (action)
        {
            case "pause": AddJoyButton(action, 6); break;
            case "planet_map": AddJoyButton(action, 4); break;
            case "move_left": AddJoyAxis(action, 0, -1.0f); break;
            case "move_right": AddJoyAxis(action, 0, 1.0f); break;
            case "move_forward": AddJoyAxis(action, 1, -1.0f); break;
            case "move_backward": AddJoyAxis(action, 1, 1.0f); break;
            case "jump": AddJoyButton(action, 0); break;
            case "interact": AddJoyButton(action, 2); break;
            case "player_sprint": AddJoyButton(action, 7); break;
            case "player_crouch": AddJoyButton(action, 1); break;
            case "ship_forward": AddJoyAxis(action, 5, 1.0f); break;
            case "ship_reverse": AddJoyAxis(action, 4, 1.0f); break;
            case "ship_strafe_left": AddJoyAxis(action, 0, -1.0f); break;
            case "ship_strafe_right": AddJoyAxis(action, 0, 1.0f); break;
            case "ship_lift_up": AddJoyButton(action, 10); break;
            case "ship_lift_down": AddJoyButton(action, 9); break;
            case "ship_roll_left": AddJoyButton(action, 13); break;
            case "ship_roll_right": AddJoyButton(action, 14); break;
            case "ship_pitch_up": AddJoyAxis(action, 3, -1.0f); break;
            case "ship_pitch_down": AddJoyAxis(action, 3, 1.0f); break;
            case "ship_yaw_left": AddJoyAxis(action, 2, -1.0f); break;
            case "ship_yaw_right": AddJoyAxis(action, 2, 1.0f); break;
            case "ship_boost": AddJoyButton(action, 0); break;
            case "ship_brake": AddJoyButton(action, 1); break;
            case "ship_camera": AddJoyButton(action, 3); break;
            case "ship_stabilize": AddJoyButton(action, 2); break;
        }
    }

    private static void AddJoyButton(string action, int button)
    {
        InputMap.ActionAddEvent(action, new InputEventJoypadButton
        {
            ButtonIndex = (JoyButton)button
        });
    }

    private static void AddJoyAxis(string action, int axis, float value)
    {
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion
        {
            Axis = (JoyAxis)axis,
            AxisValue = value
        });
    }

    private static bool HasMouseButton(string action, MouseButton button)
    {
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
        {
            if (inputEvent is InputEventMouseButton mouse && mouse.ButtonIndex == button)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasJoyButton(string action, int button)
    {
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
        {
            if (inputEvent is InputEventJoypadButton joy && (int)joy.ButtonIndex == button)
            {
                return true;
            }
        }
        return false;
    }

    private static void EnsureAction(string action)
    {
        if (!InputMap.HasAction(action))
        {
            InputMap.AddAction(action, 0.2f);
        }
    }

    private static void ApplyAudio(GameUserSettings settings)
    {
        AudioDirector.EnsureBusLayout();
        SetBusVolume("Music", settings.MusicVolume);
        SetBusVolume("Voice", settings.SpeechVolume);
        foreach (string bus in new[] { "Ambient", "SFX", "UI", "Vehicle", "Weather" })
        {
            SetBusVolume(bus, settings.EffectsVolume);
        }
    }

    private static void SetBusVolume(string busName, float linear)
    {
        int index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, busName);
        }
        float clamped = Mathf.Clamp(linear, 0.0f, 1.0f);
        float db = clamped <= 0.0001f
            ? -80.0f
            : (float)(20.0 * Math.Log10(clamped));
        AudioServer.SetBusVolumeDb(index, db);
        AudioServer.SetBusMute(index, clamped <= 0.0001f);
    }

    private static void Normalize(GameUserSettings settings)
    {
        if (!GameLocalizationService.IsSupportedConfiguration(settings.LanguageCode))
        {
            settings.LanguageCode = GameLocalizationService.AutomaticLanguage;
        }
        settings.OnFootMouseSensitivity = Mathf.Clamp(settings.OnFootMouseSensitivity, 0.0005f, 0.01f);
        settings.ShipMouseSensitivity = Mathf.Clamp(settings.ShipMouseSensitivity, 0.0005f, 0.015f);
        settings.GamepadDeadZone = AccessibilityControlPolicy.NormalizeDeadZone(settings.GamepadDeadZone);
        settings.GamepadResponseExponent = AccessibilityControlPolicy.NormalizeResponseExponent(settings.GamepadResponseExponent);
        settings.SubtitleScale = AccessibilityControlPolicy.NormalizeSubtitleScale(settings.SubtitleScale);
        settings.FieldOfViewDegrees = Mathf.Clamp(settings.FieldOfViewDegrees, 60.0f, 110.0f);
        settings.UiScale = Mathf.Clamp(settings.UiScale, 0.8f, 1.5f);
        settings.MusicVolume = Mathf.Clamp(settings.MusicVolume, 0.0f, 1.0f);
        settings.EffectsVolume = Mathf.Clamp(settings.EffectsVolume, 0.0f, 1.0f);
        settings.SpeechVolume = Mathf.Clamp(settings.SpeechVolume, 0.0f, 1.0f);
        if (!GraphicsQualityProfilePolicy.IsValid(settings.GraphicsQualityProfile))
        {
            settings.GraphicsQualityProfile = GraphicsQualityProfile.Medium;
        }
        foreach ((string action, Key key) in DefaultKeyboardBindings)
        {
            if (!settings.KeyboardBindings.ContainsKey(action))
            {
                settings.KeyboardBindings[action] = (long)key;
            }
        }
    }

    private static string ReadString(ConfigFile config, string section, string key, string fallback) =>
        config.GetValue(section, key, fallback).AsString();
    private static float ReadFloat(ConfigFile config, string section, string key, float fallback) =>
        (float)config.GetValue(section, key, fallback).AsDouble();
    private static bool ReadBool(ConfigFile config, string section, string key, bool fallback) =>
        config.GetValue(section, key, fallback).AsBool();
    private static long ReadLong(ConfigFile config, string section, string key, long fallback) =>
        config.GetValue(section, key, fallback).AsInt64();
}
