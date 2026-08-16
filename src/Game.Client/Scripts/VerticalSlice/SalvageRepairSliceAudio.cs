using System;
using System.Globalization;
using System.Linq;
using Godot;

public partial class SalvageRepairSlice
{
    private AudioDirector? _audioDirector;
    private bool _task134AcceptancePrinted;
    private string _task134AcceptanceHud = "READY";
    private double _audioUiRescanElapsed;
    private double _lifeSupportAlarmCooldown;

    private void BindAudioRuntime()
    {
        _audioDirector = AudioDirector.EnsureInstalled(GetTree());
        _audioDirector.AttachUiSounds(this);
    }

    private void InitializeAudioGameplayRuntime()
    {
        if (_audioDirector is null)
        {
            _audioDirector = AudioDirector.EnsureInstalled(GetTree());
        }
        GameAudioEnvironment environment = ResolveAudioEnvironment();
        _audioDirector.SetEnvironment(environment, force: true);
        _audioDirector.SetMusicState(ResolveMusicState(environment));
        UpdateAudioVehicleLoop();
    }

    private void UpdateAudioRuntime(double delta)
    {
        if (_audioDirector is null || _stageOneVoyageRuntime is null ||
            _galaxyNavigationRuntime is null)
        {
            return;
        }

        GameAudioEnvironment environment = ResolveAudioEnvironment();
        _audioDirector.SetEnvironment(environment);
        _audioDirector.SetMusicState(ResolveMusicState(environment));
        UpdateAudioVehicleLoop();
        UpdateLifeSupportAudio(delta);

        _audioUiRescanElapsed += Math.Max(0.0, delta);
        if (_audioUiRescanElapsed >= 2.0)
        {
            _audioUiRescanElapsed = 0.0;
            _audioDirector.AttachUiSounds(this);
        }
    }

    private GameAudioEnvironment ResolveAudioEnvironment()
    {
        if (_player?.IsSwimming == true)
        {
            return GameAudioEnvironment.Water;
        }

        if (_stageOneVoyageRuntime is not null &&
            StageOneVoyage.Location == StageOneVoyageLocation.OrbitalStation)
        {
            return GameAudioEnvironment.Interior;
        }

        if (_stageOneVoyageRuntime is not null && StageOneVoyage.Piloted)
        {
            return _voyageShip?.InAtmosphere == true
                ? GameAudioEnvironment.Atmosphere
                : GameAudioEnvironment.Vacuum;
        }

        if (_galaxyNavigationRuntime is not null &&
            GalaxyNavigation.CurrentSystem.Planets.Count > 0 &&
            GalaxyNavigation.CurrentPlanet.HasAtmosphere)
        {
            return GameAudioEnvironment.Atmosphere;
        }

        return GameAudioEnvironment.Vacuum;
    }

    private GameMusicState ResolveMusicState(GameAudioEnvironment environment)
    {
        if (IsActiveShipCombatAudioContext())
        {
            return GameMusicState.Combat;
        }
        return environment switch
        {
            GameAudioEnvironment.Vacuum => GameMusicState.Space,
            GameAudioEnvironment.Interior => GameMusicState.Interior,
            _ => GameMusicState.Surface
        };
    }

    private bool IsActiveShipCombatAudioContext()
    {
        if (_voyageShip is null || _stageOneVoyageRuntime is null ||
            !StageOneVoyage.Piloted)
        {
            return false;
        }

        return _npcShipNavigationNodes.Any(node =>
            node.Role == NpcShipNavigationRole.HostileRaider &&
            node.GlobalPosition.DistanceTo(_voyageShip.GlobalPosition) <= 95.0f &&
            node.NavigationState is NpcShipNavigationState.Pursuit or
                NpcShipNavigationState.CombatApproach or
                NpcShipNavigationState.BreakAway or
                NpcShipNavigationState.Evade);
    }

    private void UpdateAudioVehicleLoop()
    {
        if (_audioDirector is null || _voyageShip is null ||
            _stageOneVoyageRuntime is null)
        {
            return;
        }

        bool active = StageOneVoyage.Piloted;
        float denominator = Math.Max(1.0f, _voyageShip.BoostMaxSpeed);
        float intensity = active
            ? Mathf.Clamp(_voyageShip.Velocity.Length() / denominator, 0.0f, 1.0f)
            : 0.0f;
        _audioDirector.SetVehicleLoop(active, intensity);
    }

