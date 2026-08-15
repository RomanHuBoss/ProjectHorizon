using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public enum GameAudioEnvironment
{
    Atmosphere = 0,
    Vacuum = 1,
    Interior = 2,
    Water = 3
}

public enum GameMusicState
{
    None = 0,
    Menu = 1,
    Surface = 2,
    Space = 3,
    Interior = 4,
    Combat = 5
}

public enum GameAudioPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public readonly record struct AudioDirectorDiagnostics(
    int BusCount,
    int RequiredBuses,
    int TwoDPoolSize,
    int ThreeDPoolSize,
    int ActiveTransientVoices,
    int MaximumTransientVoices,
    int PlaybackRequests,
    int PoolSteals,
    int PoolRejects,
    int VacuumSuppressed,
    int PositionalRequests,
    int UiRequests,
    int VoiceRequests,
    int EnvironmentTransitions,
    int MusicTransitions,
    int UiButtonsHooked,
    GameAudioEnvironment Environment,
    GameMusicState MusicState,
    bool WaterFilterActive,
    bool InteriorFilterActive,
    bool AmbientPlaying,
    bool WeatherPlaying,
    bool VehiclePlaying);

public partial class AudioDirector : Node
{
    public const string NodeName = "ProjectHorizonAudioDirector";
    public const int TwoDPoolSize = 8;
    public const int ThreeDPoolSize = 16;
    public const int MaximumTransientVoices = TwoDPoolSize + ThreeDPoolSize;
    public const int DedicatedLoopVoiceCount = 5;
    public const int MaximumConcurrentVoices = MaximumTransientVoices + DedicatedLoopVoiceCount;
    public const float DefaultWorldMaxDistance = 72.0f;
    public const float MusicCrossfadeSeconds = 1.25f;

    public static readonly string[] RequiredBuses =
    {
        "Master",
        "Music",
        "Ambient",
        "SFX",
        "UI",
        "Voice",
        "Vehicle",
        "Weather"
    };

    private sealed class TwoDVoice
    {
        public required AudioStreamPlayer Player { get; init; }
        public GameAudioPriority Priority { get; set; }
        public ulong StartedTicks { get; set; }
    }

    private sealed class ThreeDVoice
    {
        public required AudioStreamPlayer3D Player { get; init; }
        public GameAudioPriority Priority { get; set; }
        public ulong StartedTicks { get; set; }
    }

    private readonly List<TwoDVoice> _twoDVoices = new();
    private readonly List<ThreeDVoice> _threeDVoices = new();
    private readonly HashSet<ulong> _hookedButtonIds = new();

    private AudioStreamPlayer? _musicA;
    private AudioStreamPlayer? _musicB;
    private AudioStreamPlayer? _ambient;
    private AudioStreamPlayer? _weather;
    private AudioStreamPlayer? _vehicle;
    private AudioStreamPlayer? _musicCurrent;
    private AudioStreamPlayer? _musicNext;
    private float _musicCrossfadeElapsed;
    private bool _musicCrossfadeActive;
    private GameMusicState _musicState = GameMusicState.None;
    private GameAudioEnvironment _environment = GameAudioEnvironment.Atmosphere;
    private AudioEffectLowPassFilter? _sfxLowPass;
    private AudioEffectLowPassFilter? _ambientLowPass;
    private AudioEffectLowPassFilter? _weatherLowPass;
    private AudioEffectLowPassFilter? _vehicleLowPass;
    private int _playbackRequests;
    private int _poolSteals;
    private int _poolRejects;
    private int _vacuumSuppressed;
    private int _positionalRequests;
    private int _uiRequests;
    private int _voiceRequests;
    private int _environmentTransitions;
    private int _musicTransitions;
    private bool _ready;

    public GameAudioEnvironment Environment => _environment;
    public GameMusicState MusicState => _musicState;

    public static AudioDirector EnsureInstalled(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Window root = tree.Root;
        AudioDirector? existing = root.GetNodeOrNull<AudioDirector>(NodeName);
        if (existing is not null)
        {
            existing.InitializeRuntime();
            return existing;
        }

        AudioDirector director = new()
        {
            Name = NodeName,
            ProcessMode = ProcessModeEnum.Always
        };
        root.AddChild(director);
        director.InitializeRuntime();
        return director;
    }

