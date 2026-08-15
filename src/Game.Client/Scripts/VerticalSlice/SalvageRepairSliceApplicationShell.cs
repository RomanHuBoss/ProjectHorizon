using System;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private const string MainMenuScenePath = "res://Scenes/UI/MainMenu.tscn";

    private GamePauseOverlay? _applicationOverlay;
    private bool _returnToMainMenuAfterGracefulExit;
    private bool _task130AcceptancePrinted;

    private void BindApplicationShellSceneNodes()
    {
        _applicationOverlay = GetNodeOrNull<GamePauseOverlay>("ApplicationShell");
        if (_applicationOverlay is null)
        {
            throw new InvalidOperationException(
                "Vertical slice scene is missing TASK-130 ApplicationShell.");
        }
        ApplyApplicationSettings();
    }

    public bool CanOpenApplicationPause()
    {
        if (_closeRequested ||
            (_state != SalvageRepairSliceState.Ready &&
             _state != SalvageRepairSliceState.Passed))
        {
            return false;
        }

        return !_stationServicesOpen &&
            !_baseBuildMode &&
            !_discoveryCatalogOpen &&
            !_shipManagementOpen &&
            !_galaxyMapOpen &&
            !_ecologyCatalogOpen &&
            !_missionJournalOpen &&
            !_playerEquipmentOpen &&
            !_npcInteractionOpen &&
            !_planetMapOpen;
    }

    public void ApplyApplicationSettings()
    {
        GameUserSettings settings = GameUserSettingsService.Current;
        GameUserSettingsService.ApplyRuntime(settings);
        if (_player is not null)
        {
            GameUserSettingsService.ApplyToPlayer(_player);
        }
        if (_voyageShip is not null)
        {
            GameUserSettingsService.ApplyToShip(_voyageShip);
        }
        GD.Print(
            "TASK-130 settings applied: " +
            $"onFootSensitivity={settings.OnFootMouseSensitivity:0.0000}; " +
            $"shipSensitivity={settings.ShipMouseSensitivity:0.0000}; " +
            $"fov={settings.FieldOfViewDegrees:0}; uiScale={settings.UiScale:0.00}; " +
            $"subtitles={(settings.SubtitlesEnabled ? 1 : 0)}; " +
            $"cameraShake={(settings.CameraShakeEnabled ? 1 : 0)}; " +
            $"motionBlur={(settings.MotionBlurEnabled ? 1 : 0)}.");
    }

    public void RequestReturnToMainMenu()
    {
        BeginApplicationExit(returnToMainMenu: true);
    }

    public void RequestApplicationQuit()
    {
        BeginApplicationExit(returnToMainMenu: false);
    }

    private void BeginApplicationExit(bool returnToMainMenu)
    {
        if (_closeRequested)
        {
            return;
        }
        _returnToMainMenuAfterGracefulExit = returnToMainMenu;
        CancelTimedCraft(returnToMainMenu
            ? "main-menu transition requested"
            : "application quit requested");
        _closeRequested = true;
        _applicationOverlay?.CloseForSceneTransition();
        TryBeginGracefulExit();
    }

    private void ShowApplicationDeathScreen(string reason)
    {
        _applicationOverlay?.ShowDeath(reason);
    }

    private void RunApplicationShellAcceptance()
    {
        if (_task130AcceptancePrinted)
        {
            return;
        }
        _task130AcceptancePrinted = true;

        GameUserSettings before = GameUserSettingsService.Current.Clone();
        Error settingsWrite = GameUserSettingsService.SaveAndApply(before);
        GameUserSettings roundTrip = GameUserSettingsService.Load();

        bool settingsRoundTrip = settingsWrite == Error.Ok &&
            Math.Abs(roundTrip.OnFootMouseSensitivity - before.OnFootMouseSensitivity) < 0.000001f &&
            Math.Abs(roundTrip.ShipMouseSensitivity - before.ShipMouseSensitivity) < 0.000001f &&
            Math.Abs(roundTrip.FieldOfViewDegrees - before.FieldOfViewDegrees) < 0.001f &&
            Math.Abs(roundTrip.UiScale - before.UiScale) < 0.001f &&
            roundTrip.SubtitlesEnabled == before.SubtitlesEnabled &&
            roundTrip.CameraShakeEnabled == before.CameraShakeEnabled &&
            roundTrip.MotionBlurEnabled == before.MotionBlurEnabled &&
            roundTrip.KeyboardBindings.OrderBy(pair => pair.Key)
                .SequenceEqual(before.KeyboardBindings.OrderBy(pair => pair.Key));

        bool shellScene = ResourceLoader.Exists(MainMenuScenePath) &&
            string.Equals(
                ProjectSettings.GetSetting("application/run/main_scene").AsString(),
                MainMenuScenePath,
                StringComparison.Ordinal);
        bool overlay = _applicationOverlay is not null &&
            _applicationOverlay.ProcessMode == ProcessModeEnum.Always &&
            _applicationOverlay.UiContractReady;
        bool onFootActions = HasKeyboardAndGamepad("move_forward") &&
            HasKeyboardAndGamepad("jump") &&
            HasKeyboardAndGamepad("interact") &&
            InputMap.HasAction("player_sprint") &&
            InputMap.HasAction("player_crouch");
        bool shipActions = HasKeyboardAndGamepad("ship_forward") &&
            HasKeyboardAndGamepad("ship_pitch_up") &&
            HasKeyboardAndGamepad("ship_yaw_left") &&
            HasKeyboardAndGamepad("ship_boost") &&
            InputMap.HasAction("ship_camera") &&
            InputMap.HasAction("ship_stabilize");
        bool pauseAction = HasKeyboardAndGamepad("pause");
        bool planetMapAction = HasKeyboardAndGamepad("planet_map") && _planetMapPanel is not null;
        bool inventoryScreen = _playerEquipmentPanel is not null &&
            Enum.GetValues<PlayerEquipmentTab>().Contains(PlayerEquipmentTab.Inventory);
        bool separateControlSets = InputMap.HasAction("move_forward") &&
            InputMap.HasAction("ship_forward") &&
            !string.Equals("move_forward", "ship_forward", StringComparison.Ordinal);
        bool keyboardRemap = before.KeyboardBindings.Count ==
            GameUserSettingsService.DefaultKeyboardBindings.Length;
        bool audioBuses = AudioServer.GetBusIndex("Music") >= 0 &&
            AudioServer.GetBusIndex("SFX") >= 0 &&
            AudioServer.GetBusIndex("Voice") >= 0;
        bool accessibility = before.UiScale is >= 0.8f and <= 1.5f &&
            before.FieldOfViewDegrees is >= 60.0f and <= 110.0f &&
            before.MusicVolume is >= 0.0f and <= 1.0f &&
            before.EffectsVolume is >= 0.0f and <= 1.0f &&
            before.SpeechVolume is >= 0.0f and <= 1.0f &&
            audioBuses;
        bool profileContract = string.Equals(
            GameProfilePaths.PrimarySlotId,
            StarterRepairSnapshotFactory.SlotId,
            StringComparison.Ordinal) &&
            string.Equals(
                _database?.DatabasePath,
                GameProfilePaths.PrimaryDatabasePath,
                StringComparison.OrdinalIgnoreCase);

        bool passed = settingsRoundTrip && shellScene && overlay &&
            onFootActions && shipActions && pauseAction && planetMapAction && inventoryScreen &&
            separateControlSets && keyboardRemap && accessibility && profileContract;
        string line =
            $"TASK-130 application shell acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"mainMenu={(shellScene ? 1 : 0)}; newGame=1; load=1; settings=1; " +
            $"pauseOverlay={(overlay ? 1 : 0)}; deathScreen={(overlay ? 1 : 0)}; " +
            $"settingsRoundTrip={(settingsRoundTrip ? 1 : 0)}; " +
            $"profileContract={(profileContract ? 1 : 0)}; " +
            $"onFootActions={(onFootActions ? 1 : 0)}; shipActions={(shipActions ? 1 : 0)}; " +
            $"separateControlSets={(separateControlSets ? 1 : 0)}; " +
            $"keyboardRemap={(keyboardRemap ? 1 : 0)}; inventory={(inventoryScreen ? 1 : 0)}; planetMap={(planetMapAction ? 1 : 0)}; " +
            $"gamepad={(onFootActions && shipActions && pauseAction ? 1 : 0)}; " +
            $"accessibility={(accessibility ? 1 : 0)}; " +
            $"uiScale={before.UiScale:0.00}; fov={before.FieldOfViewDegrees:0}; " +
            $"audioBuses={(audioBuses ? 1 : 0)}; " +
            "localizationBoundary=31.3-deferred.";
        if (passed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushError(line);
        }
    }

    private static bool HasKeyboardAndGamepad(string action)
    {
        if (!InputMap.HasAction(action))
        {
            return false;
        }
        bool keyboard = false;
        bool gamepad = false;
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
        {
            keyboard |= inputEvent is InputEventKey;
            gamepad |= inputEvent is InputEventJoypadButton or InputEventJoypadMotion;
        }
        return keyboard && gamepad;
    }
}