    private void PlayPlayerWeaponAudio()
    {
        if (_audioDirector is null || _player is null)
        {
            return;
        }
        _audioDirector.PlayWorldCue(
            AudioCue.WeaponMultitool,
            _player.GlobalPosition + SurfaceLocalDirectionToWorld(Vector3.Up * 1.2f),
            externalInVacuum: true,
            priority: GameAudioPriority.High,
            maxDistance: 80.0f,
            volumeDb: -1.0f,
            pitchScale: 1.0f,
            bus: "SFX");
    }

    private void PlayResourceCollectAudio(Vector3 position)
    {
        _audioDirector?.PlayWorldCue(
            AudioCue.ResourceCollect,
            position,
            externalInVacuum: true,
            priority: GameAudioPriority.Normal,
            maxDistance: 46.0f,
            volumeDb: -2.0f,
            pitchScale: 1.0f,
            bus: "SFX");
    }

    private void PlayCraftCompletionAudio(Vector3 position)
    {
        _audioDirector?.PlayWorldCue(
            AudioCue.CraftComplete,
            position,
            externalInVacuum: true,
            priority: GameAudioPriority.High,
            maxDistance: 54.0f,
            volumeDb: -1.5f,
            pitchScale: 1.0f,
            bus: "SFX");
        _audioDirector?.PlayUiConfirm();
    }

    private void PlayDialogueVoiceAudio()
    {
        _audioDirector?.PlayVoiceRadio();
    }

    private void PlayPlayerDamageAudio()
    {
        _audioDirector?.PlayDamageAlert();
    }

    private void UpdateLifeSupportAudio(double delta)
    {
        _lifeSupportAlarmCooldown = Math.Max(0.0, _lifeSupportAlarmCooldown - Math.Max(0.0, delta));
        if (_audioDirector is null || _playerSurvivalRuntime is null ||
            (_stageOneVoyageRuntime is not null && StageOneVoyage.Piloted))
        {
            return;
        }

        PlayerSurvivalEffectiveStats stats = PlayerSurvival.GetEffectiveStats();
        double maximumOxygen = Math.Max(0.001, stats.MaximumOxygen);
        double oxygenRatio = PlayerSurvival.Oxygen / maximumOxygen;
        if (oxygenRatio <= 0.18 && _lifeSupportAlarmCooldown <= 0.0)
        {
            _audioDirector.PlayLifeSupportAlarm();
            _lifeSupportAlarmCooldown = 4.5;
        }
        else if (oxygenRatio >= 0.25)
        {
            _lifeSupportAlarmCooldown = 0.0;
        }
    }

    private string BuildAudioHudLine()
    {
        if (_audioDirector is null)
        {
            return LF(
                "ui.hud.audio",
                ("environment", L("audio.environment.unavailable")),
                ("music", L("audio.music.none")),
                ("active", 0),
                ("maximum", AudioDirector.MaximumTransientVoices),
                ("positional", 0),
                ("suppressed", 0));
        }

        AudioDirectorDiagnostics diagnostics = _audioDirector.CaptureDiagnostics();
        return LF(
            "ui.hud.audio",
            ("environment", L(AudioEnvironmentLocalizationKey(diagnostics.Environment))),
            ("music", L(AudioMusicLocalizationKey(diagnostics.MusicState))),
            ("active", diagnostics.ActiveTransientVoices),
            ("maximum", diagnostics.MaximumTransientVoices),
            ("positional", diagnostics.PositionalRequests),
            ("suppressed", diagnostics.VacuumSuppressed));
    }

    private static string AudioEnvironmentLocalizationKey(GameAudioEnvironment environment) =>
        environment switch
        {
            GameAudioEnvironment.Atmosphere => "audio.environment.atmosphere",
            GameAudioEnvironment.Vacuum => "audio.environment.vacuum",
            GameAudioEnvironment.Interior => "audio.environment.interior",
            GameAudioEnvironment.Water => "audio.environment.water",
            _ => "audio.environment.unavailable"
        };

    private static string AudioMusicLocalizationKey(GameMusicState state) =>
        state switch
        {
            GameMusicState.Menu => "audio.music.menu",
            GameMusicState.Surface => "audio.music.surface",
            GameMusicState.Space => "audio.music.space",
            GameMusicState.Interior => "audio.music.interior",
            GameMusicState.Combat => "audio.music.combat",
            _ => "audio.music.none"
        };