    public static void EnsureBusLayout()
    {
        foreach (string bus in RequiredBuses)
        {
            EnsureBus(bus);
        }

        SetBusSend("Music", "Master");
        SetBusSend("Ambient", "Master");
        SetBusSend("SFX", "Master");
        SetBusSend("UI", "Master");
        SetBusSend("Voice", "Master");
        SetBusSend("Vehicle", "Master");
        SetBusSend("Weather", "Master");
    }

    public override void _Ready()
    {
        InitializeRuntime();
    }

    private void InitializeRuntime()
    {
        if (_ready)
        {
            return;
        }

        ProcessMode = ProcessModeEnum.Always;
        EnsureBusLayout();
        ProceduralAudioBank.EnsureBuilt();
        BuildDedicatedPlayers();
        BuildPools();
        EnsureEnvironmentFilters();
        SetEnvironment(GameAudioEnvironment.Atmosphere, force: true);
        _ready = true;
        GD.Print(
            "TASK-134 audio architecture READY: " +
            $"buses={RequiredBuses.Length}; pool2d={TwoDPoolSize}; pool3d={ThreeDPoolSize}; " +
            $"maxTransient={MaximumTransientVoices}; maxConcurrent={MaximumConcurrentVoices}; cues={ProceduralAudioBank.All.Count}; " +
            "positional=AudioStreamPlayer3D; attenuation=max-distance; " +
            "environments=atmosphere/vacuum/interior/water; " +
            "vacuumExternalSuppression=enabled; musicCrossfade=enabled.");
    }

    public override void _Process(double delta)
    {
        UpdateMusicCrossfade((float)Math.Max(0.0, delta));
    }

    public void AttachUiSounds(Node root)
    {
        ArgumentNullException.ThrowIfNull(root);
        AttachUiSoundRecursive(root);
    }

    public void PlayUiClick() =>
        Play2D(AudioCue.UiClick, "UI", GameAudioPriority.Normal, -3.0f, 1.0f);

    public void PlayUiConfirm() =>
        Play2D(AudioCue.UiConfirm, "UI", GameAudioPriority.High, -1.5f, 1.0f);

    public void PlayUiError() =>
        Play2D(AudioCue.UiError, "UI", GameAudioPriority.High, -1.0f, 0.94f);

    public void PlayVoiceRadio() =>
        Play2D(AudioCue.VoiceRadio, "Voice", GameAudioPriority.High, -2.0f, 1.0f);

    public void PlayDamageAlert() =>
        Play2D(AudioCue.DamageAlert, "UI", GameAudioPriority.High, -1.0f, 0.92f);

    public void PlayLifeSupportAlarm() =>
        Play2D(AudioCue.LifeSupportAlarm, "UI", GameAudioPriority.Critical, -2.0f, 1.0f);

    public bool PlayWorldCue(
        string cueId,
        Vector3 worldPosition,
        bool externalInVacuum = true,
        GameAudioPriority priority = GameAudioPriority.Normal,
        float maxDistance = DefaultWorldMaxDistance,
        float volumeDb = 0.0f,
        float pitchScale = 1.0f,
        string bus = "SFX")
    {
        if (_environment == GameAudioEnvironment.Vacuum && externalInVacuum)
        {
            _vacuumSuppressed++;
            return false;
        }

        if (!ProceduralAudioBank.Contains(cueId))
        {
            _poolRejects++;
            GD.PushWarning($"Audio cue '{cueId}' is not registered.");
            return false;
        }

        ThreeDVoice? voice = AcquireThreeDVoice(priority);
        if (voice is null)
        {
            _poolRejects++;
            return false;
        }

        AudioStreamPlayer3D player = voice.Player;
        player.Stop();
        player.Stream = ProceduralAudioBank.Get(cueId);
        player.Bus = bus;
        player.GlobalPosition = worldPosition;
        player.UnitSize = 3.0f;
        player.MaxDistance = Math.Max(1.0f, maxDistance);
        player.VolumeDb = volumeDb;
        player.PitchScale = Mathf.Clamp(pitchScale, 0.25f, 4.0f);
        player.Play();
        voice.Priority = priority;
        voice.StartedTicks = Time.GetTicksMsec();
        _playbackRequests++;
        _positionalRequests++;
        return true;
    }

    public void SetEnvironment(GameAudioEnvironment environment, bool force = false)
    {
        if (!force && _environment == environment)
        {
            return;
        }

        _environment = environment;
        _environmentTransitions++;
        ApplyEnvironmentFilterProfile(environment);

        if (_ambient is null || _weather is null)
        {
            return;
        }

        switch (environment)
        {
            case GameAudioEnvironment.Atmosphere:
                StartLoop(_ambient, AudioCue.AmbientAtmosphere, "Ambient", -15.0f, 1.0f);
                StartLoop(_weather, AudioCue.WeatherWind, "Weather", -20.0f, 0.94f);
                break;
            case GameAudioEnvironment.Vacuum:
                _ambient.Stop();
                _weather.Stop();
                break;
            case GameAudioEnvironment.Interior:
                StartLoop(_ambient, AudioCue.AmbientInterior, "Ambient", -17.0f, 1.0f);
                _weather.Stop();
                break;
            case GameAudioEnvironment.Water:
                StartLoop(_ambient, AudioCue.AmbientWater, "Ambient", -13.0f, 0.80f);
                _weather.Stop();
                break;
        }
    }

    public void SetMusicState(GameMusicState state)
    {
        if (!_ready || state == _musicState)
        {
            return;
        }

        _musicState = state;
        _musicTransitions++;
        if (state == GameMusicState.None)
        {
            _musicA?.Stop();
            _musicB?.Stop();
            _musicCurrent = null;
            _musicNext = null;
            _musicCrossfadeActive = false;
            return;
        }

        string cue = state switch
        {
            GameMusicState.Menu => AudioCue.MusicMenu,
            GameMusicState.Surface => AudioCue.MusicSurface,
            GameMusicState.Space => AudioCue.MusicSpace,
            GameMusicState.Interior => AudioCue.MusicInterior,
            GameMusicState.Combat => AudioCue.MusicCombat,
            _ => AudioCue.MusicSurface
        };
        BeginMusicCrossfade(cue);
    }

    public void SetVehicleLoop(bool active, float intensity)
    {
        if (_vehicle is null)
        {
            return;
        }

        if (!active)
        {
            _vehicle.Stop();
            return;
        }

        float t = Mathf.Clamp(intensity, 0.0f, 1.0f);
        if (!_vehicle.Playing || _vehicle.Stream != ProceduralAudioBank.Get(AudioCue.VehicleEngine))
        {
            StartLoop(_vehicle, AudioCue.VehicleEngine, "Vehicle", -11.0f, 0.82f);
        }
        _vehicle.PitchScale = Mathf.Lerp(0.76f, 1.28f, t);
        _vehicle.VolumeDb = Mathf.Lerp(-16.0f, -5.5f, t);
    }

    public AudioDirectorDiagnostics CaptureDiagnostics()
    {
        int active = _twoDVoices.Count(voice => voice.Player.Playing) +
            _threeDVoices.Count(voice => voice.Player.Playing);
        return new AudioDirectorDiagnostics(
            AudioServer.BusCount,
            RequiredBuses.Length,
            _twoDVoices.Count,
            _threeDVoices.Count,
            active,
            MaximumTransientVoices,
            _playbackRequests,
            _poolSteals,
            _poolRejects,
            _vacuumSuppressed,
            _positionalRequests,
            _uiRequests,
            _voiceRequests,
            _environmentTransitions,
            _musicTransitions,
            _hookedButtonIds.Count,
            _environment,
            _musicState,
            IsEnvironmentFilterActive(GameAudioEnvironment.Water),
            IsEnvironmentFilterActive(GameAudioEnvironment.Interior),
            _ambient?.Playing ?? false,
            _weather?.Playing ?? false,
            _vehicle?.Playing ?? false);
    }