    private void RunAudioArchitectureAcceptance()
    {
        if (_task134AcceptancePrinted)
        {
            return;
        }
        _task134AcceptancePrinted = true;

        if (_audioDirector is null || _player is null)
        {
            _task134AcceptanceHud = "FAIL missing audio director/player";
            GD.PushError(
                "TASK-134 audio architecture acceptance FAIL: audioDirector=0 or player=0.");
            return;
        }

        AudioDirector director = _audioDirector;
        GameAudioEnvironment restoreEnvironment = ResolveAudioEnvironment();
        GameMusicState restoreMusic = ResolveMusicState(restoreEnvironment);
        AudioDirectorDiagnostics before = director.CaptureDiagnostics();

        bool busContract = director.ValidateBusContract(out string busDetail);
        bool poolContract = director.ValidatePoolContract(out string poolDetail);
        string[] requiredCues =
        {
            AudioCue.UiClick,
            AudioCue.UiConfirm,
            AudioCue.UiError,
            AudioCue.VoiceRadio,
            AudioCue.WeaponMultitool,
            AudioCue.ResourceCollect,
            AudioCue.CraftComplete,
            AudioCue.DamageAlert,
            AudioCue.LifeSupportAlarm,
            AudioCue.VehicleEngine,
            AudioCue.AmbientAtmosphere,
            AudioCue.AmbientInterior,
            AudioCue.AmbientWater,
            AudioCue.WeatherWind,
            AudioCue.MusicMenu,
            AudioCue.MusicSurface,
            AudioCue.MusicSpace,
            AudioCue.MusicInterior,
            AudioCue.MusicCombat
        };
        bool cueCoverage = requiredCues.All(director.HasCue);

        director.SetEnvironment(GameAudioEnvironment.Atmosphere, force: true);
        AudioDirectorDiagnostics atmosphere = director.CaptureDiagnostics();
        bool atmosphereProfile = atmosphere.AmbientPlaying &&
            atmosphere.WeatherPlaying &&
            director.IsEnvironmentFilterActive(GameAudioEnvironment.Atmosphere);

        Vector3 testPosition = _player.GlobalPosition + SurfaceLocalDirectionToWorld((Vector3.Right * 4.0f) + Vector3.Up);
        bool atmosphereExternal = director.PlayWorldCue(
            AudioCue.WeaponMultitool,
            testPosition,
            externalInVacuum: true,
            priority: GameAudioPriority.High,
            maxDistance: 64.0f,
            volumeDb: -7.0f);

        director.SetEnvironment(GameAudioEnvironment.Water, force: true);
        AudioDirectorDiagnostics water = director.CaptureDiagnostics();
        bool waterProfile = water.AmbientPlaying && !water.WeatherPlaying &&
            director.IsEnvironmentFilterActive(GameAudioEnvironment.Water);

        director.SetEnvironment(GameAudioEnvironment.Interior, force: true);
        AudioDirectorDiagnostics interior = director.CaptureDiagnostics();
        bool interiorProfile = interior.AmbientPlaying && !interior.WeatherPlaying &&
            director.IsEnvironmentFilterActive(GameAudioEnvironment.Interior);

        director.SetEnvironment(GameAudioEnvironment.Vacuum, force: true);
        AudioDirectorDiagnostics vacuumBefore = director.CaptureDiagnostics();
        bool externalSuppressed = !director.PlayWorldCue(
            AudioCue.WeaponMultitool,
            testPosition,
            externalInVacuum: true,
            priority: GameAudioPriority.High);
        bool internalAllowed = director.PlayWorldCue(
            AudioCue.VehicleEngine,
            testPosition,
            externalInVacuum: false,
            priority: GameAudioPriority.High,
            maxDistance: 32.0f,
            volumeDb: -12.0f,
            bus: "Vehicle");
        AudioDirectorDiagnostics vacuumAfter = director.CaptureDiagnostics();
        bool vacuumProfile = !vacuumAfter.AmbientPlaying && !vacuumAfter.WeatherPlaying &&
            externalSuppressed && internalAllowed &&
            vacuumAfter.VacuumSuppressed > vacuumBefore.VacuumSuppressed;

        director.PlayUiClick();
        director.PlayUiConfirm();
        director.PlayDamageAlert();
        director.PlayLifeSupportAlarm();
        director.PlayVoiceRadio();

        director.SetEnvironment(GameAudioEnvironment.Atmosphere, force: true);
        for (int i = 0; i < AudioDirector.ThreeDPoolSize + 5; i++)
        {
            director.PlayWorldCue(
                AudioCue.ResourceCollect,
                testPosition + (Vector3.Right * (i * 0.08f)),
                externalInVacuum: false,
                priority: GameAudioPriority.Low,
                maxDistance: 52.0f,
                volumeDb: -16.0f,
                pitchScale: 0.94f + (i * 0.003f));
        }
        for (int i = 0; i < AudioDirector.TwoDPoolSize + 3; i++)
        {
            director.PlayUiClick();
        }
        AudioDirectorDiagnostics stressed = director.CaptureDiagnostics();
        bool limiter = stressed.ActiveTransientVoices <=
            stressed.MaximumTransientVoices &&
            stressed.PoolSteals > before.PoolSteals;
        bool positional = atmosphereExternal &&
            stressed.PositionalRequests > before.PositionalRequests;
        bool layers = stressed.UiRequests > before.UiRequests &&
            stressed.VoiceRequests > before.VoiceRequests &&
            stressed.PlaybackRequests > before.PlaybackRequests;

        director.SetMusicState(GameMusicState.Menu);
        director.SetMusicState(GameMusicState.Surface);
        director.SetMusicState(GameMusicState.Space);
        director.SetMusicState(GameMusicState.Interior);
        director.SetMusicState(GameMusicState.Combat);
        AudioDirectorDiagnostics music = director.CaptureDiagnostics();
        bool musicStateMachine = music.MusicTransitions >= before.MusicTransitions + 5;

        bool settingsRouting =
            IsFiniteBusVolume("Music") &&
            IsFiniteBusVolume("Ambient") &&
            IsFiniteBusVolume("SFX") &&
            IsFiniteBusVolume("UI") &&
            IsFiniteBusVolume("Voice") &&
            IsFiniteBusVolume("Vehicle") &&
            IsFiniteBusVolume("Weather");

        director.SetEnvironment(restoreEnvironment, force: true);
        director.SetMusicState(restoreMusic);
        UpdateAudioVehicleLoop();

        bool passed = busContract && poolContract && cueCoverage &&
            atmosphereProfile && waterProfile && interiorProfile && vacuumProfile &&
            positional && limiter && layers && musicStateMachine && settingsRouting;

        _task134AcceptanceHud = passed
            ? "PASS"
            : "FAIL";
        string line =
            $"TASK-134 audio architecture acceptance {(passed ? "PASS" : "FAIL")}: " +
            $"buses={(busContract ? 8 : 0)}/8; cues={(cueCoverage ? requiredCues.Length : 0)}/{requiredCues.Length}; " +
            $"pool2d={AudioDirector.TwoDPoolSize}; pool3d={AudioDirector.ThreeDPoolSize}; " +
            $"activeTransient={stressed.ActiveTransientVoices}/{stressed.MaximumTransientVoices}; " +
            $"maxConcurrent={AudioDirector.MaximumConcurrentVoices}; " +
            $"poolSteals={stressed.PoolSteals - before.PoolSteals}; poolRejects={stressed.PoolRejects - before.PoolRejects}; " +
            $"positional={(positional ? 1 : 0)}; attenuation={(poolContract ? 1 : 0)}; " +
            $"atmosphere={(atmosphereProfile ? 1 : 0)}; water={(waterProfile ? 1 : 0)}; " +
            $"interior={(interiorProfile ? 1 : 0)}; vacuum={(vacuumProfile ? 1 : 0)}; " +
            $"externalVacuumSuppressed={(externalSuppressed ? 1 : 0)}; internalVacuumAllowed={(internalAllowed ? 1 : 0)}; " +
            $"musicCrossfade={(musicStateMachine ? 1 : 0)}; ui={(layers ? 1 : 0)}; voice={(layers ? 1 : 0)}; " +
            $"settingsRouting={(settingsRouting ? 1 : 0)}; " +
            $"busDetail={busDetail}; poolDetail={poolDetail}; " +
            $"restoreEnvironment={restoreEnvironment}; restoreMusic={restoreMusic}; " +
            $"sampleRate={ProceduralAudioBank.SampleRate}; proceduralBank=1; " +
            "result=section-32-audio-runtime.";
        if (passed)
        {
            GD.Print(line);
        }
        else
        {
            GD.PushError(line);
        }
    }

    private static bool IsFiniteBusVolume(string busName)
    {
        int index = AudioServer.GetBusIndex(busName);
        if (index < 0)
        {
            return false;
        }
        float db = AudioServer.GetBusVolumeDb(index);
        return float.IsFinite(db) && db is >= -80.0f and <= 24.0f;
    }
}