    public bool ValidateBusContract(out string result)
    {
        List<string> missing = RequiredBuses
            .Where(name => AudioServer.GetBusIndex(name) < 0)
            .ToList();
        if (missing.Count > 0)
        {
            result = "missing=" + string.Join(",", missing);
            return false;
        }

        foreach (string name in RequiredBuses.Where(name => name != "Master"))
        {
            int index = AudioServer.GetBusIndex(name);
            if (index < 0 || !string.Equals(
                AudioServer.GetBusSend(index).ToString(),
                "Master",
                StringComparison.Ordinal))
            {
                result = $"bus={name}; send={AudioServer.GetBusSend(index)}";
                return false;
            }
        }

        result = "buses=8/8;sends=Master";
        return true;
    }

    public bool HasCue(string cueId) => ProceduralAudioBank.Contains(cueId);

    public bool ValidatePoolContract(out string result)
    {
        bool twoD = _twoDVoices.Count == TwoDPoolSize &&
            _twoDVoices.All(voice => voice.Player.MaxPolyphony == 1);
        bool threeD = _threeDVoices.Count == ThreeDPoolSize &&
            _threeDVoices.All(voice =>
                voice.Player.MaxPolyphony == 1 &&
                voice.Player.MaxDistance > 0.0f &&
                voice.Player.UnitSize > 0.0f);
        result = $"2d={_twoDVoices.Count}/{TwoDPoolSize}; 3d={_threeDVoices.Count}/{ThreeDPoolSize}; maxTransient={MaximumTransientVoices}; attenuation={(threeD ? 1 : 0)}";
        return twoD && threeD;
    }

    public bool IsEnvironmentFilterActive(GameAudioEnvironment environment)
    {
        return environment switch
        {
            GameAudioEnvironment.Water =>
                IsBusFilterEnabled("SFX") &&
                IsBusFilterEnabled("Ambient") &&
                IsBusFilterEnabled("Weather"),
            GameAudioEnvironment.Interior =>
                IsBusFilterEnabled("Ambient") &&
                IsBusFilterEnabled("Vehicle"),
            _ => !IsBusFilterEnabled("SFX") &&
                 !IsBusFilterEnabled("Ambient") &&
                 !IsBusFilterEnabled("Weather")
        };
    }

    private void BuildDedicatedPlayers()
    {
        _musicA = Create2DPlayer("MusicA", "Music");
        _musicB = Create2DPlayer("MusicB", "Music");
        _ambient = Create2DPlayer("AmbientLoop", "Ambient");
        _weather = Create2DPlayer("WeatherLoop", "Weather");
        _vehicle = Create2DPlayer("VehicleInteriorLoop", "Vehicle");
        _musicA.VolumeDb = -80.0f;
        _musicB.VolumeDb = -80.0f;
    }

    private void BuildPools()
    {
        for (int i = 0; i < TwoDPoolSize; i++)
        {
            AudioStreamPlayer player = Create2DPlayer($"Transient2D_{i:00}", "SFX");
            player.MaxPolyphony = 1;
            _twoDVoices.Add(new TwoDVoice
            {
                Player = player,
                Priority = GameAudioPriority.Low,
                StartedTicks = 0
            });
        }

        for (int i = 0; i < ThreeDPoolSize; i++)
        {
            AudioStreamPlayer3D player = new()
            {
                Name = $"Transient3D_{i:00}",
                Bus = "SFX",
                MaxPolyphony = 1,
                MaxDistance = DefaultWorldMaxDistance,
                UnitSize = 3.0f
            };
            AddChild(player);
            _threeDVoices.Add(new ThreeDVoice
            {
                Player = player,
                Priority = GameAudioPriority.Low,
                StartedTicks = 0
            });
        }
    }

    private AudioStreamPlayer Create2DPlayer(string name, string bus)
    {
        AudioStreamPlayer player = new()
        {
            Name = name,
            Bus = bus,
            MaxPolyphony = 1
        };
        AddChild(player);
        return player;
    }

    private void EnsureEnvironmentFilters()
    {
        _sfxLowPass = EnsureLowPass("SFX");
        _ambientLowPass = EnsureLowPass("Ambient");
        _weatherLowPass = EnsureLowPass("Weather");
        _vehicleLowPass = EnsureLowPass("Vehicle");
    }

    private static AudioEffectLowPassFilter EnsureLowPass(string busName)
    {
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0)
        {
            throw new InvalidOperationException($"Audio bus '{busName}' is missing.");
        }

        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter existing)
            {
                return existing;
            }
        }

        AudioEffectLowPassFilter filter = new();
        filter.Set("cutoff_hz", 12000.0f);
        AudioServer.AddBusEffect(bus, filter);
        AudioServer.SetBusEffectEnabled(bus, AudioServer.GetBusEffectCount(bus) - 1, false);
        return filter;
    }

    private void ApplyEnvironmentFilterProfile(GameAudioEnvironment environment)
    {
        switch (environment)
        {
            case GameAudioEnvironment.Water:
                ConfigureFilter("SFX", _sfxLowPass, 1150.0f, true);
                ConfigureFilter("Ambient", _ambientLowPass, 900.0f, true);
                ConfigureFilter("Weather", _weatherLowPass, 800.0f, true);
                ConfigureFilter("Vehicle", _vehicleLowPass, 2500.0f, true);
                break;
            case GameAudioEnvironment.Interior:
                ConfigureFilter("SFX", _sfxLowPass, 9000.0f, false);
                ConfigureFilter("Ambient", _ambientLowPass, 6200.0f, true);
                ConfigureFilter("Weather", _weatherLowPass, 9000.0f, false);
                ConfigureFilter("Vehicle", _vehicleLowPass, 7200.0f, true);
                break;
            default:
                ConfigureFilter("SFX", _sfxLowPass, 12000.0f, false);
                ConfigureFilter("Ambient", _ambientLowPass, 12000.0f, false);
                ConfigureFilter("Weather", _weatherLowPass, 12000.0f, false);
                ConfigureFilter("Vehicle", _vehicleLowPass, 12000.0f, false);
                break;
        }
    }

    private static void ConfigureFilter(
        string busName,
        AudioEffectLowPassFilter? filter,
        float cutoffHz,
        bool enabled)
    {
        if (filter is null)
        {
            return;
        }
        filter.Set("cutoff_hz", cutoffHz);
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0)
        {
            return;
        }
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter candidate &&
                candidate.GetInstanceId() == filter.GetInstanceId())
            {
                AudioServer.SetBusEffectEnabled(bus, i, enabled);
                return;
            }
        }
    }

    private static bool IsBusFilterEnabled(string busName)
    {
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0)
        {
            return false;
        }
        for (int i = 0; i < AudioServer.GetBusEffectCount(bus); i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectLowPassFilter)
            {
                return AudioServer.IsBusEffectEnabled(bus, i);
            }
        }
        return false;
    }

    private void Play2D(
        string cueId,
        string bus,
        GameAudioPriority priority,
        float volumeDb,
        float pitchScale)
    {
        if (!ProceduralAudioBank.Contains(cueId))
        {
            _poolRejects++;
            return;
        }
        TwoDVoice? voice = AcquireTwoDVoice(priority);
        if (voice is null)
        {
            _poolRejects++;
            return;
        }
        AudioStreamPlayer player = voice.Player;
        player.Stop();
        player.Stream = ProceduralAudioBank.Get(cueId);
        player.Bus = bus;
        player.VolumeDb = volumeDb;
        player.PitchScale = Mathf.Clamp(pitchScale, 0.25f, 4.0f);
        player.Play();
        voice.Priority = priority;
        voice.StartedTicks = Time.GetTicksMsec();
        _playbackRequests++;
        if (string.Equals(bus, "UI", StringComparison.Ordinal))
        {
            _uiRequests++;
        }
        if (string.Equals(bus, "Voice", StringComparison.Ordinal))
        {
            _voiceRequests++;
        }
    }

    private TwoDVoice? AcquireTwoDVoice(GameAudioPriority priority)
    {
        TwoDVoice? free = _twoDVoices.FirstOrDefault(voice => !voice.Player.Playing);
        if (free is not null)
        {
            return free;
        }
        TwoDVoice? steal = _twoDVoices
            .Where(voice => voice.Priority <= priority)
            .OrderBy(voice => voice.Priority)
            .ThenBy(voice => voice.StartedTicks)
            .FirstOrDefault();
        if (steal is not null)
        {
            steal.Player.Stop();
            _poolSteals++;
        }
        return steal;
    }

    private ThreeDVoice? AcquireThreeDVoice(GameAudioPriority priority)
    {
        ThreeDVoice? free = _threeDVoices.FirstOrDefault(voice => !voice.Player.Playing);
        if (free is not null)
        {
            return free;
        }
        ThreeDVoice? steal = _threeDVoices
            .Where(voice => voice.Priority <= priority)
            .OrderBy(voice => voice.Priority)
            .ThenBy(voice => voice.StartedTicks)
            .FirstOrDefault();
        if (steal is not null)
        {
            steal.Player.Stop();
            _poolSteals++;
        }
        return steal;
    }

    private void StartLoop(
        AudioStreamPlayer player,
        string cueId,
        string bus,
        float volumeDb,
        float pitchScale)
    {
        AudioStream stream = ProceduralAudioBank.Get(cueId);
        if (player.Playing && player.Stream == stream &&
            string.Equals(player.Bus.ToString(), bus, StringComparison.Ordinal))
        {
            player.VolumeDb = volumeDb;
            player.PitchScale = pitchScale;
            return;
        }
        player.Stop();
        player.Stream = stream;
        player.Bus = bus;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchScale;
        player.Play();
    }

    private void BeginMusicCrossfade(string cueId)
    {
        if (_musicA is null || _musicB is null)
        {
            return;
        }
        AudioStreamPlayer next = ReferenceEquals(_musicCurrent, _musicA)
            ? _musicB
            : _musicA;
        next.Stop();
        next.Stream = ProceduralAudioBank.Get(cueId);
        next.Bus = "Music";
        next.VolumeDb = -80.0f;
        next.Play();

        if (_musicCurrent is null || !_musicCurrent.Playing)
        {
            next.VolumeDb = -8.5f;
            _musicCurrent = next;
            _musicNext = null;
            _musicCrossfadeActive = false;
            return;
        }

        _musicNext = next;
        _musicCrossfadeElapsed = 0.0f;
        _musicCrossfadeActive = true;
    }

    private void UpdateMusicCrossfade(float delta)
    {
        if (!_musicCrossfadeActive || _musicCurrent is null || _musicNext is null)
        {
            return;
        }
        _musicCrossfadeElapsed += delta;
        float t = Mathf.Clamp(_musicCrossfadeElapsed / MusicCrossfadeSeconds, 0.0f, 1.0f);
        _musicCurrent.VolumeDb = Mathf.Lerp(-8.5f, -80.0f, t);
        _musicNext.VolumeDb = Mathf.Lerp(-80.0f, -8.5f, t);
        if (t < 1.0f)
        {
            return;
        }
        _musicCurrent.Stop();
        _musicCurrent = _musicNext;
        _musicNext = null;
        _musicCrossfadeActive = false;
    }

    private void AttachUiSoundRecursive(Node node)
    {
        if (node is BaseButton button)
        {
            ulong id = button.GetInstanceId();
            if (_hookedButtonIds.Add(id))
            {
                button.Pressed += PlayUiClick;
            }
        }
        foreach (Node child in node.GetChildren())
        {
            AttachUiSoundRecursive(child);
        }
    }

    private static int EnsureBus(string busName)
    {
        int index = AudioServer.GetBusIndex(busName);
        if (index >= 0)
        {
            return index;
        }
        AudioServer.AddBus();
        index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, busName);
        return index;
    }

    private static void SetBusSend(string busName, string sendName)
    {
        int bus = AudioServer.GetBusIndex(busName);
        if (bus < 0)
        {
            return;
        }
        AudioServer.SetBusSend(bus, new StringName(sendName));
    }
}
